using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MiniParty.EditorTools
{
    /// <summary>MiniParty > Build > Windows: builds a standalone game into Builds/MiniParty (send the whole folder / zip).</summary>
    public static class BuildTool
    {
        public const string OutputDir = "Builds/MiniParty";
        public const string ExeName = "MiniParty.exe";

        const string Readme =
@"MINI PARTY BATTLE — 같은 와이파이에서 친구와 1:1 대결하는 법

1. 이 폴더를 친구에게 주고, 두 사람 다 같은 와이파이(공유기)에 연결한 뒤 MiniParty.exe 를 실행하세요.
2. 한 사람이 게임 서버(UDPServer.exe)를 먼저 켭니다. 검은 창에 '접속할 주소'가 표시됩니다.
     * 처음 실행할 때 Windows 방화벽 창이 뜨면 '액세스 허용'을 꼭 눌러주세요. (안 누르면 친구가 못 들어와요)
3. 두 사람 모두 로비 아래쪽 [온라인 1:1] → 서버 주소 입력(예: 192.168.0.5) → [서버에 접속].
4. 두 명이 다 들어오면 바로 시작! 먼저 접속한 사람이 P1, 다음이 P2 입니다.
     * 승패 · 데미지 · 미니게임 순서는 모두 서버가 정합니다.

안 들어가질 때
- 두 사람이 정말 같은 와이파이인지 확인 (한 명이 휴대폰 핫스팟/유선이면 다른 네트워크일 수 있어요)
- 서버를 켠 PC의 방화벽에서 UDP 7777 허용 (제어판 > Windows Defender 방화벽 > 앱 허용)
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
        public static void BuildWindows()
        {
            var report = Build("Assets/Scenes/SampleScene.unity", OutputDir, "MiniParty Battle");
            if (report.summary.result != BuildResult.Succeeded) return;
            File.WriteAllText(Path.Combine(OutputDir, "플레이 방법.txt"), Readme, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// 2조 씬만 들어간 테스트 빌드. UDP 게임 서버 하나에 이 실행 파일 2개를 붙여서 확인한다
        /// (MiniParty.exe -server 127.0.0.1 / 두 번째 창은 -server 127.0.0.1 -p2).
        /// </summary>
        [MenuItem("MiniParty/Build/Group2_Brain (UDP 서버 테스트용)")]
        public static void BuildBrainTest() =>
            Build("Assets/Scenes/Groups/Group2_Brain.unity", "Builds/Group2_Brain", "MiniParty Brain Test");

        static BuildReport Build(string scene, string outputDir, string productName)
        {
            PlayerSettings.productName = productName;
            PlayerSettings.companyName = "MiniParty";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.forceSingleInstance = false; // 한 PC 에서 2개 띄워 테스트

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
                Debug.Log($"[Build] OK — {Path.GetFullPath(outputDir)} ({report.summary.totalSize / (1024 * 1024)} MB)");
            }
            else Debug.LogError($"[Build] FAILED: {report.summary.result} ({report.summary.totalErrors} errors)");
            return report;
        }
    }
}
