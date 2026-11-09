using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MiniParty.Group1Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace MiniParty
{
    /// <summary>
    /// 1조 테스트 씬(Scenes/Groups/Group1_Reaction.unity) 전용 테스트 패널. 화면 왼쪽 위에 뜬다.
    ///
    ///  - 게임 고르기: 1조 게임 6개 전체 / 하나만 반복
    ///  - 모드: 나 vs CPU · CPU vs CPU (한 PC) · 1조 전용 서버 접속
    ///  - 서버 모드 (에디터): UDPServer/Group1Server 를 빌드해서 콘솔 창으로 띄우고, 테스트 봇을 상대로 붙여 준다
    ///  - [시작] / F5 : 설정대로 씬을 다시 불러와 로비를 건너뛰고 바로 시작.  F2 : 패널 열기/닫기
    ///
    /// 공용 코드는 건드리지 않고 MiniGameRegistry.ForcedGame, DevArgs.Set 훅만 쓴다.
    /// </summary>
    public class Group1TestPanel : MonoBehaviour
    {
        public enum Mode { HumanVsCpu, CpuVsCpu, Server }

        const string Prefs = "Group1Test.";
        static readonly string[] ModeNames = { "나 vs CPU", "CPU vs CPU", "1조 서버" };
        static readonly string[] ModeHints =
        {
            "한 PC에서 내가 직접 플레이 (Space/Enter · 마우스), 상대는 CPU",
            "둘 다 CPU — 게임이 끝까지 도는지 구경·확인",
            "1조 전용 UDP 서버에 접속. 서버가 게임 추첨·승패 판정",
        };

        // ---- 설정 (PlayerPrefs 에 저장, 씬을 다시 불러와도 유지) ----
        static int game = -1;                 // Group1Games.Games 번호, -1 = 전체 6개
        static Mode mode = Mode.HumanVsCpu;
        static string address = "127.0.0.1";
        static bool damageOne = true;         // 패배 데미지 1 → 라운드를 오래 테스트
        static bool cpuOnline = true;         // 서버 모드에서 내 쪽도 CPU 가 플레이
        static bool launchServer = true;      // (에디터) 시작할 때 로컬 서버를 띄움
        static bool launchBot = true;         // (에디터) 시작할 때 테스트 봇을 상대로 붙임

        // ---- 실행 상태 ----
        static bool pendingStart;             // 씬을 다시 불러온 뒤 로비를 자동으로 진행할지
        static bool open = true;
        static bool loaded;
        static Process serverProcess;
        static string serverArgs;
        static readonly List<Process> botProcesses = new List<Process>();
        static string toolStatus = "";
        static bool busy, quitHooked;

        bool autoApplied, autoCollapsed;
        GUIStyle box, label, small, button, toggle, field, title;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // (도메인 리로드를 끈 경우를 위해 Play 할 때마다 초기화)
            pendingStart = busy = false;
            open = true;
            loaded = false;
            toolStatus = "";
        }

        // ------------------------------------------------------------------------------------------ 수명

        void Awake()
        {
            LoadPrefs();
            ApplyHooks();
            if (!quitHooked)
            {
                quitHooked = true;
                Application.quitting += StopProcesses; // 에디터에서 Play 를 멈출 때도 불린다
            }
        }

        void Start()
        {
            // GameManager 의 테스트 데미지 스위치 (private 필드)
            var gm = FindAnyObjectByType<GameManager>();
            var field = typeof(GameManager).GetField("testDamageOne", BindingFlags.Instance | BindingFlags.NonPublic);
            if (gm != null && field != null) field.SetValue(gm, damageOne);
        }

        void OnDestroy()
        {
            if (!pendingStart) DevArgs.Clear("-autoserver");
        }

        /// <summary>강제 게임·온라인 CPU 스위치를 공용 코드 훅에 반영.</summary>
        static void ApplyHooks()
        {
            MiniGameRegistry.ForcedGame = game >= 0 ? RegistryIndex(Group1Games.Games[game].Type) : -1;
            if (mode == Mode.Server && cpuOnline) DevArgs.Set("-autoplay");
            else DevArgs.Clear("-autoplay");
        }

        static int RegistryIndex(Type type)
        {
            for (int i = 0; i < MiniGameRegistry.Games.Length; i++)
                if (MiniGameRegistry.Games[i].Type == type) return i;
            return -1;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f2Key.wasPressedThisFrame) open = !open;
                if (kb.f5Key.wasPressedThisFrame && !busy) Restart();
            }

            // 미니게임이 시작되면 패널을 한 번 접어서 클릭을 가리지 않게
            var gameLayer = GameObject.Find("GameManager/GameCanvas/Shake/GameLayer");
            if (!autoCollapsed && gameLayer != null && gameLayer.transform.childCount > 0)
            {
                autoCollapsed = true;
                open = false;
            }

            if (pendingStart && !autoApplied) TryAutoStart();
        }

        /// <summary>로비가 만들어지면 설정한 모드로 바로 진행시킨다 (씬을 다시 불러온 직후 한 번).</summary>
        void TryAutoStart()
        {
            var lobby = FindAnyObjectByType<LobbyScreen>();
            if (lobby == null) return;
            autoApplied = true;
            pendingStart = false;
            DevArgs.Clear("-autoserver"); // 로비가 이미 읽었음. 경기 뒤 로비로 돌아왔을 때 또 접속하지 않게

            const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                switch (mode)
                {
                    case Mode.CpuVsCpu:
                        typeof(LobbyScreen).GetMethod("ToggleCpu", Any).Invoke(lobby, new object[] { 0 });
                        break;
                    case Mode.HumanVsCpu:
                        var ready = (bool[])typeof(LobbyScreen).GetField("ready", Any).GetValue(lobby);
                        ready[0] = true;
                        typeof(LobbyScreen).GetMethod("RefreshStatus", Any).Invoke(lobby, new object[] { 0 });
                        break;
                    // Mode.Server: 로비가 DevArgs "-autoserver" 를 보고 직접 접속한다
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[1조 테스트] 로비 자동 진행 실패 (LobbyScreen 이 바뀌었나요?): {e.Message}");
            }
        }

        // ------------------------------------------------------------------------------------------ 시작

        void Restart()
        {
            SavePrefs();
            if (mode == Mode.Server && Application.isEditor && (launchServer || launchBot)) LaunchToolsThenRestart();
            else ReloadScene();
        }

        static void ReloadScene()
        {
            ApplyHooks();
            if (mode == Mode.Server) DevArgs.Set("-autoserver", address);
            else DevArgs.Clear("-autoserver");
            pendingStart = true;

            // 이전 접속 정리 (DontDestroyOnLoad 라 씬을 다시 불러와도 남는다)
            foreach (var link in FindObjectsByType<Group1ServerLink>(FindObjectsSortMode.None)) link.Close();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        // ------------------------------------------------------------------------------------------ 서버·봇 (에디터)

        static string ServerDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UDPServer", "Group1Server"));
        static string ServerDll => Path.Combine(ServerDir, "bin", "Release", "net10.0", "Group1Server.dll");
        static bool ServerRunning => serverProcess != null && !serverProcess.HasExited;

        static int Port
        {
            get
            {
                int colon = address.LastIndexOf(':');
                return colon > 0 && int.TryParse(address.Substring(colon + 1), out int p) ? p : G1Protocol.DefaultPort;
            }
        }

        static string WantedServerArgs()
        {
            string args = $"--port {Port}";
            if (game >= 0) args += $" --game {Group1ServerLink.ServerGameIdOf(Group1Games.Games[game].Type)}";
            if (damageOne) args += " --damage 1";
            return args;
        }

        static async void LaunchToolsThenRestart()
        {
            busy = true;
            try
            {
                if (launchServer && !(ServerRunning && serverArgs == WantedServerArgs()))
                {
                    if (!await StartServer()) return;
                }
                if (launchBot) StartBot();
            }
            finally
            {
                busy = false;
            }
            if (Application.isPlaying) ReloadScene();
        }

        static async Task<bool> StartServer()
        {
            StopServer();
            toolStatus = "서버 빌드 중... (dotnet build)";
            var build = await Task.Run(BuildServer);
            if (!Application.isPlaying) return false;
            if (!build.ok)
            {
                toolStatus = "서버 빌드 실패: " + build.message;
                Debug.LogError("[1조 테스트] 서버 빌드 실패\n" + build.message);
                return false;
            }

            string args = WantedServerArgs();
            serverProcess = Launch($"\"{ServerDll}\" {args}");
            if (serverProcess == null) return false;
            serverArgs = args;
            await Task.Delay(700);
            if (serverProcess.HasExited)
            {
                toolStatus = $"서버가 바로 꺼졌어요 — UDP {Port} 를 다른 서버가 쓰고 있나요?";
                serverProcess = null;
                return false;
            }
            toolStatus = $"서버 실행 중 (PID {serverProcess.Id}) {args}";
            Debug.Log($"[1조 테스트] 서버 실행: dotnet Group1Server.dll {args}");
            return true;
        }

        static (bool ok, string message) BuildServer()
        {
            try
            {
                var info = new ProcessStartInfo("dotnet", "build -c Release -nologo -v q")
                {
                    WorkingDirectory = ServerDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(info))
                {
                    var stdout = p.StandardOutput.ReadToEndAsync();
                    var stderr = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(180000)) return (false, "3분 안에 끝나지 않음");
                    return p.ExitCode == 0 ? (true, "") : (false, stdout.Result + stderr.Result);
                }
            }
            catch (Exception e)
            {
                return (false, ".NET SDK(dotnet)를 실행할 수 없어요: " + e.Message);
            }
        }

        static void StartBot()
        {
            var bot = Launch($"\"{ServerDll}\" bot 127.0.0.1:{Port}");
            if (bot == null) return;
            botProcesses.RemoveAll(b => b.HasExited);
            botProcesses.Add(bot);
            Debug.Log($"[1조 테스트] 봇 실행 (PID {bot.Id}) → 127.0.0.1:{Port}");
        }

        /// <summary>콘솔 창을 띄워서 실행 (서버 로그를 바로 볼 수 있게).</summary>
        static Process Launch(string arguments)
        {
            if (!File.Exists(ServerDll))
            {
                toolStatus = "서버가 아직 빌드되지 않았어요 (먼저 [서버 켜기])";
                return null;
            }
            try
            {
                return Process.Start(new ProcessStartInfo("dotnet", arguments) { WorkingDirectory = ServerDir, UseShellExecute = true });
            }
            catch (Exception e)
            {
                toolStatus = "실행 실패: " + e.Message;
                return null;
            }
        }

        static void StopServer()
        {
            if (ServerRunning) serverProcess.Kill();
            serverProcess = null;
            serverArgs = null;
        }

        static void StopProcesses()
        {
            try
            {
                StopServer();
                foreach (var b in botProcesses)
                    if (!b.HasExited) b.Kill();
                botProcesses.Clear();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[1조 테스트] 프로세스 정리 실패: " + e.Message);
            }
        }

        // ------------------------------------------------------------------------------------------ 저장

        static void LoadPrefs()
        {
            if (loaded) return;
            loaded = true;
            game = Mathf.Clamp(PlayerPrefs.GetInt(Prefs + "Game", -1), -1, Group1Games.Games.Length - 1);
            mode = (Mode)Mathf.Clamp(PlayerPrefs.GetInt(Prefs + "Mode", 0), 0, 2);
            address = PlayerPrefs.GetString(Prefs + "Address", "127.0.0.1");
            damageOne = PlayerPrefs.GetInt(Prefs + "DamageOne", 1) == 1;
            cpuOnline = PlayerPrefs.GetInt(Prefs + "CpuOnline", 1) == 1;
            launchServer = PlayerPrefs.GetInt(Prefs + "LaunchServer", 1) == 1;
            launchBot = PlayerPrefs.GetInt(Prefs + "LaunchBot", 1) == 1;
        }

        static void SavePrefs()
        {
            PlayerPrefs.SetInt(Prefs + "Game", game);
            PlayerPrefs.SetInt(Prefs + "Mode", (int)mode);
            PlayerPrefs.SetString(Prefs + "Address", address);
            PlayerPrefs.SetInt(Prefs + "DamageOne", damageOne ? 1 : 0);
            PlayerPrefs.SetInt(Prefs + "CpuOnline", cpuOnline ? 1 : 0);
            PlayerPrefs.SetInt(Prefs + "LaunchServer", launchServer ? 1 : 0);
            PlayerPrefs.SetInt(Prefs + "LaunchBot", launchBot ? 1 : 0);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------------------------------ 화면

        void OnGUI()
        {
            MakeStyles();
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            if (!open)
            {
                // 접힌 상태: 왼쪽 아래 작은 탭 (HP 바를 가리지 않게)
                if (GUI.Button(new Rect(12, 1080 - 52, 340, 40), "1조 테스트  (F2 열기 · F5 다시 시작)", button)) open = true;
                return;
            }

            GUILayout.BeginArea(new Rect(12, 12, 540, 1050));
            GUILayout.BeginVertical(box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("1조 테스트 패널", title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("접기 (F2)", button, GUILayout.Width(120))) open = false;
            GUILayout.EndHorizontal();

            GUI.enabled = !busy;

            GUILayout.Label("게임", label);
            var names = new string[Group1Games.Games.Length + 1];
            names[0] = "전체 6개 (무작위)";
            for (int i = 0; i < Group1Games.Games.Length; i++) names[i + 1] = Group1Games.Games[i].Title;
            game = GUILayout.SelectionGrid(game + 1, names, 2, button) - 1;

            GUILayout.Space(6);
            GUILayout.Label("모드", label);
            mode = (Mode)GUILayout.Toolbar((int)mode, ModeNames, button);
            GUILayout.Label(ModeHints[(int)mode], small);

            damageOne = GUILayout.Toggle(damageOne, " 패배 데미지 1 (라운드를 오래 테스트)", toggle);

            if (mode == Mode.Server) DrawServerOptions();

            GUILayout.Space(8);
            if (GUILayout.Button(busy ? "준비 중..." : "▶  이 설정으로 시작  (F5)", button, GUILayout.Height(52))) Restart();

            GUI.enabled = true;
            DrawStatus();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void DrawServerOptions()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("서버 주소", label, GUILayout.Width(110));
            address = GUILayout.TextField(address, 40, field).Trim();
            GUILayout.EndHorizontal();
            cpuOnline = GUILayout.Toggle(cpuOnline, " 내 쪽도 CPU가 플레이 (-autoplay)", toggle);

            if (!Application.isEditor) return;
            launchServer = GUILayout.Toggle(launchServer, $" 시작할 때 로컬 서버 실행 (UDP {Port}, 고른 게임만)", toggle);
            launchBot = GUILayout.Toggle(launchBot, " 시작할 때 테스트 봇을 상대로 붙이기", toggle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(ServerRunning ? "서버 다시 켜기" : "서버 켜기", button)) _ = StartServerOnly();
            GUI.enabled = GUI.enabled && ServerRunning;
            if (GUILayout.Button("서버 끄기", button))
            {
                StopServer();
                toolStatus = "서버를 껐어요";
            }
            if (GUILayout.Button("봇 1명 추가", button)) StartBot();
            GUI.enabled = !busy;
            GUILayout.EndHorizontal();
        }

        static async Task StartServerOnly()
        {
            busy = true;
            try { await StartServer(); }
            finally { busy = false; }
        }

        void DrawStatus()
        {
            GUILayout.Space(6);
            string forced = MiniGameRegistry.ForcedGame >= 0 ? MiniGameRegistry.Games[MiniGameRegistry.ForcedGame].Title : "1조 전체";
            GUILayout.Label($"지금 씬: {forced} · {ModeNames[(int)mode]}{(pendingStart ? " (자동 시작 대기)" : "")}", small);

            if (Application.isEditor && mode == Mode.Server)
            {
                botProcesses.RemoveAll(b => b.HasExited);
                string server = ServerRunning ? $"실행 중 (PID {serverProcess.Id})" : "꺼짐";
                GUILayout.Label($"로컬 서버: {server} · 봇 {botProcesses.Count}명", small);
            }
            var link = FindAnyObjectByType<Group1ServerLink>();
            if (link != null) GUILayout.Label($"접속: {link.Status.Replace('\n', ' ')}", small);
            if (!string.IsNullOrEmpty(toolStatus)) GUILayout.Label(toolStatus, small);
        }

        void MakeStyles()
        {
            if (box != null) return;
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 10, 12) };
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.16f, 0.92f));
            bg.Apply();
            box.normal.background = bg;
            title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            title.normal.textColor = Palette.Gold;
            label = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            label.normal.textColor = Color.white;
            small = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            small.normal.textColor = new Color(0.75f, 0.78f, 0.9f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 18, fixedHeight = 0, padding = new RectOffset(8, 8, 7, 7) };
            // 선택된 버튼(게임·모드)은 파란색으로 확실히 보이게
            var selected = new Texture2D(1, 1);
            selected.SetPixel(0, 0, new Color(0.30f, 0.55f, 1f));
            selected.Apply();
            button.onNormal.background = button.onHover.background = button.onActive.background = selected;
            button.onNormal.textColor = button.onHover.textColor = button.onActive.textColor = Color.white;
            toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 18 };
            toggle.normal.textColor = toggle.onNormal.textColor = Color.white;
            field = new GUIStyle(GUI.skin.textField) { fontSize = 20, padding = new RectOffset(8, 8, 6, 6) };
        }
    }
}
