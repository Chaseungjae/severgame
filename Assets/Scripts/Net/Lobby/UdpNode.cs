using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UdpLobby
{
    /// <summary>
    /// 상대 한 명(서버 입장에선 클라이언트 하나, 클라이언트 입장에선 서버)에 대한 연결 정보.
    /// </summary>
    public class Peer
    {
        public IPEndPoint EndPoint;
        public float LastHeard;                       // 마지막으로 패킷을 받은 시각 (타임아웃 판정용)
        public int NextSeq = 1;                       // 다음에 보낼 신뢰 메시지 번호
        public readonly Dictionary<int, PendingPacket> Pending = new Dictionary<int, PendingPacket>(); // ACK 대기 중
        public readonly HashSet<int> ReceivedSeqs = new HashSet<int>(); // 이미 처리한 번호 (중복 제거)
    }

    public class PendingPacket
    {
        public byte[] Data;
        public float LastSent;
    }

    /// <summary>
    /// 서버와 클라이언트가 공통으로 쓰는 UDP 소켓 + 간단한 신뢰성 계층.
    ///
    /// 패킷 형식 (UTF-8 텍스트)
    ///   R|seq|본문   신뢰 메시지 : 받은 쪽은 반드시 "A|seq" 로 응답하고, 같은 seq 는 한 번만 처리한다.
    ///                             보낸 쪽은 ACK 가 올 때까지 일정 간격으로 재전송한다.
    ///   A|seq        ACK (수신 확인)
    ///   U|본문       비신뢰 메시지 : 잃어버려도 괜찮은 것 (PING/PONG 등)
    ///   본문 = "TYPE|인자|인자..."
    ///
    /// 수신은 별도 스레드에서 블로킹으로 받고, 유니티 API 는 메인 스레드에서만 써야 하므로
    /// 받은 패킷은 큐에 넣었다가 Update 의 PumpNetwork() 에서 처리한다.
    /// </summary>
    public abstract class UdpNode : MonoBehaviour
    {
        const float ResendInterval = 0.25f;
        const int MaxPacketSize = 4096;
        // Windows 전용: 상대가 꺼진 뒤 보낸 패킷의 ICMP 응답 때문에 ReceiveFrom 이 10054 에러로 깨지는 것을 막는다.
        const int SIO_UDP_CONNRESET = -1744830452;

        protected Socket socket;
        Thread receiveThread;
        volatile bool running;
        readonly ConcurrentQueue<KeyValuePair<IPEndPoint, string>> inbox = new ConcurrentQueue<KeyValuePair<IPEndPoint, string>>();

        protected bool IsOpen => socket != null;
        protected static float Now => Time.realtimeSinceStartup;

        protected virtual void Awake()
        {
            // 창이 포커스를 잃어도 Update 가 돌아야 하트비트가 끊기지 않는다 (한 PC 에서 2개 띄워 테스트할 때 필수).
            Application.runInBackground = true;
        }

        /// <summary>port 0 이면 OS 가 빈 포트를 골라준다 (클라이언트용).</summary>
        protected void OpenSocket(int port)
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try { s.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null); }
            catch (Exception) { /* Windows 가 아니면 지원하지 않음 - 무시 */ }

            s.Bind(new IPEndPoint(IPAddress.Any, port)); // 0.0.0.0 : 모든 네트워크 카드(외부 IP 포함)에서 받기
            socket = s;
            running = true;
            receiveThread = new Thread(() => ReceiveLoop(s)) { IsBackground = true, Name = GetType().Name + " Receive" };
            receiveThread.Start();
        }

        protected void CloseSocket()
        {
            running = false;
            socket?.Close(); // 블로킹 중인 ReceiveFrom 이 예외를 던지며 빠져나온다
            socket = null;
            receiveThread?.Join(500);
            receiveThread = null;
            while (inbox.TryDequeue(out _)) { }
        }

        void ReceiveLoop(Socket s)
        {
            var buffer = new byte[MaxPacketSize];
            while (running)
            {
                try
                {
                    EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    int length = s.ReceiveFrom(buffer, ref remote);
                    var from = (IPEndPoint)remote;
                    inbox.Enqueue(new KeyValuePair<IPEndPoint, string>(
                        new IPEndPoint(from.Address, from.Port),
                        Encoding.UTF8.GetString(buffer, 0, length)));
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset ||
                                                e.SocketErrorCode == SocketError.MessageSize)
                {
                    // 상대가 꺼짐 / 너무 큰 패킷 → 그 패킷만 버리고 계속 받는다
                }
                catch (SocketException)
                {
                    if (!running) break;
                    Thread.Sleep(10);
                }
                catch (ObjectDisposedException)
                {
                    break; // CloseSocket() 으로 소켓이 닫힘
                }
            }
        }

        // ---------------------------------------------------------------- 송신

        protected void SendRaw(IPEndPoint to, string text) => SendBytes(to, Encoding.UTF8.GetBytes(text));

        void SendBytes(IPEndPoint to, byte[] data)
        {
            if (socket == null) return;
            try { socket.SendTo(data, to); }
            catch (SocketException e) { Debug.LogWarning($"[{GetType().Name}] 송신 실패 {to}: {e.SocketErrorCode}"); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>도착할 때까지 재전송하는 메시지 (READY, START, GAME_START 처럼 잃어버리면 안 되는 것).</summary>
        protected void SendReliable(Peer peer, string body)
        {
            int seq = peer.NextSeq++;
            var data = Encoding.UTF8.GetBytes($"R|{seq}|{body}");
            peer.Pending[seq] = new PendingPacket { Data = data, LastSent = Now };
            SendBytes(peer.EndPoint, data);
        }

        protected void SendUnreliable(Peer peer, string body) => SendRaw(peer.EndPoint, "U|" + body);

        /// <summary>ACK 를 못 받은 신뢰 메시지를 재전송. 매 프레임 호출.</summary>
        protected void ResendPending(Peer peer)
        {
            float now = Now;
            foreach (var packet in peer.Pending.Values)
            {
                if (now - packet.LastSent < ResendInterval) continue;
                packet.LastSent = now;
                SendBytes(peer.EndPoint, packet.Data);
            }
        }

        // ---------------------------------------------------------------- 수신 처리 (메인 스레드)

        protected void PumpNetwork()
        {
            while (inbox.TryDequeue(out var packet))
            {
                IPEndPoint from = packet.Key;
                if (!TryParse(packet.Value, out char kind, out int seq, out string body)) continue;

                Peer peer = ResolvePeer(from, body);
                if (peer == null) continue; // 모르는 상대
                peer.LastHeard = Now;

                try
                {
                    switch (kind)
                    {
                        case 'A':
                            peer.Pending.Remove(seq);
                            break;
                        case 'R':
                            SendRaw(from, "A|" + seq);              // 중복이어도 ACK 는 다시 보낸다 (이전 ACK 가 유실됐을 수 있음)
                            if (peer.ReceivedSeqs.Add(seq)) OnMessage(peer, body);
                            break;
                        case 'U':
                            OnMessage(peer, body);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[{GetType().Name}] 잘못된 패킷 무시 ({from}): {packet.Value}\n{e.Message}");
                }
            }
        }

        static bool TryParse(string text, out char kind, out int seq, out string body)
        {
            kind = '\0'; seq = 0; body = null;
            if (text.Length < 2 || text[1] != '|') return false;
            kind = text[0];
            switch (kind)
            {
                case 'U':
                    body = text.Substring(2);
                    return true;
                case 'A':
                    return int.TryParse(text.Substring(2), out seq);
                case 'R':
                    int bar = text.IndexOf('|', 2);
                    if (bar < 0 || !int.TryParse(text.Substring(2, bar - 2), out seq)) return false;
                    body = text.Substring(bar + 1);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>보낸 주소에 해당하는 Peer 를 찾는다. null 이면 패킷을 버린다. (ACK 패킷이면 body == null)</summary>
        protected abstract Peer ResolvePeer(IPEndPoint from, string body);

        /// <summary>"TYPE|인자..." 형태의 본문 하나를 처리한다.</summary>
        protected abstract void OnMessage(Peer from, string body);

        /// <summary>이름 등 사용자 입력에서 구분자 문자를 제거한다.</summary>
        protected static string Clean(string text, int maxLength = 16)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? "")
                if (c != '|' && c != ';' && c != ':' && !char.IsControl(c)) sb.Append(c);
            string result = sb.ToString().Trim();
            return result.Length > maxLength ? result.Substring(0, maxLength) : result;
        }

        protected virtual void OnDestroy() => CloseSocket();
    }
}
