using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using MiniParty.Group1Net;

namespace Group1Server.Net;

/// <summary>
/// System.Net.Sockets.Socket 을 직접 감싼 UDP 소켓.
///  - 받기: 별도 스레드가 ReceiveFrom 으로 블로킹 수신 → 큐에 넣는다.
///  - 처리: 게임 로직은 메인 루프 한 스레드에서 Drain() 으로 꺼내 처리한다 (잠금 없이 단순하게).
/// 서버와 테스트 봇이 같이 쓴다.
/// </summary>
public sealed class UdpSocket : IDisposable
{
    // Windows 전용: 상대가 꺼진 뒤 보낸 패킷의 ICMP "port unreachable" 때문에 ReceiveFrom 이 10054 로 깨지는 것을 막는다.
    const int SIO_UDP_CONNRESET = -1744830452;

    readonly Socket socket;
    readonly Thread receiveThread;
    readonly ConcurrentQueue<(IPEndPoint From, byte[] Data)> inbox = new();
    volatile bool running = true;

    public IPEndPoint LocalEndPoint => (IPEndPoint)socket.LocalEndPoint;
    public long BytesSent, BytesReceived, PacketsSent, PacketsReceived;

    /// <summary>port 0 = OS 가 빈 포트를 고른다 (봇 클라이언트용).</summary>
    public UdpSocket(int port)
    {
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows())
            socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
        socket.Bind(new IPEndPoint(IPAddress.Any, port)); // 0.0.0.0 = 모든 네트워크 카드
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "UDP Receive" };
        receiveThread.Start();
    }

    void ReceiveLoop()
    {
        var buffer = new byte[G1Protocol.MaxPacketSize * 2];
        while (running)
        {
            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int length = socket.ReceiveFrom(buffer, ref remote);
                var data = new byte[length];
                Buffer.BlockCopy(buffer, 0, data, 0, length);
                var from = (IPEndPoint)remote;
                inbox.Enqueue((new IPEndPoint(from.Address, from.Port), data));
                Interlocked.Add(ref BytesReceived, length);
                Interlocked.Increment(ref PacketsReceived);
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                // 상대가 꺼짐 / 너무 큰 패킷: 그 패킷만 버리고 계속
            }
            catch (SocketException)
            {
                if (!running) break;
                Thread.Sleep(5);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    public bool TryReceive(out IPEndPoint from, out byte[] data)
    {
        if (inbox.TryDequeue(out var item))
        {
            from = item.From;
            data = item.Data;
            return true;
        }
        from = null;
        data = null;
        return false;
    }

    public void Send(IPEndPoint to, byte[] data)
    {
        try
        {
            socket.SendTo(data, to);
            Interlocked.Add(ref BytesSent, data.Length);
            Interlocked.Increment(ref PacketsSent);
        }
        catch (SocketException e)
        {
            Log.Warn($"송신 실패 {to}: {e.SocketErrorCode}");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        running = false;
        socket.Close(); // 블로킹 중인 ReceiveFrom 이 예외로 빠져나온다
        receiveThread.Join(500);
    }
}
