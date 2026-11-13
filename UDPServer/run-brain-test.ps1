<#
2조(Group2_Brain) 자동 대전 테스트 — UDP 게임 서버 1개 + 게임 클라이언트 2개를 한 PC 에서 띄운다.
클라이언트는 -autoplay 로 CPU 가 대신 조작하므로 손을 대지 않아도 경기가 끝까지 진행된다.

  powershell -ExecutionPolicy Bypass -File UDPServer\run-brain-test.ps1
  powershell -ExecutionPolicy Bypass -File UDPServer\run-brain-test.ps1 -Port 7790 -Manual

  -Build   게임 빌드 폴더 (기본: 저장소 옆의 '빌드' 폴더)
  -Port    서버 포트 (기본 7777)
  -Manual  CPU 자동 조작을 끄고 두 창을 직접 조작한다
  -Minutes 결과를 기다리는 최대 시간 (기본 10분)
#>
param(
    [string]$Build = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '빌드'),
    [int]$Port = 7777,
    [switch]$Manual,
    [int]$Minutes = 10
)

$ErrorActionPreference = 'Stop'

$game = Get-ChildItem -Path $Build -Filter *.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'UnityCrashHandler64.exe' } | Select-Object -First 1
if (-not $game) { throw "빌드 폴더에서 게임 실행 파일을 찾지 못했습니다: $Build" }

if (Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue) {
    throw "UDP $Port 포트를 이미 다른 프로그램(켜 둔 서버 등)이 쓰고 있습니다. 끄거나 -Port 로 다른 포트를 지정하세요."
}

$logs = Join-Path $env:TEMP 'MiniPartyTest'
New-Item -ItemType Directory -Force $logs | Out-Null
$serverLog = Join-Path $logs 'server.log'
Remove-Item $serverLog -ErrorAction SilentlyContinue

# 1. 서버 빌드 (Release — Debug 실행 파일이 켜져 있어도 겹치지 않는다) 후 새 터미널 창에서 실행
$project = Join-Path $PSScriptRoot 'UDPServer'
Write-Host '서버 빌드 중...'
dotnet build $project -c Release -v q --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw '서버 빌드 실패' }
$serverExe = Join-Path $project 'bin\Release\net10.0\UDPServer.exe'
$server = Start-Process $serverExe -ArgumentList "--port $Port --log `"$serverLog`"" -PassThru

$deadline = (Get-Date).AddSeconds(15)
while (-not (Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue)) {
    if ((Get-Date) -gt $deadline -or $server.HasExited) { throw '서버가 시작되지 않았습니다.' }
    Start-Sleep -Milliseconds 300
}

# 2. 클라이언트 2개 — 먼저 띄운 쪽이 P1 이 되도록 시간차를 둔다
$common = "-server 127.0.0.1:$Port -screen-fullscreen 0 -screen-width 960 -screen-height 540"
if (-not $Manual) { $common += ' -autoplay' }
$p1 = Start-Process $game.FullName -ArgumentList "$common -logFile `"$logs\p1.log`"" -PassThru
Start-Sleep -Seconds 2
$p2 = Start-Process $game.FullName -ArgumentList "$common -p2 -logFile `"$logs\p2.log`"" -PassThru

Write-Host "서버 + 클라이언트 2개 실행됨 (로그: $logs)"
Write-Host '경기 진행 상황 ----------------------------------------'

# 3. 서버 로그를 따라가며 경기 종료를 기다린다
$shown = 0
$finished = $false
$deadline = (Get-Date).AddMinutes($Minutes)
while ((Get-Date) -lt $deadline) {
    if (Test-Path $serverLog) {
        $lines = @(Get-Content $serverLog -Encoding UTF8)
        for (; $shown -lt $lines.Count; $shown++) { Write-Host $lines[$shown] }
        if ($lines -match '경기 종료') { $finished = $true; break }
    }
    if ($p1.HasExited -and $p2.HasExited) { break }
    Start-Sleep -Seconds 1
}

$all = @(Get-Content $serverLog -Encoding UTF8)
$rounds = @($all -match '결과 — 승자').Count
$serverJudged = @($all -match '서버 판정\)').Count
$rejected = @($all -match '규칙에 맞지 않는|남의 입력').Count

Write-Host '-------------------------------------------------------'
if ($finished) {
    Write-Host "통과: $rounds 라운드 진행, 그중 서버 판정 $serverJudged 라운드, 거부된 입력 $rejected 개" -ForegroundColor Green
} else {
    Write-Host "실패: $Minutes 분 안에 경기가 끝나지 않았습니다 ($rounds 라운드까지 진행). 로그: $logs" -ForegroundColor Red
}
Write-Host '창은 그대로 둡니다. 다 보셨으면 게임 창과 서버 창을 닫아 주세요.'
if (-not $finished) { exit 1 }
