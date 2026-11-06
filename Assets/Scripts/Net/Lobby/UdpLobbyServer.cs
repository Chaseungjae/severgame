using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace UdpLobby
{
    public class LobbyPlayer : Peer
    {
        public int Id;
        public string Name;
        public bool Ready;
    }

    /// <summary>
    /// 로비 서버. 준비 상태의 "진짜 값"은 서버만 들고 있고, 바뀔 때마다 전원에게 LOBBY 로 알린다.
    ///
    /// 클라 → 서버
    ///   HELLO|이름        (신뢰) 접속 요청. 처음 들어온 사람이 방장
    ///   READY|1 / READY|0 (신뢰) 준비 / 준비 취소 (방장은 무시)
    ///   START             (신뢰) 방장의 시작 요청
    ///   PING              (비신뢰) 1초마다 생존 신호
    ///   LEAVE             (비신뢰) 나가기
    /// 서버 → 클라
    ///   WELCOME|내ID                              (신뢰)
    ///   LOBBY|버전|방장ID|게임중|id:이름:준비;...   (신뢰) 로비 상태 전체
    ///   START_DENIED|이유                         (신뢰) 시작 거부 (방장에게만)
    ///   GAME_START|시드|id,id,...                 (신뢰) 전원에게
    ///   PONG / REJECT|FULL|IN_GAME / SERVER_CLOSED (비신뢰)
    /// </summary>
    public class UdpLobbyServer : UdpNode
    {
        [SerializeField] int port = 7777;
        [SerializeField] int maxPlayers = 4;
        [SerializeField] float timeout = 5f;
        [SerializeField] bool startOnAwake = true;
        [SerializeField] bool showGui = true;

        readonly Dictionary<IPEndPoint, LobbyPlayer> players = new Dictionary<IPEndPoint, LobbyPlayer>();
        readonly List<LobbyPlayer> joinOrder = new List<LobbyPlayer>(); // 들어온 순서 (방장 승계용)
        int nextPlayerId = 1;
        int hostId;          // 0 = 방장 없음
        int lobbyVersion;    // LOBBY 메시지 순서가 뒤바뀌어 도착해도 클라가 옛날 것을 버릴 수 있게
        bool inGame;
        string status = "정지됨";
        string addressText = "";

        public bool InGame => inGame;
        public int PlayerCount => joinOrder.Count;

        void Start()
        {
            if (startOnAwake) StartServer();
        }

        public void StartServer()
        {
            if (IsOpen) return;
            try
            {
                OpenSocket(port);
                addressText = string.Join(", ", LocalAddresses());
                status = $"실행 중 (UDP {port})";
                Debug.Log($"[Server] UDP {port} 에서 대기 중. 내부 IP: {addressText}");
            }
            catch (SocketException e)
            {
                status = $"시작 실패: {e.SocketErrorCode} (포트 {port} 사용 중?)";
                Debug.LogError("[Server] " + status);
            }
        }

        void Update()
        {
            if (!IsOpen) return;
            PumpNetwork();

            float now = Now;
            for (int i = joinOrder.Count - 1; i >= 0; i--)
            {
                if (now - joinOrder[i].LastHeard <= timeout) continue;
                Debug.Log($"[Server] {joinOrder[i].Name}#{joinOrder[i].Id} 응답 없음 → 내보냄");
                RemovePlayer(joinOrder[i]);
            }

            foreach (var p in joinOrder) ResendPending(p);
        }

        protected override Peer ResolvePeer(IPEndPoint from, string body)
        {
            if (players.TryGetValue(from, out var known)) return known;

            // 처음 보는 주소는 HELLO 일 때만 받아준다
            if (body == null || !body.StartsWith("HELLO|")) return null;
            if (inGame) { SendRaw(from, "U|REJECT|IN_GAME"); return null; }
            if (players.Count >= maxPlayers) { SendRaw(from, "U|REJECT|FULL"); return null; }

            var player = new LobbyPlayer { EndPoint = from, Id = nextPlayerId++, LastHeard = Now };
            players[from] = player;
            joinOrder.Add(player);
            return player;
        }

        protected override void OnMessage(Peer peer, string body)
        {
            var p = (LobbyPlayer)peer;
            string[] parts = body.Split('|');

            switch (parts[0])
            {
                case "HELLO":
                    p.Name = parts.Length > 1 ? Clean(parts[1]) : "";
                    if (p.Name.Length == 0) p.Name = "Player";
                    if (hostId == 0) hostId = p.Id;
                    SendReliable(p, $"WELCOME|{p.Id}");
                    Debug.Log($"[Server] 입장: {p.Name}#{p.Id} ({p.EndPoint}){(p.Id == hostId ? " - 방장" : "")}");
                    BroadcastLobby();
                    break;

                case "READY":
                    if (inGame || p.Id == hostId) break;
                    bool ready = parts.Length > 1 && parts[1] == "1";
                    if (p.Ready == ready) break;
                    p.Ready = ready;
                    Debug.Log($"[Server] {p.Name}#{p.Id} {(ready ? "준비 완료" : "준비 취소")}");
                    BroadcastLobby();
                    break;

                case "START":
                    HandleStart(p);
                    break;

                case "PING":
                    SendUnreliable(p, "PONG");
                    break;

                case "LEAVE":
                    Debug.Log($"[Server] 퇴장: {p.Name}#{p.Id}");
                    RemovePlayer(p);
                    break;
            }
        }

        /// <summary>시작 가능 여부는 서버가 최종 판단한다 (클라 화면이 늦게 갱신됐을 수도 있으니까).</summary>
        void HandleStart(LobbyPlayer requester)
        {
            string reason = null;
            if (inGame) reason = "ALREADY_STARTED";
            else if (requester.Id != hostId) reason = "NOT_HOST";
            else if (joinOrder.Count < 2) reason = "NOT_ENOUGH_PLAYERS";
            else if (joinOrder.Exists(x => x.Id != hostId && !x.Ready)) reason = "NOT_ALL_READY";

            if (reason != null)
            {
                Debug.Log($"[Server] {requester.Name}#{requester.Id} 시작 요청 거부: {reason}");
                SendReliable(requester, "START_DENIED|" + reason);
                return;
            }

            inGame = true;
            int seed = Random.Range(1, int.MaxValue); // 모든 클라가 같은 랜덤을 쓰도록 서버가 정해서 내려준다
            var order = new StringBuilder();
            for (int i = 0; i < joinOrder.Count; i++)
            {
                if (i > 0) order.Append(',');
                order.Append(joinOrder[i].Id);
            }

            foreach (var p in joinOrder) SendReliable(p, $"GAME_START|{seed}|{order}");
            status = $"게임 진행 중 (seed {seed})";
            Debug.Log($"[Server] 게임 시작! 인원 {joinOrder.Count}, seed {seed}");
            BroadcastLobby();
        }

        void RemovePlayer(LobbyPlayer p)
        {
            if (!players.Remove(p.EndPoint)) return;
            joinOrder.Remove(p);

            if (p.Id == hostId)
            {
                hostId = joinOrder.Count > 0 ? joinOrder[0].Id : 0; // 가장 먼저 들어온 사람이 방장 승계
                if (hostId != 0)
                {
                    joinOrder[0].Ready = false;
                    Debug.Log($"[Server] 방장 변경 → {joinOrder[0].Name}#{hostId}");
                }
            }

            if (joinOrder.Count == 0)
            {
                inGame = false;
                status = $"실행 중 (UDP {port})";
            }
            BroadcastLobby();
        }

        void BroadcastLobby()
        {
            lobbyVersion++;
            var sb = new StringBuilder();
            sb.Append("LOBBY|").Append(lobbyVersion).Append('|').Append(hostId).Append('|').Append(inGame ? 1 : 0).Append('|');
            for (int i = 0; i < joinOrder.Count; i++)
            {
                var p = joinOrder[i];
                if (i > 0) sb.Append(';');
                sb.Append(p.Id).Append(':').Append(p.Name).Append(':').Append(p.Ready ? 1 : 0);
            }

            string message = sb.ToString();
            foreach (var p in joinOrder) SendReliable(p, message);
        }

        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(ua.Address.ToString());
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Server] IP 목록을 가져오지 못함: " + e.Message);
            }
            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }

        void OnGUI()
        {
            if (!showGui) return;
            GUILayout.BeginArea(new Rect(Screen.width - 332, 12, 320, 260), GUI.skin.box);
            GUILayout.Label("UDP 로비 서버");
            GUILayout.Label("상태: " + status);
            GUILayout.Label($"포트: {port}   내부 IP: {addressText}");
            GUILayout.Label("외부 접속: 공유기에서 UDP " + port + " 포트포워딩 필요");
            GUILayout.Space(6);
            GUILayout.Label($"플레이어 {joinOrder.Count}/{maxPlayers}");
            foreach (var p in joinOrder)
            {
                string state = p.Id == hostId ? "[방장]" : p.Ready ? "준비 완료" : "대기 중";
                GUILayout.Label($"  #{p.Id} {p.Name}  {state}  ({p.EndPoint})");
            }
            if (!IsOpen && GUILayout.Button("서버 시작")) StartServer();
            GUILayout.EndArea();
        }

        protected override void OnDestroy()
        {
            foreach (var p in joinOrder) SendUnreliable(p, "SERVER_CLOSED");
            base.OnDestroy();
        }
    }
}
