using System.Collections.Generic;
using System.IO;

namespace Game.Core.Data
{
    /// <summary>
    /// Тексты Data/*.json одним набором «имя файла → текст». Это вход GameData.Parse: хост (тесты, Game.Sim, Unity) кладёт
    /// сюда все файлы, а каждый модуль сам берёт свой по объявленному имени (*Data.FileName). Только для сборки данных,
    /// в Step не используется.
    /// </summary>
    public sealed class DataFiles
    {
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>();

        public DataFiles Add(string name, string text) { _texts[name] = text; return this; }

        public bool Has(string name) => _texts.ContainsKey(name);

        /// <summary>Текст файла; нет в наборе — FileNotFoundException с именем (а не тихий пропуск).</summary>
        public string Get(string name)
        {
            if (!_texts.TryGetValue(name, out var text)) throw new FileNotFoundException("Нет файла данных " + name, name);
            return text;
        }

        /// <summary>Копия набора, где файл name заменён (тесты подменяют один JSON).</summary>
        public DataFiles With(string name, string text)
        {
            var c = new DataFiles();
            foreach (var kv in _texts) c._texts[kv.Key] = kv.Value;
            c._texts[name] = text;
            return c;
        }
    }
}
