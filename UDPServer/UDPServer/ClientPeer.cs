using System.Net;

namespace MiniParty.Server;

/// <summary>서버가 보는 클라이언트 한 명.</summary>
internal sealed class ClientPeer
{
    public ClientPeer(IPEndPoint endPoint, ReliableChannel channel)
    {
        EndPoint = endPoint;
        Channel = channel;
    }

    public IPEndPoint EndPoint { get; }
    public ReliableChannel Channel { get; }

    /// <summary>0 = P1, 1 = P2. 방에 들어가기 전에는 -1.</summary>
    public int Slot { get; set; } = -1;

    public int Character { get; set; }

    /// <summary>테스트 중인 조(씬). 0 = 전체 게임.</summary>
    public int Group { get; set; }

    public bool Greeted { get; set; }

    public string Name => Slot < 0 ? EndPoint.ToString() : $"P{Slot + 1}({EndPoint})";

    public void Send(NetMsg msg, float now) => Channel.SendReliable(GamePacket.Encode(msg), now);

    public void Send(MsgType type, float now, int a = 0, int b = 0, int c = 0, int d = 0, float f = 0f) =>
        Channel.SendReliable(GamePacket.Encode(type, a, b, c, d, f), now);

    public void SendUnreliable(MsgType type, int a = 0) =>
        Channel.SendUnreliable(GamePacket.Encode(type, a));
}
