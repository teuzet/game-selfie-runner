using SelfieRunner.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RaceSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Race.unity";

    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera", typeof(Camera));
        cam.tag = "MainCamera";
        new GameObject("RaceBootstrap", typeof(RaceBootstrap));
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
    }
}
