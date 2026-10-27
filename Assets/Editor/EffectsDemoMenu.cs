using UnityEditor;
using UnityEngine;

namespace MiniParty.EditorTools
{
    static class EffectsDemoMenu
    {
        [MenuItem("MiniParty/Effects/데모 (Play 중에 실행)")]
        static void Demo()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.Log("[Effects] Play 모드에서 실행해 주세요.");
                return;
            }
            var overlay = GameObject.Find("GameManager/GameCanvas/Shake/Overlay");
            if (overlay == null) return;
            Juice.Demo((RectTransform)overlay.transform);

            // screenshots of each step to Temp/Shots/fx_N.png
            var dir = System.IO.Path.GetFullPath("Temp/Shots");
            System.IO.Directory.CreateDirectory(dir);
            double start = EditorApplication.timeSinceStartup;
            float[] at = { 0.12f, 1.2f, 2.35f, 3.5f, 4.7f, 5.8f };
            int next = 0;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (!EditorApplication.isPlaying || next >= at.Length)
                {
                    EditorApplication.update -= tick;
                    return;
                }
                if (EditorApplication.timeSinceStartup - start < at[next]) return;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"fx_{next}.png"));
                next++;
            };
            EditorApplication.update += tick;
        }
    }
}
