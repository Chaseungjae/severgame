using UnityEngine;

namespace MiniParty
{
    /// <summary>
    /// Makes the game start in any scene: if the loaded scene has no GameManager (e.g. a fresh clone opened an empty
    /// "Untitled" scene), one is created automatically.
    /// </summary>
    static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureGame()
        {
            if (Object.FindAnyObjectByType<GameManager>() != null) return;
            if (Object.FindAnyObjectByType<AudioListener>() == null)
                new GameObject("AudioListener", typeof(AudioListener)); // rhythm game needs one to be heard
            new GameObject("GameManager", typeof(GameManager));
        }
    }
}
