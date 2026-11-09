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
        string dir = DataDir();
        return GameData.Parse(File.ReadAllText(Path.Combine(dir, "runners.json")),
            File.ReadAllText(Path.Combine(dir, "bull.json")), File.ReadAllText(Path.Combine(dir, "street.json")),
            File.ReadAllText(Path.Combine(dir, "characters.json")),
            File.ReadAllText(Path.Combine(dir, "actions.json")));
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
