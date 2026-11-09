using System;
using System.IO;
using System.Text;
using Game.Core.Bots;
using Game.Core.Bull;
using Game.Core.Data;
using Game.Core.Economy;
using Game.Core.Runners;
using Game.Core.Street;

namespace Game.Core
{
    /// <summary>
    /// Все Data/*.json ядра одним объектом: сумма данных модулей. Модуль сам объявляет имя своего файла (*Data.FileName)
    /// и парсит себя; новый JSON — одно поле здесь и одна строка сборки в Parse, сигнатура Parse не меняется.
    /// Parse диск не трогает; Load нужен только тестам и Game.Sim (хост Unity читает файлы сам и зовёт Parse).
    /// </summary>
    public sealed class GameData
    {
        public RunnerData RunnerData { get; }
        public BullData BullData { get; }
        public StreetData StreetData { get; }
        public CharacterData CharacterData { get; }
        public ActionData ActionData { get; }
        public EconomyData EconomyData { get; }
        public BotData BotData { get; }

        /// <summary>Сборка из готовых данных модулей (Parse и тесты, которым нужен нестандартный набор).</summary>
        public GameData(RunnerData runnerData, BullData bullData, StreetData streetData, CharacterData characterData,
            ActionData actionData, EconomyData economyData, BotData botData)
        {
            RunnerData = runnerData;
            BullData = bullData;
            StreetData = streetData;
            CharacterData = characterData;
            ActionData = actionData;
            EconomyData = economyData;
            BotData = botData;
        }

        public static GameData Parse(DataFiles files) => new GameData(
            RunnerData.Parse(files.Get(RunnerData.FileName)),
            BullData.Parse(files.Get(BullData.FileName)),
            StreetData.Parse(files.Get(StreetData.FileName)),
            CharacterData.Parse(files.Get(CharacterData.FileName)),
            ActionData.Parse(files.Get(ActionData.FileName)),
            EconomyData.Parse(files.Get(EconomyData.FileName)),
            BotData.Parse(files.Get(BotData.FileName)));

        /// <summary>Читает все *.json папки в набор файлов (для тестов и Game.Sim).</summary>
        public static DataFiles ReadFiles(string? dir = null)
        {
            dir ??= FindDataDir();
            var files = new DataFiles();
            foreach (string path in Directory.GetFiles(dir, "*.json"))
                files.Add(Path.GetFileName(path), File.ReadAllText(path, Encoding.UTF8));
            return files;
        }

        /// <summary>Читает папку dir; null — искать Game.Core/Data вверх от AppContext.BaseDirectory.</summary>
        public static GameData Load(string? dir = null) => Parse(ReadFiles(dir));

        private static string FindDataDir()
        {
            string? d = AppContext.BaseDirectory;
            while (d != null)
            {
                string c = Path.Combine(d, "Game.Core", "Data");
                if (File.Exists(Path.Combine(c, StreetData.FileName))) return c;
                c = Path.Combine(d, "Data");
                if (File.Exists(Path.Combine(c, StreetData.FileName))) return c;
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            throw new FileNotFoundException("Game.Core/Data not found");
        }
    }
}
