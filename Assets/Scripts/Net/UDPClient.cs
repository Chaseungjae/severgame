using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class UDPClient : MonoBehaviour
{
    [SerializeField] string serverAddress = "127.0.0.1";
    [SerializeField] int serverPort = 7777;

    readonly ConcurrentQueue<string> receivedMessages = new ConcurrentQueue<string>();
    UdpClient socket;
    IPEndPoint serverEndpoint;
    CancellationTokenSource receiveCancellation;
    string status = "서버에 접속 중...";
    string playerId = "-";
    string messageInput = "안녕하세요!";

    void Start()
    {
        if (!IPAddress.TryParse(serverAddress, out var address))
        {
            status = "잘못된 서버 IP 주소입니다.";
            Debug.LogError($"[UDPClient] {status} ({serverAddress})");
            return;
        }

        serverEndpoint = new IPEndPoint(address, serverPort);
        socket = new UdpClient(0);
        receiveCancellation = new CancellationTokenSource();
        _ = ReceiveLoopAsync(socket, receiveCancellation.Token);

        Send("JOIN");
        status = $"JOIN 전송: {serverEndpoint}";
        Debug.Log($"[UDPClient] 서버 접속 요청: {serverEndpoint}");
    }

    void Update()
    {
        while (receivedMessages.TryDequeue(out var message))
            HandleMessage(message);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 380, 220), GUI.skin.window);
        GUILayout.Label("UDP 테스트 클라이언트");
        GUILayout.Label($"서버: {serverEndpoint?.ToString() ?? serverAddress + ":" + serverPort}");
        GUILayout.Label($"플레이어: {playerId} | 상태: {status}");

        if (GUILayout.Button("PING 보내기"))
            Send("PING");

        messageInput = GUILayout.TextField(messageInput);
        if (GUILayout.Button("서버를 통해 메시지 보내기"))
            Send("MSG|" + messageInput);

        GUILayout.EndArea();
    }

    async Task ReceiveLoopAsync(UdpClient receiver, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await receiver.ReceiveAsync().ConfigureAwait(false);
                receivedMessages.Enqueue(Encoding.UTF8.GetString(result.Buffer));
            }
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            receivedMessages.Enqueue("CLIENT_ERROR|" + exception.Message);
        }
    }

    void HandleMessage(string message)
    {
        Debug.Log($"[UDPClient {playerId}] 수신: {message}");

        if (message.StartsWith("WELCOME|", StringComparison.Ordinal))
        {
            playerId = message.Substring("WELCOME|".Length);
            status = "서버에 등록됨";
        }
        else if (message == "READY")
        {
            status = "두 클라이언트 접속 완료";
        }
        else if (message == "PONG")
        {
            status = "PING 응답 수신";
        }
        else if (message.StartsWith("MESSAGE|", StringComparison.Ordinal))
        {
            status = "채팅 메시지 수신";
        }
        else if (message == "FULL")
        {
            status = "서버 정원 초과";
        }
        else if (message.StartsWith("ERROR|", StringComparison.Ordinal) ||
                 message.StartsWith("CLIENT_ERROR|", StringComparison.Ordinal))
        {
            status = message;
            Debug.LogError($"[UDPClient] {message}");
        }
        else
        {
            status = message;
        }
    }

    void Send(string message)
    {
        if (socket == null || serverEndpoint == null)
        {
            Debug.LogWarning("[UDPClient] 서버 소켓이 준비되지 않았습니다.");
            return;
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            socket.Send(data, data.Length, serverEndpoint);
            Debug.Log($"[UDPClient {playerId}] 송신: {message}");
        }
        catch (SocketException exception)
        {
            status = "송신 오류: " + exception.Message;
            Debug.LogError($"[UDPClient] {status}");
        }
        catch (ObjectDisposedException)
        {
            status = "소켓이 종료되었습니다.";
            Debug.LogWarning($"[UDPClient] {status}");
        }
    }

    void OnDestroy()
    {
        receiveCancellation?.Cancel();
        socket?.Close();
        receiveCancellation?.Dispose();
    }
}
