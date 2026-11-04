using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace UdpLobby
{
    /// <summary>
    /// 로비 클라이언트. 클라는 "요청"만 보내고(READY / START), 화면은 서버가 보내주는 LOBBY 로만 갱신한다.
    /// 그래서 준비 버튼을 눌러도 서버가 확인해줘야 화면에 "준비 완료"가 뜬다.
    /// </summary>
    public class UdpLobbyClient : UdpNode
    {
        public enum State { Idle, Connecting, InLobby, InGame }

        public struct LobbyEntry
        {
            public int Id;
            public string Name;
            public bool Ready;
        }

        [SerializeField] string serverAddress = "127.0.0.1";
        [SerializeField] int serverPort = 7777;
        [SerializeField] string playerName = "Player";
        [SerializeField] float heartbeatInterval = 1f;
        [SerializeField] float timeout = 5f;
        [SerializeField] bool connectOnStart;
        [SerializeField] bool showGui = true;
        [SerializeField] Vector2 windowPosition = new Vector2(12, 12);

        public State CurrentState { get; private set; } = State.Idle;
        public int MyId { get; private set; }
        public int HostId { get; private set; }
        public bool IsHost => MyId != 0 && MyId == HostId;
        public bool IsReady => Lobby.Exists(e => e.Id == MyId && e.Ready);
        public string Status { get; private set; } = "";
        public int GameSeed { get; private set; }
        public readonly List<LobbyEntry> Lobby = new List<LobbyEntry>();

        /// <summary>서버가 게임 시작을 확정했을 때 (시드, 플레이어 ID 순서). 여기에 실제 게임 시작을 연결하면 된다.</summary>
        public event Action<int, int[]> GameStarted;

        Peer server;
        int lobbyVersion;
        float connectStarted;
        float lastPing;

        void Start()
        {
            if (connectOnStart) Connect();
        }

        public void Configure(string address, int port, string name)
        {
            serverAddress = address;
            serverPort = port;
            playerName = name;
        }

        public void Connect()
        {
            if (CurrentState != State.Idle) return;
            if (!TryResolve(serverAddress.Trim(), out IPAddress address))
            {
                Status = "잘못된 서버 주소: " + serverAddress;
                return;
            }

            try { OpenSocket(0); }
            catch (SocketException e)
            {
                Status = "소켓 열기 실패: " + e.SocketErrorCode;
                return;
            }

            server = new Peer { EndPoint = new IPEndPoint(address, serverPort), LastHeard = Now };
            lobbyVersion = 0;
            MyId = 0;
            HostId = 0;
            Lobby.Clear();
            CurrentState = State.Connecting;
            connectStarted = Now;
            lastPing = Now;

            SendReliable(server, "HELLO|" + Clean(playerName));
            Status = $"{server.EndPoint} 접속 중...";
            Debug.Log($"[Client {playerName}] 접속 요청 → {server.EndPoint}");
        }

        public void Disconnect(string reason)
        {
            if (server != null && IsOpen) SendUnreliable(server, "LEAVE");
            CloseSocket();
            server = null;
            Lobby.Clear();
            MyId = 0;
            HostId = 0;
            CurrentState = State.Idle;
            Status = reason;
            Debug.Log($"[Client {playerName}] 연결 종료: {reason}");
        }

        /// <summary>준비 요청만 보낸다. 실제 상태는 서버의 LOBBY 응답으로 바뀐다.</summary>
        public void SetReady(bool ready)
        {
            if (CurrentState != State.InLobby || IsHost) return;
            SendReliable(server, "READY|" + (ready ? 1 : 0));
        }

        /// <summary>방장만. 시작할 수 있는지는 서버가 판단해서 GAME_START 또는 START_DENIED 로 답한다.</summary>
        public void RequestStart()
        {
            if (CurrentState != State.InLobby || !IsHost) return;
            SendReliable(server, "START");
            Status = "시작 요청 보냄...";
        }

        void Update()
        {
            if (!IsOpen) return;
            PumpNetwork();
            if (server == null) return; // 처리 도중 Disconnect 됐을 수 있음

            float now = Now;
            if (CurrentState == State.Connecting)
            {
                if (now - connectStarted > timeout)
                {
                    Disconnect("서버 응답 없음 (IP / 포트포워딩 / 방화벽 확인)");
                    return;
                }
            }
            else
            {
                if (now - server.LastHeard > timeout)
                {
                    Disconnect("서버와 연결이 끊겼어요");
                    return;
                }
                if (now - lastPing >= heartbeatInterval)
                {
                    lastPing = now;
                    SendUnreliable(server, "PING"); // 생존 신호 + 공유기 NAT 매핑 유지
                }
            }

            ResendPending(server);
        }

        protected override Peer ResolvePeer(IPEndPoint from, string body)
        {
            return server != null && from.Equals(server.EndPoint) ? server : null; // 서버가 아닌 곳에서 온 패킷은 무시
        }

        protected override void OnMessage(Peer from, string body)
        {
            string[] parts = body.Split('|');
            switch (parts[0])
            {
                case "WELCOME":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int id)) MyId = id;
                    if (CurrentState == State.Connecting) CurrentState = State.InLobby;
                    Status = "로비 입장";
                    Debug.Log($"[Client {playerName}] 입장 완료, 내 ID = {MyId}");
                    break;

                case "LOBBY":
                    ApplyLobby(parts);
                    break;

                case "START_DENIED":
                    Status = "시작 불가: " + DescribeDenied(parts.Length > 1 ? parts[1] : "");
                    Debug.Log($"[Client {playerName}] {Status}");
                    break;

                case "GAME_START":
                    if (CurrentState == State.InGame) break;
                    int.TryParse(parts.Length > 1 ? parts[1] : "0", out int seed);
                    var order = new List<int>();
                    if (parts.Length > 2)
                        foreach (var s in parts[2].Split(','))
                            if (int.TryParse(s, out int pid)) order.Add(pid);

                    GameSeed = seed;
                    CurrentState = State.InGame;
                    Status = "게임 시작!";
                    Debug.Log($"[Client {playerName}] 게임 시작! seed {seed}, 순서 {parts[2]}");
                    GameStarted?.Invoke(seed, order.ToArray());
                    break;

                case "REJECT":
                    Disconnect(parts.Length > 1 && parts[1] == "FULL" ? "방이 꽉 찼어요" : "이미 게임이 시작된 방이에요");
                    break;

                case "SERVER_CLOSED":
                    Disconnect("서버가 종료됐어요");
                    break;

                case "PONG":
                    break;
            }
        }

        void ApplyLobby(string[] parts)
        {
            // LOBBY|버전|방장ID|게임중|id:이름:준비;...
            if (parts.Length < 5 || !int.TryParse(parts[1], out int version)) return;
            if (version <= lobbyVersion) return; // 늦게 도착한 옛날 상태는 버림
            lobbyVersion = version;

            int.TryParse(parts[2], out int host);
            HostId = host;

            Lobby.Clear();
            foreach (var item in parts[4].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = item.Split(':');
                if (f.Length < 3 || !int.TryParse(f[0], out int pid)) continue;
                Lobby.Add(new LobbyEntry { Id = pid, Name = f[1], Ready = f[2] == "1" });
            }
        }

        static string DescribeDenied(string reason)
        {
            switch (reason)
            {
                case "NOT_HOST": return "방장만 시작할 수 있어요";
                case "NOT_ENOUGH_PLAYERS": return "2명 이상 필요해요";
                case "NOT_ALL_READY": return "아직 준비 안 한 사람이 있어요";
                case "ALREADY_STARTED": return "이미 시작됐어요";
                default: return reason;
            }
        }

        static bool TryResolve(string host, out IPAddress address)
        {
            if (IPAddress.TryParse(host, out address)) return true;
            try
            {
                foreach (var a in Dns.GetHostAddresses(host))
                    if (a.AddressFamily == AddressFamily.InterNetwork) { address = a; return true; }
            }
            catch (SocketException) { }
            catch (ArgumentException) { }
            address = null;
            return false;
        }

        void OnGUI()
        {
            if (!showGui) return;
            GUILayout.BeginArea(new Rect(windowPosition.x, windowPosition.y, 320, 330), GUI.skin.box);
            GUILayout.Label("UDP 로비 클라이언트");
            if (!string.IsNullOrEmpty(Status)) GUILayout.Label(Status);

            if (CurrentState == State.Idle)
            {
                GUILayout.Label("서버 IP (외부 IP 또는 도메인)");
                serverAddress = GUILayout.TextField(serverAddress);
                GUILayout.Label("포트");
                if (int.TryParse(GUILayout.TextField(serverPort.ToString()), out int p)) serverPort = p;
                GUILayout.Label("이름");
                playerName = GUILayout.TextField(playerName);
                if (GUILayout.Button("접속")) Connect();
            }
            else
            {
                GUILayout.Label($"내 ID: {MyId}{(IsHost ? "  (방장)" : "")}");
                GUILayout.Space(4);
                foreach (var e in Lobby)
                {
                    string state = e.Id == HostId ? "[방장]" : e.Ready ? "준비 완료" : "대기 중";
                    GUILayout.Label($"  #{e.Id} {e.Name}{(e.Id == MyId ? " (나)" : "")}  {state}");
                }
                GUILayout.Space(6);

                if (CurrentState == State.InLobby)
                {
                    if (IsHost)
                    {
                        if (GUILayout.Button("게임 시작")) RequestStart();
                    }
                    else if (GUILayout.Button(IsReady ? "준비 취소" : "준비"))
                    {
                        SetReady(!IsReady);
                    }
                }
                else if (CurrentState == State.InGame)
                {
                    GUILayout.Label($"게임 진행 중 (seed {GameSeed})");
                }

                if (GUILayout.Button("나가기")) Disconnect("직접 나감");
            }
            GUILayout.EndArea();
        }

        protected override void OnDestroy()
        {
            if (server != null && IsOpen) SendUnreliable(server, "LEAVE");
            base.OnDestroy();
        }
    }
}
