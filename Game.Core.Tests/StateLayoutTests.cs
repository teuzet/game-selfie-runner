using System;
using Game.Core;
using Xunit;

/// <summary>F-063: сведение состояния бегуна в RunnerState не меняет поведение. Хэши зафиксированы до рефакторинга.</summary>
public class StateLayoutTests
{
    static RaceConfig Cfg(ulong seed) => new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Bot, 0, 0), new SeatConfig(SeatKind.Bot, 1, 1),
        new SeatConfig(SeatKind.Bot, 2, 2), new SeatConfig(SeatKind.Bot, 3, 1),
    });

    // Хэши на тиках 1500, 3000, 4500, 6000.
    static ulong[] Run(ulong seed)
    {
        var sim = new Simulation(Cfg(seed));
        var h = new ulong[4];
        for (int t = 1; t <= 6000; t++)
        {
            sim.Step(TickInputs.Empty);
            if (t % 1500 == 0) h[t / 1500 - 1] = sim.StateHash();
        }
        return h;
    }

    static readonly (ulong seed, ulong[] hashes)[] Golden =
    {
        (1UL, new ulong[] { 3402283232199495703UL, 8526619444254312497UL, 13625915766832018182UL, 9473694523239921559UL }),
        (42UL, new ulong[] { 6785331411291185414UL, 8108664151891009243UL, 16491152769246986853UL, 4367177333539633355UL }),
        (777UL, new ulong[] { 14367466785548134369UL, 18033292107606601872UL, 8444599760930339500UL, 10795334735570521289UL }),
    };

    [Fact]
    public void RaceHashesMatchPreRefactorGolden()
    {
        var bad = "";
        foreach (var (seed, expected) in Golden)
        {
            var got = Run(seed);
            if (expected[0] != got[0] || expected[1] != got[1] || expected[2] != got[2] || expected[3] != got[3])
                bad += $"seed {seed}: {string.Join("UL, ", got)}UL\n";
        }
        Assert.True(bad.Length == 0, bad);
    }

    [Fact]
    public void CloneCoversAllRunnerFields()
    {
        var sim = new Simulation(Cfg(5));
        for (int t = 0; t < 3000; t++) sim.Step(TickInputs.Empty);
        var c = sim.State.Clone();
        Assert.Equal(sim.State.StateHash(), c.StateHash());
        for (int i = 0; i < 20; i++) sim.Step(TickInputs.Empty);
        var d = sim.State.Clone();
        Assert.Equal(sim.State.StateHash(), d.StateHash());
        Assert.NotEqual(c.StateHash(), d.StateHash());
    }
}
