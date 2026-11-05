using UnityEditor;
using UnityEngine;

namespace MiniParty.EditorTools
{
    static class ScreenshotMenu
    {
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
