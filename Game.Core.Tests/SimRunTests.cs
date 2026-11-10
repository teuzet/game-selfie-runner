using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Game.Core;
using Game.Core.Race;
using Game.Sim;
using Xunit;

/// <summary>F-025: консольный прогон Game.Sim — детерминизм вывода и то, что метрики и хроника считаются (пороги задаёт плейтест).</summary>
public class SimRunTests
{
    static readonly GameData Data = GameData.Load();

    static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "game-sim-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static string RepoRoot()
    {
        string? d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Game.sln"))) d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
        return d ?? throw new FileNotFoundException("Game.sln");
    }

    static string Cli(string outDir, int seeds, ulong from, params string[] extra)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var args = new System.Collections.Generic.List<string> { "--seeds", seeds.ToString(), "--from", from.ToString(), "--out", outDir };
        args.AddRange(extra);
        int code = Program.Run(args.ToArray(), o, e);
        Assert.True(code == 0, e.ToString());
        return o.ToString();
    }

    [Fact]
    public void SameSeedGivesByteIdenticalMetricsAndChronicle()
    {
        string a = TempDir(), b = TempDir();
        try
        {
            Cli(a, 2, 7); Cli(b, 2, 7);
            Assert.Equal(File.ReadAllBytes(Path.Combine(a, "metrics.json")), File.ReadAllBytes(Path.Combine(b, "metrics.json")));
            foreach (string seed in new[] { "7", "8" })
                Assert.Equal(File.ReadAllBytes(Path.Combine(a, "chronicle-" + seed + ".md")), File.ReadAllBytes(Path.Combine(b, "chronicle-" + seed + ".md")));
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    [Fact]
    public void RunsAreIndependentOfBatchSize()
    {
        string a = TempDir(), b = TempDir();
        try
        {
            Cli(a, 1, 9); Cli(b, 3, 8);
            Assert.Equal(File.ReadAllText(Path.Combine(a, "chronicle-9.md")), File.ReadAllText(Path.Combine(b, "chronicle-9.md")));
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    [Fact]
    public void MetricsAreComputedAndMatchState()
    {
        string dir = TempDir();
        try
        {
            Cli(dir, 3, 1);
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "metrics.json")));
            var root = doc.RootElement;
            Assert.Equal(3, root.GetProperty("runCount").GetInt32());
            var runs = root.GetProperty("runs");
            Assert.Equal(3, runs.GetArrayLength());

            for (int n = 0; n < 3; n++)
            {
                var run = runs[n];
                ulong seed = (ulong)(1 + n);
                Assert.Equal((long)seed, run.GetProperty("seed").GetInt64());
                Assert.True(run.GetProperty("finished").GetBoolean());
                Assert.True(run.GetProperty("durationSec").GetDouble() > 30);
                Assert.True(run.GetProperty("longestQuietSec").GetDouble() > 0);
                Assert.True(run.GetProperty("leaderChangesTotal").GetInt32() >= run.GetProperty("leaderChanges").GetInt32());

                // независимый прогон того же сида: числа метрик совпадают с итоговым состоянием
                var sim = new Simulation(RaceRecorder.MakeConfig(seed, Data), Data);
                for (int t = 0; t < 40000 && sim.State.Phase != RacePhase.Over; t++) sim.Step(TickInputs.Empty);
                Assert.Equal(RacePhase.Over, sim.State.Phase);
                var st = sim.State;
                var order = new int[4];
                Results.Standings(st, order);
                var win = run.GetProperty("winner");
                Assert.Equal(order[0], win.GetProperty("seat").GetInt32());
                Assert.Equal(sim.CharacterOf(order[0]).Id, win.GetProperty("character").GetString());

                var seats = run.GetProperty("seats");
                Assert.Equal(4, seats.GetArrayLength());
                int[] places = new int[5];
                for (int i = 0; i < 4; i++)
                {
                    var s = seats[i];
                    Assert.Equal(i, s.GetProperty("seat").GetInt32());
                    Assert.Equal(Game.Core.Economy.Viewers.DonationsOf(st, i), s.GetProperty("donations").GetInt32());
                    Assert.Equal(Game.Core.Economy.Viewers.ViewersOf(st, i), s.GetProperty("viewers").GetInt32());
                    Assert.Equal(st.Runners[i].Toss.Tossed, s.GetProperty("tosses").GetInt32());
                    Assert.Equal(st.Runners[i].Collision.Count, s.GetProperty("collisions").GetInt32());
                    Assert.Equal(st.Runners[i].Action.Shoved, s.GetProperty("shoves").GetInt32());
                    Assert.Equal(st.Runners[i].Action.Tripped, s.GetProperty("trips").GetInt32());
                    places[s.GetProperty("place").GetInt32()]++;
                    var z = s.GetProperty("zoneSec");
                    double sum = z.GetProperty("hot").GetDouble() + z.GetProperty("warm").GetDouble() + z.GetProperty("cool").GetDouble() + z.GetProperty("cold").GetDouble();
                    Assert.True(sum > 30, "время в зонах не считается: " + sum);
                }
                for (int p = 1; p <= 4; p++) Assert.Equal(1, places[p]);
                var gap = run.GetProperty("donationGap");
                Assert.Equal(Game.Core.Economy.Viewers.DonationsOf(st, order[0]), gap.GetProperty("first").GetInt32());
                Assert.Equal(Game.Core.Economy.Viewers.DonationsOf(st, order[1]), gap.GetProperty("second").GetInt32());
            }

            var sum2 = root.GetProperty("summary");
            Assert.Equal(0, sum2.GetProperty("unfinished").GetInt32());
            Assert.True(sum2.GetProperty("durationSec").GetProperty("median").GetDouble() > 0);
            Assert.True(sum2.GetProperty("longestQuietSec").GetProperty("median").GetDouble() > 0);
            foreach (string group in new[] { "byCharacter", "byTemperament" })
            {
                int apps = 0, wins = 0;
                foreach (var g in sum2.GetProperty(group).EnumerateArray())
                { apps += g.GetProperty("appearances").GetInt32(); wins += g.GetProperty("wins").GetInt32(); }
                Assert.Equal(12, apps);
                Assert.Equal(3, wins);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void F077MetricsAreConsistentAndDeterministic()
    {
        string a = TempDir(), b = TempDir();
        try
        {
            Cli(a, 4, 1); Cli(b, 4, 1);
            string json = File.ReadAllText(Path.Combine(a, "metrics.json"));
            Assert.Equal(File.ReadAllBytes(Path.Combine(a, "metrics.json")), File.ReadAllBytes(Path.Combine(b, "metrics.json")));
            Assert.DoesNotMatch(new Regex(@"\d[eE][+-]?\d|NaN|Infinity"), json); // формат Fmt: целые и десятые, без float
            using var doc = JsonDocument.Parse(json);
            foreach (var run in doc.RootElement.GetProperty("runs").EnumerateArray())
            {
                var bull = run.GetProperty("bull");
                Assert.Equal(0, bull.GetProperty("tossesOfFinished").GetInt32());
                double far = bull.GetProperty("farPct").GetDouble();
                Assert.InRange(far, 0, 100);
                Assert.True(bull.GetProperty("distToCrowdMedianM").GetDouble() >= 0);
                Assert.True(bull.GetProperty("speedMps").GetDouble() > 0);
                Assert.True(bull.GetProperty("charges").GetInt32() >= 0);
                Assert.True(run.GetProperty("leaderChangesNoToss").GetInt32() >= 0);
                foreach (var s in run.GetProperty("seats").EnumerateArray())
                {
                    Assert.Equal(s.GetProperty("tosses").GetInt32(), s.GetProperty("tossesCharge").GetInt32() + s.GetProperty("tossesContact").GetInt32());
                    Assert.InRange(s.GetProperty("zeroViewerPct").GetDouble(), 0, 100);
                    Assert.Equal(s.GetProperty("donations").GetInt32() - Data.EconomyData.TossDonations * s.GetProperty("tosses").GetInt32(),
                        s.GetProperty("donationsNoToss").GetInt32());
                }
            }
            var sum = doc.RootElement.GetProperty("summary");
            foreach (string k in new[] { "bullDistToCrowdMedianM", "bullFarPct", "bullSpeedMps", "seatZeroViewerSec", "seatTossesCharge", "leaderChangesNoToss" })
                Assert.True(sum.GetProperty(k).TryGetProperty("p90", out _), k);
            foreach (var g in sum.GetProperty("byCharacter").EnumerateArray())
                Assert.True(g.TryGetProperty("tossesChargeTotal", out _) && g.TryGetProperty("zeroViewerSecMedian", out _));

            string chron = File.ReadAllText(Path.Combine(a, "chronicle-1.md"));
            Assert.Matches(new Regex(@"^--- 0:05 \|.*\| бык: s \d+ м, \d+\.\d м/с$", RegexOptions.Multiline), chron);
            Assert.Matches(new Regex(@"Бык подбросил: .* \((в рывке|догнал)\)"), chron);
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    [Fact]
    public void ChronicleReadsAsStreamWithSnapshotsAndSummary()
    {
        string dir = TempDir();
        try
        {
            Cli(dir, 1, 1);
            string text = File.ReadAllText(Path.Combine(dir, "chronicle-1.md"));
            Assert.Matches(new Regex(@"^\d+:\d\d \S.* \(\S+\)", RegexOptions.Multiline), text);
            Assert.Matches(new Regex(@"^\d+:\d\d .* \([а-я]+\) .*; бык в \d+ м от ", RegexOptions.Multiline), text);
            Assert.Matches(new Regex(@"^--- 0:05 \| места: 1 .*до быка, м: .*зрители: .*донаты: ", RegexOptions.Multiline), text);
            Assert.Contains("--- 1:00 |", text);
            Assert.Contains("## Итог", text);
            Assert.Contains("Победитель:", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SeatsCoverAllCharactersAndEveryTemperament()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var cfg = RaceRecorder.MakeConfig(seed, Data);
            var chars = new bool[4]; var temps = new bool[Data.BotData.Temperaments.Length];
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(SeatKind.Bot, cfg.GetSeat(i).Kind);
                chars[cfg.GetSeat(i).CharacterId] = true; temps[cfg.GetSeat(i).BotTemperament] = true;
            }
            Assert.All(chars, c => Assert.True(c));
            Assert.All(temps, c => Assert.True(c));
        }
    }

    /// <summary>F-082: sessions.jsonl — строка на отслеживаемого участника, формат карты темпа тула sim.</summary>
    [Fact]
    public void SessionsJsonlHasRowsInSimToolFormat()
    {
        string dir = TempDir();
        try
        {
            const int seeds = 3;
            Cli(dir, seeds, 1);
            byte[] raw = File.ReadAllBytes(Path.Combine(dir, "sessions.jsonl"));
            Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF, "BOM");
            string[] lines = System.Text.Encoding.UTF8.GetString(raw).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(4 * seeds + 2 * seeds, lines.Length); // обычные места + служебные забеги idle и random (gate-seeds = seeds)

            using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "metrics.json")));
            var durations = new System.Collections.Generic.Dictionary<long, double>();
            foreach (var run in metrics.RootElement.GetProperty("runs").EnumerateArray())
                durations[run.GetProperty("seed").GetInt64()] = run.GetProperty("durationSec").GetDouble();

            var wins = new System.Collections.Generic.Dictionary<long, int>();
            var archetypes = new System.Collections.Generic.Dictionary<string, int>();
            for (int n = 0; n < lines.Length; n++)
            {
                Assert.DoesNotMatch(new Regex(@"\d[eE][+-]?\d|NaN|Infinity"), lines[n]);
                using var doc = JsonDocument.Parse(lines[n]);
                var row = doc.RootElement;
                long seed = row.GetProperty("seed").GetInt64();
                string arch = row.GetProperty("archetype").GetString()!;
                archetypes.TryGetValue(arch, out int ac); archetypes[arch] = ac + 1;
                bool service = arch == "idle" || arch == "random";
                if (!service) Assert.Contains(arch, new[] { "coward", "adrenaline", "troll" });
                Assert.Equal(n >= 4 * seeds, service);
                string outcome = row.GetProperty("outcome").GetString()!;
                Assert.Contains(outcome, new[] { "win", "loss", "draw" });
                if (outcome == "win") { long k = seed * 10 + (arch == "idle" ? 1 : arch == "random" ? 2 : 0); wins.TryGetValue(k, out int wc); wins[k] = wc + 1; }
                // длительность забега совпадает с metrics.json (там же те же сиды: служебные забеги идут тем же ходом)
                double dur = row.GetProperty("duration_s").GetDouble();
                Assert.True(dur > 30);
                if (!service) Assert.Equal(durations[seed], dur);
                Assert.InRange(row.GetProperty("margin").GetDouble(), 0, 1);
                foreach (var ev in row.GetProperty("events").EnumerateArray())
                {
                    Assert.True(ev.TryGetProperty("t", out var t) && t.GetDouble() >= 0 && t.GetDouble() <= dur + 1);
                    Assert.True(ev.TryGetProperty("threat", out _) || ev.TryGetProperty("reward", out _) || ev.TryGetProperty("decision", out _) || ev.TryGetProperty("novel", out _), lines[n]);
                    if (ev.TryGetProperty("threat", out var th)) Assert.InRange(th.GetDouble(), 0, 1);
                    if (ev.TryGetProperty("reward", out var rw)) Assert.True(rw.GetDouble() > 0);
                    if (ev.TryGetProperty("decision", out var de))
                    {
                        Assert.InRange(de.GetProperty("options").GetInt32(), 2, 3);
                        Assert.InRange(de.GetProperty("spread").GetDouble(), 0, 1);
                    }
                }
            }
            Assert.All(wins.Values, w => Assert.Equal(1, w)); // не больше одного win на забег: ключ — сид и вид забега
            Assert.Equal(seeds, archetypes["idle"]);
            Assert.Equal(seeds, archetypes["random"]);
            Assert.Equal(4 * seeds, archetypes["coward"] + archetypes["adrenaline"] + archetypes["troll"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SessionsJsonlIsByteIdenticalForSameSeed()
    {
        string a = TempDir(), b = TempDir();
        try
        {
            Cli(a, 2, 5); Cli(b, 2, 5);
            Assert.Equal(File.ReadAllBytes(Path.Combine(a, "sessions.jsonl")), File.ReadAllBytes(Path.Combine(b, "sessions.jsonl")));
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    [Fact]
    public void GateSeedsKeySetsNumberOfServiceRuns()
    {
        string dir = TempDir();
        try
        {
            Cli(dir, 2, 1, "--gate-seeds", "1");
            Assert.Equal(4 * 2 + 2, File.ReadAllLines(Path.Combine(dir, "sessions.jsonl")).Length);
            Cli(dir, 1, 1, "--gate-seeds", "0");
            Assert.Equal(4, File.ReadAllLines(Path.Combine(dir, "sessions.jsonl")).Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IdleServiceSeatDoesNothingAndRandomIsDriven()
    {
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var idle = RaceRecorder.Run(seed, Data, ServiceInput.Idle);
            Assert.True(idle.Finished);
            Assert.Equal(SeatKind.Human, RaceRecorder.MakeConfig(seed, Data, true).GetSeat(0).Kind);
            Assert.Equal(0, idle.Seats[0].Shoves + idle.Seats[0].Trips);
            Assert.Single(idle.Sessions);

            var rnd = RaceRecorder.Run(seed, Data, ServiceInput.Random);
            Assert.True(rnd.Finished);
            Assert.Single(rnd.Sessions);
            Assert.Contains("\"archetype\":\"random\"", rnd.Sessions[0]);
            Assert.Contains("\"decision\"", rnd.Sessions[0]); // случайный вход меняет темп
        }
    }

    [Fact]
    public void PlaytestPaceTargetsNamesServiceArchetypes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "playtest", "pace-targets.json")));
        var a = doc.RootElement.GetProperty("archetypes");
        Assert.Equal("adrenaline", a.GetProperty("smart").GetString());
        Assert.Equal(ServiceInput.Random, a.GetProperty("random").GetString());
        Assert.Equal(ServiceInput.Idle, a.GetProperty("idle").GetString());
    }

    [Fact]
    public void PlaytestSimJsonCallsGameSim()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "playtest", "sim.json")));
        var cmd = doc.RootElement.GetProperty("command");
        var parts = new string[cmd.GetArrayLength()];
        for (int i = 0; i < parts.Length; i++) parts[i] = cmd[i].GetString()!;
        Assert.Equal(new[] { "dotnet", "run", "--project", "Game.Sim", "-c", "Release", "--", "--seeds", "{seeds}", "--from", "{first_seed}", "--out", "{out}" }, parts);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Game.Sim", "Game.Sim.csproj")));
    }
}
