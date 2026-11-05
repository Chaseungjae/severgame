using UnityEditor;
using UnityEngine;

namespace MiniParty.EditorTools
{
    static class ScreenshotMenu
    {
        /// <summary>Logs which minigames each group scene will pick from.</summary>
        [MenuItem("MiniParty/Test/조별 미니게임 목록")]
        static void Groups()
        {
            int keep = MiniGameRegistry.ActiveGroup;
            for (int g = 1; g <= 4; g++)
            {
                MiniGameRegistry.ActiveGroup = g;
                var names = new System.Collections.Generic.List<string>();
                foreach (int i in MiniGameRegistry.ShuffledDeck()) names.Add(MiniGameRegistry.Games[i].Title);
                names.Sort();
                Debug.Log($"[조별] {MiniGameRegistry.GroupNames[g]}: {string.Join(", ", names)}");
            }
            MiniGameRegistry.ActiveGroup = keep;
        }

        static int n;

        /// <summary>Saves the Game view to Temp/Shots/manual_N.png (Play mode).</summary>
        [MenuItem("MiniParty/Test/스크린샷 (Play 중)")]
        static void Shot()
        {
            var dir = System.IO.Path.GetFullPath("Temp/Shots");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"manual_{n++}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[Shot] {path}");
        }
    }
}
