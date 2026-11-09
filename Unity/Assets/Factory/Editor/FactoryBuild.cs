using System;
using System.IO;
using System.Linq;
using SelfieRunner.Sim;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Factory.Editor
{
    /// <summary>Демо-билд фабрики: Unity.exe -batchmode -quit -projectPath Unity -executeMethod FactoryBuild.Demo -buildTarget Win64.
    /// Папка вывода — FACTORY_BUILD_OUT (по умолчанию Unity/Builds/demo). Данные ядра копируются туда, где их ищет CoreDataLoader.</summary>
    public static class FactoryBuildImpl
    {
        public static string[] ScenesToBuild()
            => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        public static string DataTarget(string outDir, string productName)
            => Path.Combine(outDir, productName + "_Data", "StreamingAssets", CoreDataLoader.BuildSubfolder);

        public static void CopyData(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from, "*.json"))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        }

        public static void Demo()
        {
            try
            {
                string outDir = Environment.GetEnvironmentVariable("FACTORY_BUILD_OUT") ?? "";
                if (outDir.Length == 0) outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "demo"));
                Directory.CreateDirectory(outDir);
                string name = string.IsNullOrWhiteSpace(PlayerSettings.productName) ? "Game" : PlayerSettings.productName;
                var scenes = ScenesToBuild();
                if (scenes.Length == 0) throw new InvalidOperationException("Build Settings пуст: нет сцены (запусти RaceSceneBuilder.Run)");
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(outDir, name + ".exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"Build Failed: {report.summary.result}, {report.summary.totalErrors} errors");
                    EditorApplication.Exit(1);
                    return;
                }
                CopyData(CoreDataLoader.DataDir(), DataTarget(outDir, name));
                Debug.Log($"FactoryBuild.Demo: built {options.locationPathName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Build Failed: {e}");
                EditorApplication.Exit(1);
            }
        }
    }
}

/// <summary>Точка входа для -executeMethod FactoryBuild.Demo (имя без пространства имён).</summary>
public static class FactoryBuild
{
    public static void Demo() => Factory.Editor.FactoryBuildImpl.Demo();
}
