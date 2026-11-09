using System;
using System.IO;
using System.Text;
using Game.Core.Bull;
using Game.Core.Runners;
using Game.Core.Street;

namespace Game.Core
{
    /// <summary>
    /// Все Data/*.json ядра одним объектом. Новая core-фича с JSON добавляет сюда поле и аргумент Parse.
    /// Parse диск не трогает; Load нужен только тестам и RaceRunner (хост Unity читает файлы сам).
    /// </summary>
    public sealed class GameData
    {
        public RunnerData RunnerData { get; }
        public BullData BullData { get; }
        public StreetData StreetData { get; }

        public GameData(RunnerData runnerData, BullData bullData, StreetData streetData)
        {
            RunnerData = runnerData;
            BullData = bullData;
            StreetData = streetData;
        }

        public static GameData Parse(string runnersJson, string bullJson, string streetJson) =>
            new GameData(RunnerData.Parse(runnersJson), BullData.Parse(bullJson), StreetData.Parse(streetJson));

        /// <summary>Читает файлы из dir; null — искать Game.Core/Data вверх от AppContext.BaseDirectory.</summary>
        public static GameData Load(string? dir = null)
        {
            if (dir == null)
            {
                string? d = AppContext.BaseDirectory;
                while (d != null)
                {
                    string c = Path.Combine(d, "Game.Core", "Data");
                    if (File.Exists(Path.Combine(c, "street.json"))) { dir = c; break; }
                    c = Path.Combine(d, "Data");
                    if (File.Exists(Path.Combine(c, "street.json"))) { dir = c; break; }
                    d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
                if (dir == null) throw new FileNotFoundException("Game.Core/Data not found");
            }
            return Parse(Read(dir, "runners.json"), Read(dir, "bull.json"), Read(dir, "street.json"));
        }

        private static string Read(string dir, string name) => File.ReadAllText(Path.Combine(dir, name), Encoding.UTF8);
    }
}
