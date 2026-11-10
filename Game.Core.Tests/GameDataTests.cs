using System;
using System.IO;
using Game.Core;
using Xunit;

public class GameDataTests
{
    static RaceConfig Cfg(ulong seed) => new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Human, 0), new SeatConfig(SeatKind.Human, 1),
        new SeatConfig(SeatKind.Human, 2), new SeatConfig(SeatKind.Bot, 3, 1),
    });

    static TickInputs[] MakeInputs(int ticks, uint seed)
    {
        var res = new TickInputs[ticks];
        uint x = seed;
        for (int t = 0; t < ticks; t++)
        {
            var ti = TickInputs.Empty;
            for (int s = 0; s < 4; s++)
            {
                x = x * 1664525u + 1013904223u;
                ti = ti.With(s, new PlayerInput((int)((x >> 8) % 3) - 1, (int)((x >> 12) % 3) - 1, ((x >> 20) & 1) != 0));
            }
            res[t] = ti;
        }
        return res;
    }

    static string DataDir()
    {
        string? d = AppContext.BaseDirectory;
        while (d != null)
        {
            string c = Path.Combine(d, "Game.Core", "Data");
            if (File.Exists(Path.Combine(c, "street.json"))) return c;
            d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        throw new FileNotFoundException("Game.Core/Data");
    }

    static GameData Parsed()
    {
        // Набор «имя файла → текст» собирается вручную из тех же файлов, что читает Load: Parse не знает про диск.
        var files = new Game.Core.Data.DataFiles();
        foreach (string path in Directory.GetFiles(DataDir(), "*.json"))
            files.Add(Path.GetFileName(path), File.ReadAllText(path));
        return GameData.Parse(files);
    }

    [Fact]
    public void ParseNamesTheMissingFile()
    {
        var files = new Game.Core.Data.DataFiles().Add("runners.json", GameData.ReadFiles().Get("runners.json"));
        var e = Assert.Throws<FileNotFoundException>(() => GameData.Parse(files));
        Assert.Contains("bull.json", e.Message);
    }

    [Fact]
    public void EveryModuleFileIsInDataDir()
    {
        var files = GameData.ReadFiles();
        foreach (string name in new[] { Game.Core.Runners.RunnerData.FileName, Game.Core.Bull.BullData.FileName,
            Game.Core.Street.StreetData.FileName, Game.Core.Runners.CharacterData.FileName, Game.Core.Runners.ActionData.FileName,
            Game.Core.Economy.EconomyData.FileName, Game.Core.Bots.BotData.FileName, Game.Core.Hints.HintData.FileName })
            Assert.True(files.Has(name), name);
    }

    [Fact]
    public void MissingKeyThrowsWithFileAndKey()
    {
        var e = Assert.Throws<FormatException>(() => Game.Core.Bull.BullData.Parse("{ \"penOffset\": 25 }"));
        Assert.Contains("bull.json", e.Message);
        Assert.Contains("lineS", e.Message);
        e = Assert.Throws<FormatException>(() => Game.Core.Runners.ActionData.Parse("{ \"shoveRange\": 1.5 }"));
        Assert.Contains("actions.json", e.Message);
        Assert.Contains("shoveCooldown", e.Message);
    }

    // F-083, design/F-102.md §4–5 (тюнить): bull.json anchorLag 20 (15–30); bots.json coward.jogAbove 30 (22–35) и jogHysteresis 5 (3–5).
    [Fact]
    public void F102KeysAreReadFromJsonAndAnchorLagIsRequired()
    {
        var data = Parsed();
        Assert.Equal(20, (double)data.BullData.AnchorLag);
        var coward = data.BotData.Find("coward")!;
        Assert.Equal(30, (double)coward.JogAbove);
        Assert.Equal(5, (double)coward.JogHysteresis);
        string json = File.ReadAllText(Path.Combine(DataDir(), "bull.json"));
        Assert.Contains("\"anchorLag\": 20,", json);
        var e = Assert.Throws<FormatException>(() => Game.Core.Bull.BullData.Parse(json.Replace("\"anchorLag\": 20,", "")));
        Assert.Contains("anchorLag", e.Message);
        // jogAbove у труса — число, которое меняет поведение: без ключа порог 0 (трусца всегда) — это ловит тест темпа BotCowardTests
        Assert.Contains("\"jogAbove\": 30", File.ReadAllText(Path.Combine(DataDir(), "bots.json")));
    }

    [Theory]
    // tossDonations 20 — ручка плейтеста итерации 2 (playtest/iter-2.md → «Тюнинг»; предел 0–80, design/F-101.md §3.3), было 50.
    [InlineData("collisionViewers", 40)] [InlineData("tossViewers", 120)] [InlineData("tossDonations", 20)]
    [InlineData("shoveViewers", 50)] [InlineData("tripViewers", 80)] [InlineData("gotShovedViewers", -30)]
    [InlineData("waterDonations", 80)] [InlineData("recordStep", 250)]
    [InlineData("frameStealRate", 6)] [InlineData("frameRange", 2)] [InlineData("frameHoldSec", 1)]
    public void EventBonusKeyIsReadFromJsonAndRequired(string key, int expected)
    {
        string json = File.ReadAllText(Path.Combine(DataDir(), "economy.json"));
        var d = Game.Core.Economy.EconomyData.Parse(json);
        var f = typeof(Game.Core.Economy.EconomyData).GetField(char.ToUpperInvariant(key[0]) + key.Substring(1))!;
        object v = f.GetValue(d)!;
        Assert.Equal(expected, v is int i ? i : ((Game.Core.Math.Fixed)v).ToInt());
        var e = Assert.Throws<FormatException>(() =>
            Game.Core.Economy.EconomyData.Parse(json.Replace("\"" + key + "\":", "\"" + key + "Gone\":")));
        Assert.Contains("economy.json", e.Message);
        Assert.Contains(key, e.Message);
    }

    [Fact]
    public void NotANumberThrowsWithFileAndKey()
    {
        string json = File.ReadAllText(Path.Combine(DataDir(), "economy.json"))
            .Replace("\"cap\":", "\"cap\": \"many\", \"x\":");
        var e = Assert.Throws<FormatException>(() => Game.Core.Economy.EconomyData.Parse(json));
        Assert.Contains("economy.json", e.Message);
        Assert.Contains("cap", e.Message);
    }

    [Fact]
    public void BrokenCharacterEntryNamesIndexAndKey()
    {
        string json = File.ReadAllText(Path.Combine(DataDir(), "characters.json")).Replace("\"tastiness\": 0.6", "\"tastyness\": 0.6");
        var e = Assert.Throws<FormatException>(() => Game.Core.Runners.CharacterData.Parse(json));
        Assert.Contains("characters.json", e.Message);
        Assert.Contains("characters[2]", e.Message);
        Assert.Contains("tastiness", e.Message);
    }

    [Fact]
    public void ParseAndLoadGiveEqualHashes()
    {
        var inputs = MakeInputs(3000, 5);
        var a = new Simulation(Cfg(42), Parsed());
        var b = new Simulation(Cfg(42), GameData.Load());
        var c = new Simulation(Cfg(42));
        for (int t = 0; t < inputs.Length; t++)
        {
            a.Step(inputs[t]); b.Step(inputs[t]); c.Step(inputs[t]);
            if (t % 100 == 0 || t == inputs.Length - 1)
            {
                Assert.Equal(a.StateHash(), b.StateHash());
                Assert.Equal(a.StateHash(), c.StateHash());
            }
        }
    }

    [Fact]
    public void StepDoesNotAllocate()
    {
        var inputs = MakeInputs(1000, 11);
        var sim = new Simulation(Cfg(8), Parsed());
        sim.Step(inputs[0]);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i < inputs.Length; i++) sim.Step(inputs[i]);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
