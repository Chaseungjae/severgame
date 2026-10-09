using UnityEditor;
using UnityEditor.SceneManagement;

namespace MiniParty.EditorTools
{
    /// <summary>
    /// A fresh clone has no record of the last opened scene, so Unity starts in an empty "Untitled" scene.
    /// Open the game scene automatically the first time the project is opened in an editor session.
    /// </summary>
    [InitializeOnLoad]
    static class OpenMainScene
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string Key = "MiniParty.OpenedMainScene";

        static OpenMainScene()
        {
            if (SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, true);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var active = EditorSceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path) && !active.isDirty)
                    EditorSceneManager.OpenScene(ScenePath);
            };
        }
    }
}
