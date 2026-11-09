# 1조 전용 UDP 서버 (Group1Server)

1조(반응·연타) 미니게임 6개를 Unity 밖에서 돌리는 전용 서버입니다.
외부 라이브러리 없이 `System.Net.Sockets.Socket`으로 UDP를 직접 다루고, 신뢰성(재전송·순서·중복 제거)도 직접 구현했습니다.

| 게임 | 서버가 하는 일 |
|---|---|
| 스페이스바 줄다리기 | 연타를 받아 깃발 위치를 계산 (0.05초마다 전원에게 전송), 결승선/15초 판정 |
| CPS 클릭 대결 | 클릭을 직접 세고(초당 25회 제한) 5초 뒤 판정. 상대에게는 서버가 센 값을 전달 |
| F1 반응 속도 | 히트 시작·대기 시간을 서버가 정함, 부정출발·반응 시간 비교, 2선승 |
| 에임 대결 | 명중(표적 번호·반응 시간) 검증·집계, 명중 수 → 평균 반응 속도로 판정 |
| 블럭 쌓기 | 층·위치·폭 검증 (한 층씩, 넓어지지 않게, 아래 블럭 위에), 먼저 떨어진 사람보다 높으면 승 |
| 타이밍 서바이벌 | 레벨·목숨 변화 검증, 탈락 판정 |

게임 추첨(1조 6개, 겹치지 않게), 라운드 동기화, 데미지·HP, 경기 종료도 서버가 결정합니다.
서버는 두 클라이언트 모두에게 "방장"이고, 클라이언트는 둘 다 게스트로 동작합니다.

## 실행

.NET 10 SDK 필요.

```
cd UDPServer/Group1Server
dotnet run -c Release                 # 서버 (UDP 33333)
dotnet run -c Release -- --damage 1   # 라운드당 데미지 1 (테스트)
dotnet run -c Release -- --verbose    # 모든 메시지 로그
dotnet run -c Release -- bot 127.0.0.1         # 봇 1명 접속 (Unity 1대로 혼자 시험)
dotnet run -c Release -- selftest              # 서버 + 봇 2명으로 경기 하나 끝까지 (성공 시 종료 코드 0)
```

다른 PC에서 접속하려면 Windows 방화벽에서 UDP 33333 인바운드를 허용하세요.

## 1조 테스트 씬 (가장 쉬운 방법)

`Assets/Scenes/Groups/Group1_Reaction.unity`를 열고 Play → 왼쪽 위 **1조 테스트 패널**.

- **게임**: 전체 6개 / 하나만 반복
- **모드**: 나 vs CPU · CPU vs CPU · 1조 서버
- **1조 서버 모드 (에디터)**: [시작]을 누르면 이 서버를 빌드해서 콘솔 창으로 띄우고(고른 게임만 나오게 `--game`), 테스트 봇을 상대로 붙인 뒤 접속합니다. Play를 멈추면 서버·봇 창도 같이 꺼집니다.
- **F5** 설정대로 다시 시작(로비 건너뜀) · **F2** 패널 열기/닫기

## Unity에서 접속

로비 → 온라인 1:1 → IP 직접 연결 → **1조 전용 서버** → 서버 PC의 IP 입력 (포트 생략 시 33333).
두 명이 같은 서버에 접속하면 들어온 순서대로 짝지어져 방이 만들어집니다 (방 여러 개 동시 가능).

빌드 두 개로 자동 테스트: `MiniParty.exe -autoserver 127.0.0.1 -autoplay` (CPU가 대신 플레이).

## 구조

```
Assets/Scripts/MiniGames/Group1_Reaction/Net/
  Shared/G1Protocol.cs   패킷 형식·메시지 번호·게임 번호   ← 서버와 Unity가 같은 파일을 컴파일
  Shared/G1Reliable.cs   신뢰성 계층 (Seq / Ack / 재전송 / 순서 정렬)
  Group1ServerLink.cs    Unity 클라이언트 소켓 (수신 스레드 → 메인 스레드 처리)
Assets/Scripts/Net/NetSession.cs   Mode.DedicatedServer 추가 (GameManager는 그대로)
UDPServer/Group1Server/
  Net/UdpSocket.cs       서버 소켓 (수신 스레드 → 큐 → 메인 루프)
  Server/G1Server.cs     접속·방 배정·Ping·시간 초과
  Server/Room.cs         경기 흐름 (Hello/Welcome, StartRound, Barrier/Go, 중계, 판정, RoundResult)
  Games/*Rules.cs        게임별 서버 규칙
  Bot/                   테스트 봇
```

### 패킷

```
[0]'G' [1]버전 [2]채널(0 비신뢰 / 1 신뢰 / 2 Ack) [3..6]Seq [7..]Payload([0]Op ...)
```

- 신뢰 패킷은 Ack가 올 때까지 0.2초마다 재전송, 받는 쪽은 Seq 순서대로 한 번씩만 처리
- 1초마다 Ping, 6초 동안 아무것도 안 오면 끊긴 것으로 처리 (상대에게 OpponentLeft)
- 게임 메시지(`Op.Net`)는 Unity의 `NetMsg`(Type, A, B, C, D, F)와 같은 모양

### 주의

- 게임 스크립트의 액션 번호(`ActPull` 등)를 바꾸면 `G1Protocol.cs`의 `G1Game` 상수와 `Games/*Rules.cs`도 같이 바꿔야 합니다.
- `Group1Games.cs`에서 게임을 빼면 서버가 그 게임을 골랐을 때 클라이언트가 접속을 끊습니다 (`Group1ServerLink.GameTypes`).
- 반응 시간·블럭 위치처럼 각 PC가 직접 재는 값은 서버가 범위만 검사합니다 (기존 온라인 구조와 같음).
