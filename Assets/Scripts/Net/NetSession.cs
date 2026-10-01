using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MiniParty
{
    public enum MsgType : byte
    {
        Hello = 1,      // client → host : A = my character
        Welcome,        // host → client : A = host character, B = client character
        StartRound,     // host → client : A = round, B = minigame index, C = seed
        Barrier,        // client → host : A = barrier tag
        Go,             // host → client : A = barrier tag
        Action,         // both ways     : A = kind, B = player, C = value, D = round, F = float value
        RoundResult,    // host → client : A = winner, B = damage
    }

    public struct NetMsg
    {
        public MsgType Type;
        public int A, B, C, D;
        public float F;
    }

    /// <summary>
    /// Online 1v1. Connect through Unity Relay (quick match, or a private room shared by its 6-letter code) or by a
    /// direct IP connection (LAN / VPN / port forwarding, no cloud setup needed). The host is P1 (index 0) and
    /// the authority for results; the client is P2.
    /// All game traffic is a single named message carrying <see cref="NetMsg"/>.
    /// </summary>
    public class NetSession : INetLink
    {
        const string Channel = "mp";

        public bool IsHost { get; private set; }
        public int LocalIndex => IsHost ? 0 : 1;
        public string Status { get; private set; } = "";
        public bool Disconnected { get; private set; }

        /// <summary>Round number stamped on game actions so late packets from a previous minigame are dropped.</summary>
        public int CurrentRound;
        public MiniGameBase ActiveGame;

        ISession session;
        bool hooked;
        readonly List<NetMsg> inbox = new List<NetMsg>();

        public static void EnsureNetworkManager()
        {
            if (NetworkManager.Singleton != null) return;
            var go = new GameObject("NetworkManager");
            var transport = go.AddComponent<UnityTransport>();
            var nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig ??= new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
        }

        public enum Mode
        {
            QuickMatch,   // Relay: join any open room or create one
            CreateRoom,   // Relay: private room, share the 6-letter code
            JoinRoom,     // Relay: join by code
            HostDirect,   // direct IP: wait for a friend on this PC (LAN / VPN / port forwarding)
            JoinDirect,   // direct IP: connect to the host's address
        }

        public const ushort DirectPort = 7777;

        /// <summary>Room code (relay) or this PC's address (direct host) to give to the friend.</summary>
        public string ShareText { get; private set; } = "";

        public async Task<bool> ConnectAsync(Mode mode, string arg, CancellationToken ct)
        {
            try
            {
                EnsureNetworkManager();
                var nm = NetworkManager.Singleton;
                while (nm.ShutdownInProgress) await Task.Delay(50);

                switch (mode)
                {
                    case Mode.HostDirect:
                        Transport.SetConnectionData(true, "127.0.0.1", DirectPort, "0.0.0.0");
                        IsHost = true;
                        if (!nm.StartHost()) return Fail("방을 열 수 없어요 (포트 7777 사용 중?)");
                        ShareText = string.Join("  /  ", LocalAddresses());
                        return await WaitForPeer(ct, float.MaxValue, "친구가 IP로 들어오길 기다리는 중...");

                    case Mode.JoinDirect:
                    {
                        string host = (arg ?? "").Trim();
                        ushort port = DirectPort;
                        int colon = host.LastIndexOf(':');
                        if (colon > 0 && ushort.TryParse(host.Substring(colon + 1), out var parsed))
                        {
                            port = parsed;
                            host = host.Substring(0, colon);
                        }
                        Transport.SetConnectionData(true, host, port);
                        IsHost = false;
                        if (!nm.StartClient()) return Fail("접속을 시작할 수 없어요");
                        return await WaitForPeer(ct, 10f, $"{host}:{port} 에 접속 중...");
                    }
                }

                // ----- Unity Relay modes -----
                if (string.IsNullOrEmpty(Application.cloudProjectId))
                    return Fail("Unity Cloud 연결이 안 된 빌드예요\n(대신 IP 직접 연결을 써 주세요)");
                await SignIn();
                if (ct.IsCancellationRequested) return false;

                if (mode == Mode.CreateRoom)
                {
                    Status = "방 만드는 중...";
                    var options = new SessionOptions { MaxPlayers = 2, IsPrivate = true }.WithRelayNetwork();
                    session = await MultiplayerService.Instance.CreateSessionAsync(options);
                    IsHost = true;
                    ShareText = session.Code;
                    return await WaitForPeer(ct, float.MaxValue, "친구에게 코드를 보내고 기다리는 중...");
                }

                if (mode == Mode.JoinRoom)
                {
                    Status = "방 찾는 중...";
                    session = await MultiplayerService.Instance.JoinSessionByCodeAsync((arg ?? "").Trim().ToUpperInvariant());
                    IsHost = session.IsHost;
                    return await WaitForPeer(ct, 15f, "방에 접속 중...");
                }

                // quick match
                while (!ct.IsCancellationRequested)
                {
                    Status = "상대 찾는 중...";
                    var quickJoin = new QuickJoinOptions { CreateSession = true, Timeout = TimeSpan.FromSeconds(3) };
                    var sessionOptions = new SessionOptions { MaxPlayers = 2 }.WithRelayNetwork();
                    session = await MultiplayerService.Instance.MatchmakeSessionAsync(quickJoin, sessionOptions);
                    IsHost = session.IsHost;

                    // two players creating rooms at the same moment would wait forever, so a lonely host retries
                    float wait = IsHost ? UnityEngine.Random.Range(12f, 20f) : 15f;
                    if (await WaitForPeer(ct, wait, IsHost ? "상대 기다리는 중..." : "상대에게 연결 중...", leaveOnTimeout: false)) return true;
                    if (ct.IsCancellationRequested) return false;

                    await LeaveAsync();
                    while (nm.ShutdownInProgress) await Task.Delay(50);
                    Disconnected = false;
                    inbox.Clear();
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                await LeaveAsync();
                return Fail(FriendlyError(e));
            }
        }

        static UnityTransport Transport => (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        bool Fail(string status)
        {
            Status = status;
            return false;
        }

        static string FriendlyError(Exception e)
        {
            string m = e.Message ?? "";
            if (m.Contains("not found") || m.Contains("NotFound")) return "그 코드의 방을 찾을 수 없어요";
            if (m.Contains("full") || m.Contains("Full")) return "방이 이미 꽉 찼어요";
            return "연결 실패: " + m;
        }

        async Task SignIn()
        {
            Status = "서비스 연결 중...";
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // Unique profile per launch so two copies on one PC sign in as different players.
                var options = new InitializationOptions().SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 12));
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Status = "로그인 중...";
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        /// <summary>Waits until both PCs are connected and the message handler is registered.</summary>
        async Task<bool> WaitForPeer(CancellationToken ct, float timeout, string waiting, bool leaveOnTimeout = true)
        {
            var nm = NetworkManager.Singleton;
            float started = Time.realtimeSinceStartup;
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    await LeaveAsync();
                    return false;
                }
                // register as early as possible so the first handshake message is never dropped
                if (!hooked && nm.IsListening && nm.CustomMessagingManager != null) Hook(nm);

                bool ready = IsHost ? nm.IsListening && nm.ConnectedClientsIds.Count >= 2 : nm.IsConnectedClient;
                if (ready && hooked)
                {
                    Status = "연결 완료!";
                    return true;
                }
                float waited = Time.realtimeSinceStartup - started;
                bool clientGaveUp = !IsHost && waited > 1f && !nm.IsListening && !nm.ShutdownInProgress; // connection refused / dropped
                if (waited > timeout || clientGaveUp)
                {
                    if (leaveOnTimeout)
                    {
                        await LeaveAsync();
                        Status = IsHost ? "상대가 오지 않았어요" : "접속하지 못했어요 (주소/코드를 확인해 주세요)";
                    }
                    return false;
                }
                Status = waiting;
                await Task.Delay(100);
            }
        }

        /// <summary>This PC's LAN / VPN IPv4 addresses (what a friend types for a direct connection).</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            list.Add(ua.Address.ToString());
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetSession] could not list addresses: {e.Message}");
            }
            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }

        void Hook(NetworkManager nm)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Channel, OnMessage);
            nm.OnClientDisconnectCallback += OnClientDisconnect;
            hooked = true;
        }

        void OnClientDisconnect(ulong clientId)
        {
            // Host: the opponent left. Client: we lost the host.
            Disconnected = true;
        }

        void OnMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte type);
            reader.ReadValueSafe(out int a);
            reader.ReadValueSafe(out int b);
            reader.ReadValueSafe(out int c);
            reader.ReadValueSafe(out int d);
            reader.ReadValueSafe(out float f);
            var msg = new NetMsg { Type = (MsgType)type, A = a, B = b, C = c, D = d, F = f };

            if (msg.Type == MsgType.Action)
            {
                if (msg.D == CurrentRound && ActiveGame != null)
                    ActiveGame.ReceiveAction(new GameAction { Kind = msg.A, Player = msg.B, A = msg.C, F = msg.F });
                return; // stale actions from an earlier round are dropped
            }
            inbox.Add(msg);
        }

        public void Send(NetMsg msg)
        {
            var nm = NetworkManager.Singleton;
            if (Disconnected || nm == null || !nm.IsListening || nm.CustomMessagingManager == null) return;

            using var writer = new FastBufferWriter(32, Allocator.Temp);
            writer.WriteValueSafe((byte)msg.Type);
            writer.WriteValueSafe(msg.A);
            writer.WriteValueSafe(msg.B);
            writer.WriteValueSafe(msg.C);
            writer.WriteValueSafe(msg.D);
            writer.WriteValueSafe(msg.F);
            nm.CustomMessagingManager.SendNamedMessage(Channel, IsHost ? OpponentClientId(nm) : NetworkManager.ServerClientId, writer);
        }

        static ulong OpponentClientId(NetworkManager nm)
        {
            foreach (var id in nm.ConnectedClientsIds)
                if (id != nm.LocalClientId) return id;
            return NetworkManager.ServerClientId;
        }

        public void SendAction(GameAction act)
        {
            Send(new NetMsg { Type = MsgType.Action, A = act.Kind, B = act.Player, C = act.A, D = CurrentRound, F = act.F });
        }

        public bool TryTake(MsgType type, out NetMsg msg, int? a = null)
        {
            for (int i = 0; i < inbox.Count; i++)
            {
                if (inbox[i].Type != type || (a.HasValue && inbox[i].A != a.Value)) continue;
                msg = inbox[i];
                inbox.RemoveAt(i);
                return true;
            }
            msg = default;
            return false;
        }

        /// <summary>Waits for a message of the given type (stops early if the connection drops).</summary>
        public IEnumerator WaitFor(MsgType type, Action<NetMsg> received, int? a = null)
        {
            NetMsg msg;
            while (!TryTake(type, out msg, a))
            {
                if (Disconnected) yield break;
                yield return null;
            }
            received(msg);
        }

        /// <summary>Both sides reach this point before either continues (host releases the client with Go).</summary>
        public IEnumerator Barrier(int tag)
        {
            if (IsHost)
            {
                NetMsg _;
                while (!TryTake(MsgType.Barrier, out _, tag))
                {
                    if (Disconnected) yield break;
                    yield return null;
                }
                Send(new NetMsg { Type = MsgType.Go, A = tag });
            }
            else
            {
                Send(new NetMsg { Type = MsgType.Barrier, A = tag });
                yield return WaitFor(MsgType.Go, _ => { }, tag);
            }
        }

        public async Task LeaveAsync()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                if (hooked)
                {
                    nm.CustomMessagingManager?.UnregisterNamedMessageHandler(Channel);
                    nm.OnClientDisconnectCallback -= OnClientDisconnect;
                    hooked = false;
                }
            }
            try
            {
                if (session != null) await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetSession] Leave failed: {e.Message}");
            }
            session = null;
            if (nm != null && nm.IsListening) nm.Shutdown();
        }
    }
}
