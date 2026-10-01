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
@"MINI PARTY BATTLE — 친구와 1:1 대결하는 법

1. 이 폴더 전체를 압축해서 친구에게 보내고, 둘 다 MiniParty.exe 를 실행하세요.
2. 로비 아래쪽 [온라인 1:1] 버튼 → 캐릭터 고르기.
3. 연결 방법 (둘 중 하나)

  ■ 초대코드 (인터넷 어디서나)
     - 한 명: [방 만들기 (초대코드)] → 화면에 나온 코드를 [복사]해서 친구에게 전송
     - 친구:  [코드로 참가] → 코드 입력(Ctrl+V) → [참가하기]

  ■ IP 직접 연결 (같은 와이파이, 또는 라디민 VPN·하마치 같은 가상 랜)
     - 한 명: [IP 직접 연결] → [내가 방 열기] → 화면에 나온 IP를 친구에게 알려주기
              (처음 실행 때 Windows 방화벽 창이 뜨면 '액세스 허용'을 눌러야 친구가 들어올 수 있어요)
     - 친구:  [IP 직접 연결] → [IP로 참가] → IP 입력 → [접속]

4. 연결되면 바로 시작! 방을 만든 사람이 P1, 들어온 사람이 P2 입니다.

조작은 두 사람 모두 똑같습니다: 방향키 또는 WASD, Space(또는 Enter), 마우스 왼쪽 클릭.
리듬 게임은 D F K L, 숫자 게임은 숫자키.
";

        [MenuItem("MiniParty/Build/Windows (친구에게 보낼 실행 파일)")]
        public static void BuildWindows()
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

            if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = Path.Combine(OutputDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                // not needed by players
                foreach (var dir in Directory.GetDirectories(OutputDir, "*DoNotShip*")) Directory.Delete(dir, true);
                File.WriteAllText(Path.Combine(OutputDir, "플레이 방법.txt"), Readme, System.Text.Encoding.UTF8);
                Debug.Log($"[Build] OK — {Path.GetFullPath(OutputDir)} ({report.summary.totalSize / (1024 * 1024)} MB)");
            }
            else
            {
                Debug.LogError($"[Build] FAILED: {report.summary.result} ({report.summary.totalErrors} errors)");
            }
        }
    }
}
