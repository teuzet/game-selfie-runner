using System.IO;
using System.Text;
using Game.Core;
using Game.Core.Data;
using UnityEngine;

namespace SelfieRunner.Sim
{
    /// <summary>Читает все *.json данных ядра в набор «имя файла → текст» и отдаёт в GameData.Parse. Ядро диск не читает.</summary>
    public static class CoreDataLoader
    {
        public const string PackageDataPath = "Packages/com.selfierunner.game-core/Data";
        public const string BuildSubfolder = "GameCoreData";

        public static string DataDir()
        {
#if UNITY_EDITOR
            string dir = Path.GetFullPath(PackageDataPath);
            if (Directory.Exists(dir)) return dir;
            // локальный пакет file: не лежит физически в Unity/Packages — берём путь, который разрешил редактор
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GameData).Assembly);
            return Path.Combine(info.resolvedPath, "Data");
#else
            return Path.Combine(Application.streamingAssetsPath, BuildSubfolder);
#endif
        }

        public static DataFiles ReadFiles(string dir = null)
        {
            var files = new DataFiles();
            foreach (string path in Directory.GetFiles(dir ?? DataDir(), "*.json"))
                files.Add(Path.GetFileName(path), File.ReadAllText(path, Encoding.UTF8));
            return files;
        }

        public static GameData Load(string dir = null) => GameData.Parse(ReadFiles(dir));
    }
}
