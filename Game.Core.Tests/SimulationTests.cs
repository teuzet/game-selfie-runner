using System;
using Game.Core;
using Xunit;

public class SimulationTests
{
    static RaceConfig Cfg(ulong seed) => new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Human, 0), new SeatConfig(SeatKind.Human, 1),
        new SeatConfig(SeatKind.Human, 2), new SeatConfig(SeatKind.Bot, 3, 1),
    });

    static InputLog MakeLog(int ticks, uint seed)
    {
        var log = new InputLog();
        uint x = seed;
        for (int t = 0; t < ticks; t++)
        {
            var ti = TickInputs.Empty;
            for (int s = 0; s < 4; s++)
            {
                x = x * 1664525u + 1013904223u;
                ti = ti.With(s, new PlayerInput((int)((x >> 8) % 3) - 1, (int)((x >> 12) % 3) - 1, ((x >> 20) & 1) != 0));
            }
            log.Record(t, ti);
        }
        return log;
    }

    [Fact]
    public void TickRateIs30() => Assert.Equal(30, Simulation.TicksPerSecond);

    [Fact]
    public void ConfigIsStored()
    {
        var c = Cfg(7);
        Assert.Equal(7UL, c.Seed);
        Assert.Equal(SeatKind.Bot, c.GetSeat(3).Kind);
        Assert.Equal(1, c.GetSeat(3).BotTemperament);
        Assert.Throws<ArgumentException>(() => new RaceConfig(1, new SeatConfig[3]));
    }

    [Fact]
    public void InputsPackRoundTrip()
    {
        for (int s = -1; s <= 1; s++)
        for (int p = -1; p <= 1; p++)
        for (int a = 0; a < 2; a++)
        {
            var pi = new PlayerInput(s, p, a == 1);
            Assert.Equal(pi, PlayerInput.Unpack(pi.Pack()));
        }
        var ti = TickInputs.Empty.With(0, new PlayerInput(-1, 1, true)).With(2, new PlayerInput(1, -1, false));
        var bytes = ti.ToBytes();
        Assert.Equal(4, bytes.Length);
        Assert.Equal(ti, TickInputs.ReadFrom(bytes));
        Assert.Equal(new PlayerInput(0, 0, false), TickInputs.Empty.Get(1));
        Assert.Equal(new PlayerInput(-1, 1, true), ti.Get(0));
    }

    [Fact]
    public void TwoRunsGiveEqualHashEveryTick()
    {
        var log = MakeLog(3000, 5);
        var a = new Simulation(Cfg(42));
        var b = new Simulation(Cfg(42));
        for (int t = 0; t < 3000; t++)
        {
            a.Step(log.Get(t)); b.Step(log.Get(t));
            Assert.Equal(a.StateHash(), b.StateHash());
        }
        Assert.Equal(3000, a.Tick);
        Assert.NotEqual(new Simulation(Cfg(43)).StateHash(), new Simulation(Cfg(42)).StateHash());
    }

    [Fact]
    public void DifferentInputsChangeHash()
    {
        var a = new Simulation(Cfg(1));
        var b = new Simulation(Cfg(1));
        a.Step(TickInputs.Empty);
        b.Step(TickInputs.Empty.With(0, new PlayerInput(1, 1, false)));
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void BotSeatIgnoresExternalInput()
    {
        var a = new Simulation(Cfg(1));
        var b = new Simulation(Cfg(1));
        a.Step(TickInputs.Empty);
        b.Step(TickInputs.Empty.With(3, new PlayerInput(1, 1, true)));
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void CloneAtTick100CaughtUpTo200EqualsDirectRun()
    {
        var log = MakeLog(200, 9);
        var direct = new Simulation(Cfg(3));
        Simulation? clone = null;
        for (int t = 0; t < 200; t++)
        {
            if (t == 100) clone = direct.Clone();
            direct.Step(log.Get(t));
        }
        Assert.NotNull(clone);
        Assert.Equal(100, clone!.Tick);
        log.Replay(clone, 200);
        Assert.Equal(200, clone.Tick);
        Assert.Equal(direct.StateHash(), clone.StateHash());

        var other = new Simulation(Cfg(3));
        other.CopyFrom(clone);
        Assert.Equal(clone.StateHash(), other.StateHash());
    }

    [Fact]
    public void StepDoesNotAllocate()
    {
        var log = MakeLog(1000, 11);
        var sim = new Simulation(Cfg(8));
        var inputs = new TickInputs[1000];
        for (int i = 0; i < 1000; i++) inputs[i] = log.Get(i);
        sim.Step(inputs[0]); // прогрев JIT
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i < 1000; i++) sim.Step(inputs[i]);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }
}
