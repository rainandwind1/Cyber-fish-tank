using CyberFishTank.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CyberFishTank.Editor
{
    public static class FishTankDemoSceneBuilder
    {
        [MenuItem("Cyber Fish Tank/Create Vertical Slice Scene")]
        public static void CreateVerticalSliceScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);
            var root = new GameObject("Fish Tank Demo");
            root.AddComponent<FishTankDemoController>();

            var scenePath = "Assets/Scenes/FishTankVerticalSlice.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (var i = scenes.Count - 1; i >= 0; i--)
                if (scenes[i].path == scenePath) scenes.RemoveAt(i);
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("Created Fish Tank vertical slice scene at " + scenePath);
        }
    }
}
