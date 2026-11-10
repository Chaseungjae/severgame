# MINI PARTY BATTLE

두 명이 미니게임으로 1:1 대결하는 Unity 2D 파티 게임입니다.
미니게임에서 지면 결과 화면에서 벌칙을 받고 HP가 깎이며, 먼저 HP가 0이 되면 패배합니다.

- 엔진: **Unity 6.3 LTS (6000.3.23f1)**, URP 2D
- 패키지: Input System, Netcode for GameObjects, Multiplayer Services (Relay)
- 모든 UI·캐릭터·연출은 코드로 생성 (`Assets/Scripts`, 네임스페이스 `MiniParty`)
- 무료 에셋: Jua 폰트(OFL), Kenney UI Pack · UI Audio · Input Prompts (CC0) — `Assets/Resources/UiTheme.asset`에서 연결

## 시작하기

1. Unity Hub에서 **6000.3.23f1**로 이 폴더를 엽니다.
2. `Assets/Scenes/SampleScene.unity`를 열고 Play.
3. 로비에서 CPU 대전 또는 온라인 1:1을 고릅니다 (PC 한 대 = 플레이어 한 명). 내 쪽도 CPU로 켜면 관전/테스트 모드.

## 폴더 구조

| 경로 | 내용 |
|---|---|
| `Assets/Scripts/Core` | UI 생성 도구(`UIKit`), 트윈, 캐릭터, 입력(`Controls`), HUD·페이드·블러 |
| `Assets/Scripts/Flow` | 게임 흐름: 로비 → 미니게임 소개 → 플레이 → 결과·벌칙 → 생존 체크 (`GameManager`) |
| `Assets/Scripts/MiniGames/GroupN_*` | 조별 미니게임 (아래 "조별 작업") |
| `Assets/Scripts/MiniGames/Shared` | 여러 게임이 같이 쓰는 도우미 (`SurvivalJudge`) |
| `Assets/Scripts/Net` | 온라인 대전 (`NetSession`: 빠른 매칭 / 초대코드 / IP 직접 연결) |
| `Assets/Editor` | 테스트·빌드 메뉴 (`MiniParty > Test`, `MiniParty > Build`) |

## 조별 작업 (4조)

미니게임을 4개 조로 나눴습니다. **각 조는 자기 폴더와 자기 씬만 수정**하면 나중에 합칠 때 충돌이 나지 않습니다.

| 조 | 스크립트 폴더 | 테스트 씬 | 미니게임 |
|---|---|---|---|
| 1조 · 반응·연타 | `Scripts/MiniGames/Group1_Reaction` | `Scenes/Groups/Group1_Reaction.unity` | 스페이스바 줄다리기, CPS 클릭, F1 반응 속도, 에임 대결, 블럭 쌓기, 타이밍 서바이벌 |
| 2조 · 두뇌·관찰 | `Scripts/MiniGames/Group2_Brain` | `Scenes/Groups/Group2_Brain.unity` | 1 to 50, 공 개수 세기, 색깔 찾기, 순간 기억력, 10초 맞추기 |
| 3조 · 보드·턴제 | `Scripts/MiniGames/Group3_Board` | `Scenes/Groups/Group3_Board.unity` | 사목, 오델로, 원을 그려봐요, 타자 대결, 야추 다이스 |
| 4조 · 액션·이동 | `Scripts/MiniGames/Group4_Action` | `Scenes/Groups/Group4_Action.unity` | 100층 도전, 방향키 입력, 표창 막기, 죽림고수, 스네이크, 리듬 대결 |

- 조 씬에서 Play하면 **그 조의 미니게임만** 나옵니다 (씬 이름 `GroupN_...`으로 자동 판단). 로비 위에 조 이름이 표시돼요.
- 게임 목록은 각 조 폴더의 `GroupNGames.cs`에만 있습니다. 게임 추가·삭제는 자기 조 파일만 고치세요.
- `SampleScene`(메인)은 모든 조의 게임이 다 나오는 최종 씬입니다. 메인 씬·`Core`·`Flow`·`Net`·`MiniGameBase.cs`는 **공용 파일**이라 고치기 전에 팀에 먼저 말해주세요.
- 작업 시작 전 항상 Pull, 끝나면 Commit → Push. 브랜치를 조별로 (`group1` …) 따서 작업하고 PR로 합치면 더 안전합니다.
- 확인: `MiniParty > Test > 조별 미니게임 목록` (콘솔에 조별 게임 출력).

## 새 미니게임 추가하기

1. `MiniGameBase`를 상속한 클래스를 만듭니다 (`Title`, `Rules`, `Build()`, `OnAction()`, `Punish()`).
2. 플레이어 입력은 직접 결과를 바꾸지 말고 `Emit(kind, player, a, f)`로 액션을 보내고, `OnAction`에서 상태를 바꿉니다.
   그래야 한 PC 대전과 온라인 대전이 같은 코드로 동작합니다. 승패는 `DeclareWinner()` (방장/로컬만 결정).
3. 자기 조 폴더에 파일을 만들고, 그 조의 `GroupNGames.cs` 목록에 한 줄 추가.
4. `MiniParty > Test > CPU vs CPU` 메뉴로 확인합니다.

## 온라인

- **초대코드 / 빠른 매칭**: Unity Relay 사용. 프로젝트가 Unity Cloud에 연결되어 있어야 합니다 (`Edit > Project Settings > Services`).
- **IP 직접 연결**: 같은 네트워크나 라디민·하마치 같은 VPN에서 포트 7777로 연결.
- 방장이 P1이며 미니게임 추첨·승패·데미지를 결정합니다.
- 한 PC에서 두 개를 띄워 테스트: 빌드 후 `MiniParty.exe -autohost -autoplay` 와 `MiniParty.exe -autojoin 127.0.0.1 -autoplay`.

## UDP 게임 서버 (`UdpGameServer/`) — 3조 보드게임

직접 만든 C# UDP 서버가 **방장(판정자)** 입니다. 두 클라이언트는 둘 다 손님이고, 서버가 미니게임 추첨 · 수 검증 · 승패 · 데미지/HP를 정합니다.

| 게임 | 서버가 하는 일 (`UdpGameServer/Games/`) |
|---|---|
| 사목 | 차례·둘 수 있는 줄 검사, 4목 판정, 가득 차면 추첨, 응답 없는 플레이어는 서버가 대신 둠 |
| 오델로 | 뒤집기가 되는 자리만 허용, 패스·종료·돌 개수 판정 |
| 원을 그려봐요 | 시도 순서·점수 범위(0~100) 검사, 최고점 → 합계로 판정, 시간 초과 처리 |
| 타자 대결 | 단어 진행이 한 칸씩인지, 기록이 서버 시계·사람 속도로 가능한지 검사, 완주 판정 |
| 야추 다이스 | **주사위를 서버가 굴림** (클라이언트는 요청만), 확정 패가 서버 주사위와 같은지 검사, 족보 판정·동점 재경기 |

- **실행**: `cd UdpGameServer` → `dotnet run -c Release`
  - 옵션: `[포트] [--damage N] [--game N] [--loss N] [--quiet]` — 기본 포트 **7780**, `--game 11~15`는 한 게임만, `--loss N`은 받은 패킷 N%를 일부러 버리는 테스트
- **게임에서 접속**: `[온라인 1:1] → [IP / 서버 연결] → [UDP 서버 접속]` → 서버 PC의 IP. 먼저 온 두 명이 짝지어지고 `주소#방이름`으로 방을 지정할 수 있어요.
- **구조**
  - `Assets/Scripts/Net/UdpServer/UdpProtocol.cs` — 패킷 규격 + 신뢰성(순번·ACK·재전송·순서 보장)
  - `Assets/Scripts/Net/UdpServer/BoardProtocol.cs` — 3조 게임 번호·액션 번호·야추 족보 계산
  - 위 두 파일은 **서버와 Unity가 같은 파일을 컴파일**합니다. 게임 스크립트의 액션 번호를 바꾸면 여기도 같이 바꾸세요 (어긋나면 접속 시 Unity가 거절).
  - `UdpServerLink.cs` — Unity 클라이언트, `NetSession`의 `Mode.UdpServer` — 기존 온라인 흐름(로비 → 라운드 → 결과)을 그대로 사용
  - `UdpGameServer/Room.cs` — 경기 흐름(캐릭터 교환 → StartRound → Barrier/Go → 판정 → RoundResult), `Games/*Rules.cs` — 게임별 규칙
- **조 씬 빌드**: `MiniParty > Build > Windows - 3조 씬 (UDP 서버 테스트)` → `Builds/MiniParty_Group3/`
- **자동 테스트**: 서버 실행 후 `MiniParty.exe -udpjoin 127.0.0.1 -autoplay` 를 두 번 실행하면 CPU끼리 서버를 거쳐 대전합니다.

## 빌드

`MiniParty > Build > Windows` → `Builds/MiniParty/` (zip으로 묶어 배포). `Builds/`는 git에 올리지 않습니다.

## 주의

- Play 중에 스크립트를 수정하면 도메인 리로드로 진행 중인 연출이 멈춥니다. 수정은 Play를 멈춘 뒤에.
