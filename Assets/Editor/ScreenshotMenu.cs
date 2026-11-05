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

        /// <summary>
        /// Sets the Game view to a fixed 1920x1080 so text renders at its real size (a small Game view
        /// rasterizes small Korean text at a few pixels and it looks broken). Uses internal editor API by reflection.
        /// </summary>
        [MenuItem("MiniParty/Test/Game 창 1920x1080")]
        static void FullHd()
        {
            var asm = typeof(EditorWindow).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance").GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { (int)GameViewSizeGroupType.Standalone });
            var groupType = group.GetType();

            int index = -1;
            int count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
            var getSize = groupType.GetMethod("GetGameViewSize");
            for (int i = 0; i < count; i++)
            {
                var s = getSize.Invoke(group, new object[] { i });
                var t = s.GetType();
                if ((int)t.GetProperty("width").GetValue(s) == 1920 && (int)t.GetProperty("height").GetValue(s) == 1080
                    && t.GetProperty("sizeType").GetValue(s).ToString() == "FixedResolution") { index = i; break; }
            }
            if (index < 0)
            {
                var sizeType = asm.GetType("UnityEditor.GameViewSize");
                var kind = asm.GetType("UnityEditor.GameViewSizeType");
                var size = System.Activator.CreateInstance(sizeType, System.Enum.Parse(kind, "FixedResolution"), 1920, 1080, "MiniParty 1080p");
                groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                index = count;
            }

            var gameView = EditorWindow.GetWindow(asm.GetType("UnityEditor.GameView"));
            gameView.GetType().GetMethod("SizeSelectionCallback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(gameView, new object[] { index, null });
            Debug.Log($"[Shot] Game 창 1920x1080 (index {index})");
        }
    }
}
