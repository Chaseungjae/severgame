using UnityEngine;

namespace MiniParty
{
    /// <summary>
    /// Art / sound references for the code-built UI (Jua font, Kenney UI pack buttons, UI audio, input prompt icons).
    /// Lives at Assets/Resources/UiTheme.asset and is filled by MiniParty > Setup > UI 테마 연결.
    /// Every field is optional: anything missing falls back to the procedural look.
    /// </summary>
    public class UiTheme : ScriptableObject
    {
        [Header("폰트 (제목·버튼·숫자). 설명 글씨는 맑은 고딕")]
        public Font titleFont;

        [Header("버튼 (9-slice, 색은 코드에서 입힘)")]
        public Sprite button;

        [Header("효과음")]
        public AudioClip click;
        public AudioClip tick;
        public AudioClip go;
        public AudioClip toggle;
        public AudioClip tap;
        public AudioClip win;
        public AudioClip lose;
        public AudioClip roll;

        [Header("조작 키 아이콘 (Kenney Input Prompts)")]
        public Sprite[] keyIcons;

        static UiTheme current;
        static bool loaded;

        public static UiTheme Current
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    current = Resources.Load<UiTheme>("UiTheme");
                }
                return current;
            }
        }

        /// <summary>Key icon by file name (e.g. "keyboard_space"), or null.</summary>
        public Sprite Key(string name)
        {
            if (keyIcons == null) return null;
            foreach (var s in keyIcons)
                if (s != null && s.name == name) return s;
            return null;
        }
    }

    /// <summary>UI sound effects from the theme. Safe to call when a clip is missing.</summary>
    public static class Sfx
    {
        const int Voices = 8;
        static AudioSource[] sources;
        static int next;

        static void Play(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null) return;
            if (sources == null || sources[0] == null)
            {
                var go = new GameObject("Sfx");
                Object.DontDestroyOnLoad(go);
                sources = new AudioSource[Voices];
                for (int i = 0; i < Voices; i++)
                {
                    sources[i] = go.AddComponent<AudioSource>();
                    sources[i].playOnAwake = false;
                }
            }
            var src = sources[next];
            next = (next + 1) % Voices;
            src.pitch = pitch;
            src.PlayOneShot(clip, volume);
        }

        static UiTheme T => UiTheme.Current;

        public static void Click() { if (T) Play(T.click, 0.7f); }
        public static void Tick() { if (T) Play(T.tick, 0.8f); }
        public static void Go() { if (T) Play(T.go, 0.9f, 1.15f); }
        public static void Toggle() { if (T) Play(T.toggle, 0.7f); }
        public static void Tap() { if (T) Play(T.tap, 0.5f, Random.Range(0.95f, 1.1f)); }
        public static void Win() { if (T) Play(T.win, 0.9f, 1.2f); }
        public static void Lose() { if (T) Play(T.lose, 0.7f, 0.8f); }
        public static void Roll() { if (T) Play(T.roll, 0.6f, Random.Range(0.9f, 1.2f)); }
    }
}
