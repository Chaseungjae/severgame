using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MiniParty.Udp
{
    /// <summary>
    /// [내가 방 열기]: runs the UDP game server (UdpGameServer) on this PC in its own console window, bound to the
    /// IP:port the player typed. The player then joins it like everyone else (Mode.UdpServer).
    ///   Build  : &lt;game folder&gt;/Server/UdpGameServer.exe (self-contained, BuildTool copies it there; no .NET needed)
    ///   Editor : builds UdpGameServer/ with the .NET SDK (dotnet build -c Release) and runs bin/Release/net9.0/UdpGameServer.exe
    /// The window closes when the match ends, the player cancels, or the game quits.
    /// </summary>
    public sealed class LocalUdpServer
    {
        static readonly List<LocalUdpServer> running = new List<LocalUdpServer>();
        static bool hookedQuit;

        public readonly IPAddress Address;
        public readonly int Port;
        /// <summary>Progress text while starting ("서버 빌드 중..."), or why it failed.</summary>
        public string Status { get; private set; } = "서버 프로그램 시작 중...";
        public bool Failed { get; private set; }

        Process process;
        bool stopped;

        /// <summary>The address friends type in [UDP 서버 접속].</summary>
        public string ShareAddress => $"{Address}:{Port}";

        LocalUdpServer(IPAddress address, int port)
        {
            Address = address;
            Port = port;
        }

        // ---------- input ----------

        /// <summary>"192.168.0.5:7780" or "192.168.0.5" (port 7780). Only this PC's IPv4 addresses are allowed.</summary>
        public static bool TryParse(string text, out IPAddress address, out int port, out string error)
        {
            address = null;
            port = UdpWire.DefaultPort;
            error = null;
            string host = (text ?? "").Trim();
            int colon = host.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!int.TryParse(host.Substring(colon + 1), out port) || port < 1024 || port > 65535)
                {
                    error = "포트 번호는 1024 ~ 65535 사이로 입력해 주세요";
                    return false;
                }
                host = host.Substring(0, colon);
            }
            if (!IPAddress.TryParse(host, out address) || address.AddressFamily != AddressFamily.InterNetwork)
            {
                error = "IP 주소 형식이 아니에요 (예: 192.168.0.5:7780)";
                return false;
            }
            if (!address.Equals(IPAddress.Loopback) && !NetSession.LocalAddresses().Contains(address.ToString()))
            {
                error = $"{address} 는 이 PC의 IP가 아니에요\n이 PC: {string.Join(" / ", NetSession.LocalAddresses())}";
                return false;
            }
            if (!CanBind(address, port))
            {
                error = $"포트 {port} 를 이미 다른 프로그램이 쓰고 있어요 (다른 포트로 해 보세요)";
                return false;
            }
            return true;
        }

        /// <summary>True if nothing on this PC holds IP:port yet (the server is not up / the port is free).</summary>
        static bool CanBind(IPAddress address, int port)
        {
            try
            {
                using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    probe.Bind(new IPEndPoint(address, port));
                }
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        // ---------- lifetime ----------

        public static LocalUdpServer Start(IPAddress address, int port)
        {
            var server = new LocalUdpServer(address, port);
            running.Add(server);
            if (!hookedQuit)
            {
                hookedQuit = true;
                Application.quitting += StopAll; // also fires when the editor leaves play mode
            }
            _ = server.Launch();
            return server;
        }

        /// <summary>
        /// The server process is running. (It opens its socket within a moment; the client's HELLO is resent for
        /// several seconds, so joining right away is fine. Probing the port here could steal it from the server.)
        /// </summary>
        public bool Ready => process != null && !Failed;

        public bool HasExited
        {
            get
            {
                try { return process != null && process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }

        async Task Launch()
        {
            try
            {
                string exe = await FindServerExe();
                if (stopped) return;
                if (exe == null) return; // Fail() already set the status

                var info = new ProcessStartInfo(exe, $"{Port} --bind {Address} --from-game")
                {
                    UseShellExecute = true,     // its own console window
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                };
                process = Process.Start(info);
                if (process == null)
                {
                    Fail("서버 프로그램을 실행하지 못했어요");
                    return;
                }
                Status = "서버 여는 중...";
                Debug.Log($"[UDP 서버] 실행: {exe} {info.Arguments} (PID {process.Id})");
            }
            catch (Exception e)
            {
                Fail("서버 프로그램을 실행하지 못했어요: " + e.Message);
            }
        }

        async Task<string> FindServerExe()
        {
#if UNITY_EDITOR
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UdpGameServer"));
            string built = Path.Combine(project, "bin", "Release", "net9.0", "UdpGameServer.exe");
            Status = "서버 빌드 중... (dotnet build)";
            var (ok, log) = await Task.Run(() => RunHidden("dotnet", $"build \"{Path.Combine(project, "UdpGameServer.csproj")}\" -c Release -nologo -v q"));
            if (!ok)
            {
                if (File.Exists(built))
                {
                    Debug.LogWarning("[UDP 서버] dotnet build 실패 — 이전에 빌드된 서버로 실행합니다\n" + log);
                    return built;
                }
                Fail("서버를 빌드하지 못했어요 (.NET SDK 설치 확인, 콘솔 참고)");
                Debug.LogError("[UDP 서버] dotnet build 실패\n" + log);
                return null;
            }
            return built;
#else
            await Task.Yield();
            string exe = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Server", "UdpGameServer.exe"));
            if (File.Exists(exe)) return exe;
            Fail("서버 프로그램이 없어요 (게임 폴더의 Server/UdpGameServer.exe)");
            return null;
#endif
        }

#if UNITY_EDITOR
        static (bool, string) RunHidden(string file, string args)
        {
            try
            {
                var info = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(info))
                {
                    var stdout = p.StandardOutput.ReadToEndAsync();
                    var stderr = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(120000))
                    {
                        p.Kill();
                        return (false, "시간 초과");
                    }
                    return (p.ExitCode == 0, stdout.Result + stderr.Result);
                }
            }
            catch (Exception e)
            {
                return (false, e.Message);
            }
        }
#endif

        void Fail(string status)
        {
            Failed = true;
            Status = status;
            Debug.LogWarning("[UDP 서버] " + status);
        }

        /// <summary>Closes the server window (everyone in its rooms is disconnected).</summary>
        public void Stop()
        {
            if (stopped) return;
            stopped = true;
            running.Remove(this);
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                    Debug.Log($"[UDP 서버] 종료 (PID {process.Id})");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UDP 서버] 종료 실패: " + e.Message);
            }
            process?.Dispose();
            process = null;
        }

        static void StopAll()
        {
            foreach (var s in running.ToArray()) s.Stop();
        }
    }
}
