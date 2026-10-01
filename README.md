# MINI PARTY BATTLE

두 명이 미니게임으로 1:1 대결하는 Unity 2D 파티 게임입니다.
미니게임에서 지면 결과 화면에서 벌칙을 받고 HP가 깎이며, 먼저 HP가 0이 되면 패배합니다.

- 엔진: **Unity 6.3 LTS (6000.3.23f1)**, URP 2D
- 패키지: Input System, Netcode for GameObjects, Multiplayer Services (Relay)
- 모든 UI·캐릭터·연출은 코드로 생성 (`Assets/Scripts`, 네임스페이스 `MiniParty`) — 아트 에셋 없음

## 시작하기

1. Unity Hub에서 **6000.3.23f1**로 이 폴더를 엽니다.
2. `Assets/Scenes/SampleScene.unity`를 열고 Play.
3. 로비에서 로컬 2인(한 PC) 또는 온라인 1:1을 고릅니다. 양쪽을 CPU로 두면 관전/테스트 모드(게임 중복 없이 순서대로, 패배 시 HP -1).

## 폴더 구조

| 경로 | 내용 |
|---|---|
| `Assets/Scripts/Core` | UI 생성 도구(`UIKit`), 트윈, 캐릭터, 입력(`Controls`), HUD·페이드·블러 |
| `Assets/Scripts/Flow` | 게임 흐름: 로비 → 미니게임 소개 → 플레이 → 결과·벌칙 → 생존 체크 (`GameManager`) |
| `Assets/Scripts/MiniGames` | 미니게임들. `MiniGameBase.cs`의 `MiniGameRegistry`에 등록 |
| `Assets/Scripts/Net` | 온라인 대전 (`NetSession`: 빠른 매칭 / 초대코드 / IP 직접 연결) |
| `Assets/Editor` | 테스트·빌드 메뉴 (`MiniParty > Test`, `MiniParty > Build`) |

## 새 미니게임 추가하기

1. `MiniGameBase`를 상속한 클래스를 만듭니다 (`Title`, `Rules`, `Build()`, `OnAction()`, `Punish()`).
2. 플레이어 입력은 직접 결과를 바꾸지 말고 `Emit(kind, player, a, f)`로 액션을 보내고, `OnAction`에서 상태를 바꿉니다.
   그래야 한 PC 대전과 온라인 대전이 같은 코드로 동작합니다. 승패는 `DeclareWinner()` (방장/로컬만 결정).
3. `MiniGameRegistry.Games`에 한 줄 추가.
4. `MiniParty > Test > CPU vs CPU` 메뉴로 확인합니다.

## 온라인

- **초대코드 / 빠른 매칭**: Unity Relay 사용. 프로젝트가 Unity Cloud에 연결되어 있어야 합니다 (`Edit > Project Settings > Services`).
- **IP 직접 연결**: 같은 네트워크나 라디민·하마치 같은 VPN에서 포트 7777로 연결.
- 방장이 P1이며 미니게임 추첨·승패·데미지를 결정합니다.
- 한 PC에서 두 개를 띄워 테스트: 빌드 후 `MiniParty.exe -autohost -autoplay` 와 `MiniParty.exe -autojoin 127.0.0.1 -autoplay`.

## 빌드

`MiniParty > Build > Windows` → `Builds/MiniParty/` (zip으로 묶어 배포). `Builds/`는 git에 올리지 않습니다.

## 주의

- Play 중에 스크립트를 수정하면 도메인 리로드로 진행 중인 연출이 멈춥니다. 수정은 Play를 멈춘 뒤에.
