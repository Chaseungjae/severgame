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
    ///   - this PC's IP (LAN 192.168.x.x / VPN): the server opens on that address only.
    ///   - a public IP: it belongs to the router, not this PC, so the server opens on every address of this PC
    ///     (0.0.0.0) and the public IP is only the address shared with friends. The router must forward that UDP port
    ///     to this PC (port forwarding); the host itself joins through 127.0.0.1.
    ///   Build  : &lt;game folder&gt;/Server/UdpGameServer.exe (self-contained, BuildTool copies it there; no .NET needed)
    ///   Editor : builds UdpGameServer/ with the .NET SDK (dotnet build -c Release) and runs bin/Release/net9.0/UdpGameServer.exe
    /// The window closes when the match ends, the player cancels, or the game quits.
    /// </summary>
    public sealed class LocalUdpServer
    {
        static readonly List<LocalUdpServer> running = new List<LocalUdpServer>();
        static bool hookedQuit;

        /// <summary>The address the player typed: this PC's IP, or the public IP when <see cref="IsPublic"/>.</summary>
        public readonly IPAddress Address;
        public readonly int Port;
        /// <summary>A public IP was typed: the server listens on every address and needs router port forwarding.</summary>
        public readonly bool IsPublic;
        /// <summary>Progress text while starting ("서버 빌드 중..."), or why it failed.</summary>
        public string Status { get; private set; } = "서버 프로그램 시작 중...";
        public bool Failed { get; private set; }

        Process process;
        bool stopped;

        /// <summary>The address friends type in [UDP 서버 접속].</summary>
        public string ShareAddress => $"{Address}:{Port}";

        /// <summary>Where the server opens its socket.</summary>
        IPAddress BindAddress => IsPublic ? IPAddress.Any : Address;

        /// <summary>
        /// The address the host's own game joins. With a public IP that is 127.0.0.1: many routers don't let a PC
        /// reach its own public IP from inside (no NAT loopback).
        /// </summary>
        public string ConnectAddress => IsPublic ? $"127.0.0.1:{Port}" : ShareAddress;

        LocalUdpServer(IPAddress address, int port, bool isPublic)
        {
            Address = address;
            Port = port;
            IsPublic = isPublic;
        }

        // ---------- input ----------

        /// <summary>
        /// "192.168.0.5:7780" or "192.168.0.5" (port 7780): this PC's IPv4 address, or a public IP (isPublic = true).
        /// A private address that isn't this PC's (a typo, another PC on the LAN) is refused.
        /// </summary>
        public static bool TryParse(string text, out IPAddress address, out int port, out bool isPublic, out string error)
        {
            address = null;
            port = UdpWire.DefaultPort;
            isPublic = false;
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
            var mine = NetSession.LocalAddresses();
            if (!address.Equals(IPAddress.Loopback) && !mine.Contains(address.ToString()))
            {
                if (IsPrivate(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
                {
                    error = $"{address} 는 이 PC의 IP가 아니에요\n이 PC: {string.Join(" / ", mine)} · 또는 공인 IP";
                    return false;
                }
                isPublic = true; // the router's address: open on every address of this PC, friends use the public IP
            }
            if (!CanBind(isPublic ? IPAddress.Any : address, port))
            {
                error = $"포트 {port} 를 이미 다른 프로그램이 쓰고 있어요 (다른 포트로 해 보세요)";
                return false;
            }
            return true;
        }

        /// <summary>Addresses that never reach a PC from the internet (LAN, VPN / CGNAT, link-local, loopback).</summary>
        static bool IsPrivate(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || b[0] == 127 || b[0] == 0
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // CGNAT / Tailscale
                || b[0] >= 224;                                 // multicast / reserved
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

        public static LocalUdpServer Start(IPAddress address, int port, bool isPublic)
        {
            var server = new LocalUdpServer(address, port, isPublic);
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

                string args = $"{Port} --bind {BindAddress} --from-game" + (IsPublic ? $" --public {Address}" : "");
                var info = new ProcessStartInfo(exe, args)
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
