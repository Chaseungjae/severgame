using System;
using System.Collections.Generic;

namespace MiniParty
{
    /// <summary>
    /// 상대 한 명에 대한 신뢰성 계층 (Seq · Ack · 재전송 · 중복 제거).
    /// 소켓을 직접 들고 있지 않고 "문자열 하나를 보내는 방법"만 받아서 쓰므로
    /// 서버(여러 상대)와 클라이언트(상대는 서버 하나) 양쪽에서 똑같이 쓴다.
    /// </summary>
    public class ReliableChannel
    {
        class Pending
        {
            public string Text = "";
            public float LastSent;
        }

        readonly Action<string> send;
        readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        readonly HashSet<int> handled = new HashSet<int>();
        readonly List<int> resendScratch = new List<int>();

        int nextSeq = 1;

        /// <summary>마지막으로 이 상대에게서 패킷을 받은 시각.</summary>
        public float LastHeard { get; private set; }

        public ReliableChannel(Action<string> send, float now)
        {
            this.send = send;
            LastHeard = now;
        }

        public bool TimedOut(float now) => now - LastHeard > UdpProtocol.Timeout;

        /// <summary>도착할 때까지 재전송하는 메시지.</summary>
        public void SendReliable(string body, float now)
        {
            int seq = nextSeq++;
            string text = UdpProtocol.Reliable(seq, body);
            pending[seq] = new Pending { Text = text, LastSent = now };
            send(text);
        }

        /// <summary>잃어버려도 되는 메시지 (PING/PONG 등).</summary>
        public void SendUnreliable(string body) => send(UdpProtocol.Unreliable(body));

        /// <summary>ACK 를 못 받은 신뢰 메시지를 재전송한다. 매 틱 호출.</summary>
        public void Update(float now)
        {
            if (pending.Count == 0) return;

            resendScratch.Clear();
            foreach (var pair in pending)
                if (now - pair.Value.LastSent >= UdpProtocol.ResendInterval) resendScratch.Add(pair.Key);

            foreach (int seq in resendScratch)
            {
                var packet = pending[seq];
                packet.LastSent = now;
                send(packet.Text);
            }
        }

        /// <summary>
        /// 받은 패킷 한 개를 처리한다. 처리할 본문이 생기면 true (ACK 응답·중복 제거는 여기서 끝낸다).
        /// </summary>
        public bool Receive(string text, float now, out string body)
        {
            body = "";
            if (!UdpProtocol.TryParse(text, out var kind, out int seq, out string parsed)) return false;
            LastHeard = now;

            switch (kind)
            {
                case PacketKind.Ack:
                    pending.Remove(seq);
                    return false;

                case PacketKind.Reliable:
                    // 중복이어도 ACK 는 다시 보낸다 (이전 ACK 가 유실됐을 수 있으니까)
                    send(UdpProtocol.Ack(seq));
                    if (!handled.Add(seq)) return false;
                    body = parsed;
                    return true;

                case PacketKind.Unreliable:
                    body = parsed;
                    return true;

                default:
                    return false;
            }
        }
    }
}
