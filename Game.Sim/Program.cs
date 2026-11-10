using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core;

namespace Game.Sim
{
    /// <summary>
    /// Консольный прогон забегов 4 ботов без Unity:
    /// <c>dotnet run --project Game.Sim -- --seeds N --from S --out DIR [--data DIR] [--gate-seeds N]</c> → DIR/metrics.json, DIR/chronicle-&lt;seed&gt;.md и DIR/sessions.jsonl
    /// (F-082: все места обычных забегов + N служебных забегов на архетип idle/random, N по умолчанию = --seeds).
    /// Вывод детерминирован: один сид — побайтово тот же результат.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

        public static int Run(string[] args, TextWriter log, TextWriter err)
        {
            int seeds = 40, gateSeeds = -1; ulong from = 1; string? outDir = null, dataDir = null;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("после " + a + " нужно значение");
                    switch (a)
                    {
                        case "--seeds": seeds = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--from": from = ulong.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--out": outDir = Next(); break;
                        case "--data": dataDir = Next(); break;
                        case "--gate-seeds": gateSeeds = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                        default: throw new ArgumentException("неизвестный аргумент " + a);
                    }
                }
                if (seeds < 1) throw new ArgumentException("--seeds должно быть ≥ 1");
                if (gateSeeds < 0) gateSeeds = seeds;
                if (outDir == null) throw new ArgumentException("нужен --out DIR");
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
            {
                err.WriteLine("Game.Sim: " + e.Message);
                err.WriteLine("Использование: dotnet run --project Game.Sim -- --seeds N --from S --out DIR [--data DIR]");
                return 2;
            }

            var data = GameData.Load(dataDir);
            Directory.CreateDirectory(outDir);
            var runs = new List<RunRecord>(seeds);
            var utf8 = new UTF8Encoding(false);
            for (int i = 0; i < seeds; i++)
            {
                ulong seed = from + (ulong)i;
                var rec = RaceRecorder.Run(seed, data);
                runs.Add(rec);
                File.WriteAllText(Path.Combine(outDir, "chronicle-" + seed.ToString(CultureInfo.InvariantCulture) + ".md"), rec.Chronicle, utf8);
                log.WriteLine("сид " + seed + ": " + (rec.Finished ? "ok" : "НЕ ЗАКРЫТ") + ", победитель " +
                    Names.Character(rec.Seats[rec.WinnerSeat].Character) + " (" + Names.Temperament(rec.Seats[rec.WinnerSeat].Temperament) + ")");
            }
            File.WriteAllText(Path.Combine(outDir, "metrics.json"), MetricsJson.Build(runs), utf8);

            // sessions.jsonl: все места обычных забегов + служебные забеги гейта (idle, random) на тех же сидах
            var sessions = new StringBuilder();
            foreach (var r in runs) foreach (string line in r.Sessions) sessions.Append(line).Append('\n');
            foreach (string svc in ServiceInput.All)
                for (int i = 0; i < gateSeeds; i++)
                {
                    var rec = RaceRecorder.Run(from + (ulong)i, data, svc);
                    foreach (string line in rec.Sessions) sessions.Append(line).Append('\n');
                    log.WriteLine("гейт " + svc + ", сид " + rec.Seed + ": " + (rec.Finished ? "ok" : "НЕ ЗАКРЫТ"));
                }
            File.WriteAllText(Path.Combine(outDir, "sessions.jsonl"), sessions.ToString(), utf8);
            log.WriteLine("готово: " + runs.Count + " забегов → " + outDir);
            return 0;
        }
    }
}
