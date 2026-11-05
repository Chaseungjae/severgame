using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniParty.EditorTools
{
    /// <summary>
    /// MiniParty > Setup > UI 테마 연결: finds the imported free assets (Jua font, Kenney UI Pack / UI Audio /
    /// Input Prompts) wherever they were dropped in Assets, fixes their import settings and writes
    /// Assets/Resources/UiTheme.asset, which the runtime UI reads. Runs once automatically if the asset is missing.
    /// </summary>
    static class ThemeSetup
    {
        const string AssetPath = "Assets/Resources/UiTheme.asset";

        [InitializeOnLoadMethod]
        static void AutoRun()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && AssetDatabase.LoadAssetAtPath<UiTheme>(AssetPath) == null)
                    Run();
            };
        }

        [MenuItem("MiniParty/Setup/UI 테마 연결")]
        static void Run()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(AssetPath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<UiTheme>();
                AssetDatabase.CreateAsset(theme, AssetPath);
            }

            // font: Jua, with Malgun Gothic as fallback for symbols it lacks (◀ ▶ ● ·)
            string fontPath = FindPath("Jua-Regular.ttf", null);
            if (fontPath != null)
            {
                if (AssetImporter.GetAtPath(fontPath) is TrueTypeFontImporter fi)
                {
                    fi.fontNames = new[] { "Jua", "Malgun Gothic", "맑은 고딕" };
                    fi.SaveAndReimport();
                }
                theme.titleFont = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            }

            // button: grey depth button, 9-sliced (corners + bottom lip stay crisp)
            theme.button = SpriteAt(FindPath("button_rectangle_depth_flat.png", "kenney_ui-pack/PNG/Grey/Default/"), new Vector4(14, 18, 14, 12));

            // sounds (UI pack sounds first, UI Audio pack for the rest)
            theme.click = Clip("click-a.ogg", "kenney_ui-pack/Sounds/");
            theme.tap = Clip("tap-a.ogg", "kenney_ui-pack/Sounds/");
            theme.tick = Clip("tap-b.ogg", "kenney_ui-pack/Sounds/");
            theme.toggle = Clip("switch-a.ogg", "kenney_ui-pack/Sounds/");
            theme.go = Clip("switch-b.ogg", "kenney_ui-pack/Sounds/");
            theme.win = Clip("switch7.ogg", "kenney_ui-audio/");
            theme.lose = Clip("rollover2.ogg", "kenney_ui-audio/");
            theme.roll = Clip("click2.ogg", "kenney_ui-audio/");

            // key icons: every plain (non-outline) keyboard & mouse prompt
            var icons = new List<Sprite>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.Contains("Keyboard & Mouse/Default/")) continue;
                string file = Path.GetFileNameWithoutExtension(path);
                if (file.EndsWith("_outline")) continue;
                var sprite = SpriteAt(path, Vector4.zero);
                if (sprite != null) icons.Add(sprite);
            }
            theme.keyIcons = icons.ToArray();

            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Theme] 연결 완료 — 폰트 {(theme.titleFont ? "O" : "X")}, 버튼 {(theme.button ? "O" : "X")}, " +
                      $"효과음 {(theme.click ? "O" : "X")}, 키 아이콘 {theme.keyIcons.Length}개 ({AssetPath})");
        }

        /// <summary>Asset path of a file by exact name, optionally only under a folder hint.</summary>
        static string FindPath(string fileName, string hint)
        {
            foreach (var guid in AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(fileName), new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (Path.GetFileName(path) != fileName) continue;
                if (hint != null && !path.Contains(hint)) continue;
                return path;
            }
            return null;
        }

        static AudioClip Clip(string fileName, string hint)
        {
            string path = FindPath(fileName, hint);
            return path != null ? AssetDatabase.LoadAssetAtPath<AudioClip>(path) : null;
        }

        /// <summary>Imports a texture as a single UI sprite (no mipmaps, optional 9-slice border) and returns it.</summary>
        static Sprite SpriteAt(string path, Vector4 border)
        {
            if (path == null || !(AssetImporter.GetAtPath(path) is TextureImporter ti)) return null;
            bool dirty = ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single
                         || ti.mipmapEnabled || ti.spriteBorder != border;
            if (dirty)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.spriteBorder = border;
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(settings);
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
