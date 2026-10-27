using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MiniParty.EditorTools
{
    /// <summary>
    /// Dev tool: enters play mode with both players as CPU, forces a list of minigames in order and saves
    /// screenshots of each minigame and its punishment to Temp/Shots. (MiniParty > Test > ...)
    /// </summary>
    public static class CpuMatchTest
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
        static readonly string ShotDir = Path.GetFullPath("Temp/Shots");

        static int[] queue;
        static int round, midShots, podShots;
        static bool configured, inPodium;
        static float gameStart, nextPod;
        static string curName;

        [MenuItem("MiniParty/Test/CPU vs CPU - All Games")]
        static void AllGames()
        {
            var list = new List<int>();
            for (int i = 0; i < MiniGameRegistry.Games.Length; i++) list.Add(i);
            Begin(list.ToArray());
        }

        [MenuItem("MiniParty/Test/CPU vs CPU - Newest Games")]
        static void NewestGames()
        {
            var wanted = new[] { typeof(BallCountGame), typeof(TypingGame) };
            var list = new List<int>();
            foreach (var t in wanted)
                for (int i = 0; i < MiniGameRegistry.Games.Length; i++)
                    if (MiniGameRegistry.Games[i].Type == t) list.Add(i);
            Begin(list.ToArray());
        }

        [MenuItem("MiniParty/Test/CPU vs CPU - Board Games")]
        static void BoardGames()
        {
            var list = new List<int>();
            for (int i = 0; i < MiniGameRegistry.Games.Length; i++)
                if (typeof(BoardGameBase).IsAssignableFrom(MiniGameRegistry.Games[i].Type)) list.Add(i);
            Begin(list.ToArray());
        }

        const string StateKey = "MiniParty.CpuMatchTest.Queue";

        static void Begin(int[] games)
        {
            if (Directory.Exists(ShotDir)) Directory.Delete(ShotDir, true);
            Directory.CreateDirectory(ShotDir);
            // entering play mode reloads the domain, so the queue survives in SessionState
            SessionState.SetString(StateKey, string.Join(",", games));
            if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
            Resume(); // (re-run after a domain reload by [InitializeOnLoadMethod] if play mode reloads)
        }

        [InitializeOnLoadMethod]
        static void Resume()
        {
            string saved = SessionState.GetString(StateKey, "");
            if (string.IsNullOrEmpty(saved)) return;
            var parts = saved.Split(',');
            queue = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) queue[i] = int.Parse(parts[i]);
            round = midShots = podShots = 0;
            gameStart = -1f;
            configured = inPodium = false;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Finish()
        {
            Restore();
            SessionState.EraseString(StateKey);
            EditorApplication.update -= Tick;
        }

        static void Force(int k)
        {
            var original = typeof(MiniGameRegistry).GetField("Games").GetValue(null) as System.Array;
            // stash the real table once so every forced entry refers to the original list
            if (backup == null) backup = (System.Array)original.Clone();
            var entry = backup.GetValue(queue[k % queue.Length]);
            for (int j = 0; j < original.Length; j++) original.SetValue(entry, j);
        }

        static System.Array backup;

        static void Restore()
        {
            if (backup == null) return;
            var original = typeof(MiniGameRegistry).GetField("Games").GetValue(null) as System.Array;
            for (int j = 0; j < original.Length; j++) original.SetValue(backup.GetValue(j), j);
            backup = null;
        }

        static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, name + ".png"));

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                // stopped (or a stale request from an earlier session): clean up
                if (configured || !EditorApplication.isPlayingOrWillChangePlaymode) Finish();
                return;
            }

            if (!configured)
            {
                var lobbyGo = GameObject.Find("GameManager/GameCanvas/Shake/Overlay/Lobby");
                if (lobbyGo == null) return;
                Application.runInBackground = true;
                var lobby = lobbyGo.GetComponent<LobbyScreen>();
                var gm = Object.FindAnyObjectByType<GameManager>();
                typeof(GameManager).GetField("minDamage", Any).SetValue(gm, 3);
                typeof(GameManager).GetField("maxDamage", Any).SetValue(gm, 5);
                Force(0);
                var toggle = typeof(LobbyScreen).GetMethod("ToggleCpu", Any);
                toggle.Invoke(lobby, new object[] { 0 });
                toggle.Invoke(lobby, new object[] { 1 });
                configured = true;
                return;
            }

            if (round >= queue.Length)
            {
                Finish();
                Debug.Log($"[CpuMatchTest] done — screenshots in {ShotDir}");
                return;
            }

            var stage = GameObject.Find("GameManager/GameCanvas/Shake/Stage");
            var gameLayer = GameObject.Find("GameManager/GameCanvas/Shake/GameLayer");
            var overlay = GameObject.Find("GameManager/GameCanvas/Shake/Overlay");
            if (stage == null) return;

            bool pod = stage.transform.childCount > 0;
            if (pod && !inPodium)
            {
                inPodium = true;
                podShots = 0;
                nextPod = Time.time + 1.6f;
                Force(round + 1);
            }
            if (!pod && inPodium)
            {
                inPodium = false;
                round++;
                gameStart = -1f;
                midShots = 0;
            }
            if (pod && podShots < 6 && Time.time >= nextPod)
            {
                Shot($"r{round}_pod_{podShots}");
                podShots++;
                nextPod = Time.time + 0.75f;
            }
            if (!pod && gameLayer.transform.childCount > 0 && overlay.transform.childCount == 0)
            {
                if (gameStart < 0f)
                {
                    gameStart = Time.time;
                    curName = gameLayer.transform.GetChild(0).name;
                }
                float[] at = { 4f, 25f };
                if (midShots < at.Length && Time.time - gameStart >= at[midShots])
                {
                    Shot($"r{round}_{curName}_{midShots}");
                    midShots++;
                }
            }
        }
    }
}
