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
2. 로비 아래쪽 [온라인 1:1] → [IP 직접 연결].
3. 방 여는 사람: [내가 방 열기] → 화면에 나온 IP(예: 192.168.0.5)를 친구에게 알려주기
     * 처음 실행할 때 Windows 방화벽 창이 뜨면 '액세스 허용'을 꼭 눌러주세요. (안 누르면 친구가 못 들어와요)
4. 친구: [IP로 참가] → 알려준 IP 입력 → [접속]
5. 연결되면 바로 시작! 방을 연 사람이 P1, 들어온 사람이 P2 입니다.

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

        /// <summary>3조 test build: only the Group3_Board minigames, for the UDP game server (UdpGameServer/).</summary>
        [MenuItem("MiniParty/Build/Windows - 3조 씬 (UDP 서버 테스트)")]
        public static void BuildGroup3() => Build("Assets/Scenes/Groups/Group3_Board.unity", "Builds/MiniParty_Group3", Group3Readme);

        const string Group3Readme =
@"MINI PARTY BATTLE — 3조 (보드·턴제) UDP 서버 테스트 빌드

이 빌드는 3조 미니게임(사목, 오델로, 원을 그려봐요, 타자 대결, 야추 다이스)만 나옵니다.

1. 서버를 켤 PC에서 Server 폴더의 UdpGameServer.exe 실행
     * 처음 실행 때 Windows 방화벽 창이 뜨면 '액세스 허용'
     * 서버 창에 나온 주소(예: 192.168.0.5)를 두 사람에게 알려주기
2. 두 사람 다 MiniParty.exe 실행 → [온라인 1:1] → [IP / 서버 연결] → [UDP 서버 접속]
3. 서버 주소 입력 → [접속]. 먼저 들어온 두 명이 자동으로 짝지어집니다.
     * 여러 팀이 한 서버를 쓸 땐 주소#방이름 (예: 192.168.0.5#TEAM3) 으로 같은 방에 모이기
4. 서버가 방장입니다: 미니게임 추첨, 수 검증(차례·둘 수 있는 자리), 야추 주사위 굴리기, 승패·데미지를 서버가 정해요.
   서버 창에서 경기 진행과 거절된 입력을 실시간으로 볼 수 있어요.
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
                Debug.Log($"[Build] OK — {Path.GetFullPath(outputDir)} ({report.summary.totalSize / (1024 * 1024)} MB)");
            }
            else
            {
                Debug.LogError($"[Build] FAILED: {report.summary.result} ({report.summary.totalErrors} errors)");
            }
        }
    }
}
