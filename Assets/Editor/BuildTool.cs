using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MiniParty.EditorTools
{
    /// <summary>
    /// MiniParty > Build > Windows: builds a standalone game into Builds/MiniParty (send the whole folder / zip).
    /// Every build also gets Server/UdpGameServer.exe (dotnet publish, self-contained: friends need no .NET),
    /// which the lobby's [내가 방 열기] starts in its own console window.
    /// </summary>
    public static class BuildTool
    {
        public const string OutputDir = "Builds/MiniParty";
        public const string ExeName = "MiniParty.exe";

        const string Readme =
@"MINI PARTY BATTLE — 같은 와이파이에서 친구와 1:1 대결하는 법

1. 이 폴더를 친구에게 주고, 두 사람 다 같은 와이파이(공유기)에 연결한 뒤 MiniParty.exe 를 실행하세요.
2. 로비 아래쪽 [온라인 1:1] → [IP / 서버 연결].
3. 로컬 플레이 (서버 없이 P2P): [로컬 플레이]
     방 여는 사람: [방 열기] → 화면에 나온 IP(예: 192.168.0.5)를 친구에게 알려주기
     친구: [IP로 참가] → 알려준 IP 입력 → [접속]
     * 처음 실행할 때 Windows 방화벽 창이 뜨면 '액세스 허용'을 꼭 눌러주세요. (안 누르면 친구가 못 들어와요)
4. UDP 게임 서버로 하기: 방장이 [내가 방 열기] → 이 PC의 IP:포트(예: 192.168.0.5:7780) 확인 → [서버 열기]
     서버 창(검은 콘솔)이 따로 뜨고 방장은 자동으로 입장합니다. 친구는 [UDP 서버 접속] → 같은 IP:포트 → [접속]
     * 서버(Server/UdpGameServer.exe)도 처음엔 방화벽 '액세스 허용'이 필요해요
     * 다른 네트워크(인터넷)의 친구와: IP 칸에 공인 IP를 입력하고, 공유기에서 UDP 포트(7780)를 이 PC로 포트포워딩
     * 서버가 방장이라 1조·3조 미니게임만 나와요 (게임 추첨·입력 검증·승패를 서버가 정함)
5. 연결되면 바로 시작!

안 들어가질 때
- 두 사람이 정말 같은 와이파이인지 확인 (한 명이 휴대폰 핫스팟/유선이면 다른 네트워크일 수 있어요)
- 방 연 사람 PC의 방화벽에서 MiniParty 허용 (제어판 > Windows Defender 방화벽 > 앱 허용)
- 학교·회사 와이파이는 기기끼리 연결을 막아둔 경우가 있어요

조작: 방향키 또는 WASD, Space(또는 Enter), 마우스 왼쪽 클릭. 리듬 게임은 D F K L, 숫자 게임은 숫자키.

[패치 노트]
- 새 미니게임: 야추 다이스 (주사위 5개, 3번 굴려 더 센 패가 승리)
- 디자인 개편: Jua 폰트, 새 배경·버튼·그림자, 효과음, 조작 키 아이콘
- 색깔 찾기 선택 테두리 제거, 100층 도전 키 반대로 되던 버그 수정
- 공 개수 세기 숫자 직접 입력, 타자 대결 한/영 자동 전환
- 테스트용: 지면 HP 1 감소, 미니게임은 한 바퀴 다 나오기 전 중복 없음
";

        [MenuItem("MiniParty/Build/Windows (친구에게 보낼 실행 파일)")]
        public static void BuildWindows() => Build("Assets/Scenes/SampleScene.unity", OutputDir, Readme);

        /// <summary>1·3조 test build: 1조 (반응·연타) + 3조 (보드·턴제) minigames, for the UDP game server (UdpGameServer/).</summary>
        [MenuItem("MiniParty/Build/Windows - 1·3조 통합 씬 (UDP 서버 테스트)")]
        public static void BuildGroup13() =>
            Build("Assets/Scenes/Groups/Group13_Combined.unity", "Builds/MiniParty_Group13", ServerReadme("1조 (반응·연타) + 3조 (보드·턴제)",
                "스페이스바 줄다리기, CPS 클릭 대결, F1 반응 속도, 에임 대결, 블럭 쌓기, 타이밍 서바이벌,\n사목, 오델로, 원을 그려봐요, 타자 대결, 야추 다이스"));

        /// <summary>3조 test build: only the Group3_Board minigames, for the UDP game server (UdpGameServer/).</summary>
        [MenuItem("MiniParty/Build/Windows - 3조 씬 (UDP 서버 테스트)")]
        public static void BuildGroup3() =>
            Build("Assets/Scenes/Groups/Group3_Board.unity", "Builds/MiniParty_Group3", ServerReadme("3조 (보드·턴제)",
                "사목, 오델로, 원을 그려봐요, 타자 대결, 야추 다이스"));

        static string ServerReadme(string groups, string games) =>
$@"MINI PARTY BATTLE — {groups} UDP 서버 테스트 빌드

이 빌드는 {groups} 미니게임만 나옵니다:
{games}

[방장] 서버 열기
1. MiniParty.exe 실행 → [온라인 1:1] → [IP / 서버 연결] → [내가 방 열기]
2. 이 PC의 IP:포트 확인 (예: 192.168.0.5:7780, 화면에 이 PC 주소가 나와요) → [서버 열기]
     * 서버 창(검은 콘솔)이 따로 뜨고 방장은 자동으로 입장합니다. 경기가 끝나면 창이 닫혀요.
     * 처음 실행 때 Windows 방화벽 창이 뜨면 '액세스 허용'
     * 다른 네트워크(인터넷)의 친구와 하려면 이 PC IP 대신 공인 IP를 입력하세요 (예: 203.0.113.10:7780).
       서버는 이 PC의 모든 주소로 열리고, 공유기 설정에서 UDP 포트(7780)를 이 PC 내부 IP로 포트포워딩해야 친구가 들어와요.
3. 화면에 나온 주소를 [복사]해서 친구에게 보내기

[친구] 서버 접속
1. MiniParty.exe 실행 → [온라인 1:1] → [IP / 서버 연결] → [UDP 서버 접속]
2. 방장이 보낸 IP:포트 입력 → [접속]. 먼저 들어온 두 명이 자동으로 짝지어집니다.
     * 여러 팀이 한 서버를 쓸 땐 주소#방이름 (예: 192.168.0.5:7780#TEAM3) 으로 같은 방에 모이기

서버가 방장입니다: 미니게임 추첨, 입력 검증(차례·둘 수 있는 자리·연타 속도·점수), 승패·데미지를 서버가 정해요.
서버 창에서 경기 진행과 거절된 입력을 실시간으로 볼 수 있어요.
Server/UdpGameServer.exe 를 직접 실행해 계속 켜 둘 수도 있어요 (모든 IP, 포트 7780).
";

        static void Build(string scene, string outputDir, string readme)
        {
            PlayerSettings.productName = "MiniParty Battle";
            PlayerSettings.companyName = "MiniParty";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.forceSingleInstance = false;

            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(outputDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                // not needed by players
                foreach (var dir in Directory.GetDirectories(outputDir, "*DoNotShip*")) Directory.Delete(dir, true);
                File.WriteAllText(Path.Combine(outputDir, "플레이 방법.txt"), readme, System.Text.Encoding.UTF8);
                bool server = PublishServer(Path.Combine(outputDir, "Server"));
                Debug.Log($"[Build] OK — {Path.GetFullPath(outputDir)} ({report.summary.totalSize / (1024 * 1024)} MB)" +
                          (server ? " + Server/UdpGameServer.exe" : " (서버 없음: [내가 방 열기]가 동작하지 않아요)"));
            }
            else
            {
                Debug.LogError($"[Build] FAILED: {report.summary.result} ({report.summary.totalErrors} errors)");
            }
        }

        /// <summary>
        /// dotnet publish UdpGameServer → one self-contained UdpGameServer.exe (no .NET install needed on friends' PCs).
        /// Needs the .NET SDK on the PC that makes the build.
        /// </summary>
        static bool PublishServer(string dir)
        {
            string project = Path.GetFullPath("UdpGameServer/UdpGameServer.csproj");
            string args = $"publish \"{project}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true " +
                          $"-p:EnableCompressionInSingleFile=true -p:DebugType=none -nologo -o \"{Path.GetFullPath(dir)}\"";
            try
            {
                EditorUtility.DisplayProgressBar("MiniParty Build", "UDP 게임 서버 빌드 중 (dotnet publish)...", 0.9f);
                var info = new System.Diagnostics.ProcessStartInfo("dotnet", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = System.Diagnostics.Process.Start(info))
                {
                    var stdout = p.StandardOutput.ReadToEndAsync();
                    var stderr = p.StandardError.ReadToEndAsync();
                    p.WaitForExit();
                    bool ok = p.ExitCode == 0 && File.Exists(Path.Combine(dir, "UdpGameServer.exe"));
                    if (!ok) Debug.LogError($"[Build] 서버 빌드 실패 (dotnet {args})\n{stdout.Result}{stderr.Result}");
                    return ok;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Build] 서버 빌드 실패: .NET SDK(dotnet)가 필요해요 — {e.Message}");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
