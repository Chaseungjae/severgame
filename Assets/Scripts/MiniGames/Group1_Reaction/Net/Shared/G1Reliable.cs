using System;
using System.Collections.Generic;

namespace MiniParty.Group1Net
{
    /// <summary>
    /// UDP 위에 직접 만든 신뢰성 계층 (상대 한 명당 하나).
    ///
    ///  보내기: Reliable 패킷마다 번호(Seq)를 붙여 보관해 두고, 상대의 Ack 가 올 때까지 ResendInterval 마다 재전송.
    ///  받기  : 번호마다 Ack 를 돌려주고(중복이어도 다시 - 앞의 Ack 가 유실됐을 수 있으므로),
    ///          NextExpected 번호부터 "순서대로 한 번씩만" 처리한다. 먼저 도착한 뒤 번호는 잠시 보관.
    ///
    /// 소켓은 모른다: 실제 전송은 생성자로 받은 send 함수가 한다 (서버 / 유니티 클라이언트 공용).
    /// 스레드 안전하지 않음 - 한 스레드(서버 루프 / 유니티 메인 스레드)에서만 호출할 것.
    /// </summary>
    public sealed class ReliableChannel
    {
        const int MaxEarly = 512; // 순서가 어긋나 미리 받아 둘 수 있는 최대 개수

        sealed class Pending
        {
            public byte[] Data;
            public double LastSent;
            public int Tries;
        }

        readonly Action<byte[]> send;
        readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        readonly Dictionary<int, byte[]> early = new Dictionary<int, byte[]>();
        readonly List<int> resendOrder = new List<int>();
        int nextSendSeq = 1;
        int nextExpected = 1;

        /// <summary>마지막으로 상대에게서 (어떤 것이든) 패킷을 받은 시각.</summary>
        public double LastHeard;

        public int PendingCount => pending.Count;
        public int Resends { get; private set; }

        public ReliableChannel(Action<byte[]> send, double now)
        {
            this.send = send;
            LastHeard = now;
        }

        public void SendReliable(byte[] payload, double now)
        {
            int seq = nextSendSeq++;
            var data = new PacketWriter().Header(Channel.Reliable, seq).Bytes(payload).ToArray();
            pending[seq] = new Pending { Data = data, LastSent = now };
            send(data);
        }

        public void SendUnreliable(byte[] payload)
        {
            send(new PacketWriter().Header(Channel.Unreliable, 0).Bytes(payload).ToArray());
        }

        /// <summary>
        /// 받은 패킷 하나를 처리한다. 처리할 차례가 된 Payload 들을 순서대로 deliver 에 넘긴다
        /// (Reliable 하나가 도착하면서 보관해 둔 뒤 번호들까지 한꺼번에 풀릴 수 있다).
        /// </summary>
        public void Receive(Packet packet, double now, Action<byte[]> deliver)
        {
            LastHeard = now;
            switch (packet.Channel)
            {
                case Channel.Ack:
                    pending.Remove(packet.Seq);
                    break;

                case Channel.Unreliable:
                    deliver(packet.Payload);
                    break;

                case Channel.Reliable:
                {
                    int seq = packet.Seq;
                    if (seq <= 0 || seq >= nextExpected + MaxEarly) return; // 말이 안 되는 번호: Ack 도 하지 않음
                    send(new PacketWriter().Header(Channel.Ack, seq).ToArray());
                    if (seq < nextExpected) return;                          // 이미 처리한 중복
                    if (seq > nextExpected)
                    {
                        early[seq] = packet.Payload;                         // 앞 번호가 아직 안 옴 → 보관
                        return;
                    }

                    deliver(packet.Payload);
                    nextExpected++;
                    while (early.TryGetValue(nextExpected, out var next))
                    {
                        early.Remove(nextExpected);
                        deliver(next);
                        nextExpected++;
                    }
                    break;
                }
            }
        }

        /// <summary>Ack 를 못 받은 패킷을 재전송. 매 틱 호출.</summary>
        public void Update(double now)
        {
            if (pending.Count == 0) return;
            resendOrder.Clear();
            foreach (var pair in pending)
                if (now - pair.Value.LastSent >= G1Protocol.ResendInterval) resendOrder.Add(pair.Key);
            resendOrder.Sort(); // 낮은 번호부터 (받는 쪽이 순서대로 처리하므로)
            foreach (int seq in resendOrder)
            {
                var p = pending[seq];
                p.LastSent = now;
                p.Tries++;
                Resends++;
                send(p.Data);
            }
        }
    }
}
