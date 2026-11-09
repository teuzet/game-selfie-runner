using System.Collections.Generic;
using Game.Core;
using Game.Core.Economy;
using Game.Core.Events;
using Game.Core.Math;
using Game.Core.Race;
using Xunit;

public class PhasesTests
{
    static readonly GameData Data = GameData.Load();

    static RaceConfig Cfg(ulong seed) => new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Human, 0), new SeatConfig(SeatKind.Human, 1),
        new SeatConfig(SeatKind.Human, 2), new SeatConfig(SeatKind.Human, 3),
    });

    static int Countdown => Phases.CountdownTicks(Data.BullData);
    static int FinishTicks => Phases.FinishTicks(Data.BullData);

    static TickInputs RandomInputs(ref DetRandom rng)
    {
        var ti = TickInputs.Empty;
        for (int s = 0; s < 4; s++) ti = ti.With(s, new PlayerInput((int)(rng.NextUInt() % 3) - 1, 0, false));
        return ti;
    }

    /// <summary>Все на старте, бегун 0 за 0.3 м до арены; остальные далеко, бык позади.</summary>
    static Simulation NearArena(ulong seed)
    {
        var sim = new Simulation(Cfg(seed), Data).Running();
        sim.State.Runners[0].S = sim.ArenaStart.Raw - Fixed.FromFraction(3, 10).Raw;
        return sim;
    }

    static int Find(Simulation sim, RaceEventType type)
    {
        for (int i = 0; i < sim.Events.Count; i++) if (sim.Events[i].Type == type) return i;
        return -1;
    }

    [Fact]
    public void CountdownIsThreeSecondsRunnersStandViewers100()
    {
        var sim = new Simulation(Cfg(5), Data);
        Assert.Equal(RacePhase.Countdown, sim.State.Phase);
        Assert.Equal(90, Countdown);
        var s0 = new long[4];
        for (int i = 0; i < 4; i++) s0[i] = sim.State.Runners[i].S;
        for (int t = 0; t < Countdown - 1; t++)
        {
            // руль и спринт во время отсчёта игнорируются
            var ti = TickInputs.Empty;
            for (int s = 0; s < 4; s++) ti = ti.With(s, new PlayerInput(1, 1, true));
            sim.Step(ti);
            Assert.Equal(RacePhase.Countdown, sim.State.Phase);
            Assert.Equal(0, sim.Events.Count);
        }
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(s0[i], sim.State.Runners[i].S);
            Assert.Equal(0L, sim.State.Runners[i].Speed);
            Assert.Equal(100, Viewers.ViewersOf(sim.State, i));
            Assert.Equal(0, Viewers.DonationsOf(sim.State, i));
        }
        sim.Step(TickInputs.Empty); // «0»
        Assert.Equal(RacePhase.Running, sim.State.Phase);
        int e = Find(sim, RaceEventType.RaceStart);
        Assert.True(e >= 0);
        Assert.Equal(Countdown - 1, sim.Events[e].Tick);
        // следующий тик — бегуны бегут
        var run = TickInputs.Empty;
        for (int s = 0; s < 4; s++) run = run.With(s, new PlayerInput(0, 1, false));
        sim.Step(run);
        Assert.True(sim.State.Runners[0].S > s0[0]);
    }

    [Fact]
    public void RaceClosesWithin15sAfterFirstOnArena()
    {
        var sim = NearArena(11);
        int soonTick = -1, finishTick = -1, soonSeat = -1;
        for (int t = 0; t < 1000 && sim.State.Phase != RacePhase.Over; t++)
        {
            sim.Step(TickInputs.Empty.With(0, new PlayerInput(0, 0, false)));
            int a = Find(sim, RaceEventType.FinishSoon);
            if (a >= 0) { soonTick = sim.Events[a].Tick; soonSeat = sim.Events[a].Seat; }
            int b = Find(sim, RaceEventType.RaceFinish);
            if (b >= 0) finishTick = sim.Events[b].Tick;
        }
        Assert.Equal(0, soonSeat);
        Assert.True(soonTick >= 0 && finishTick > soonTick);
        Assert.Equal(15 * Simulation.TicksPerSecond, FinishTicks);
        Assert.True(finishTick - soonTick <= FinishTicks, $"закрылся через {finishTick - soonTick} тиков");
        Assert.Equal(RacePhase.Over, sim.State.Phase);
        for (int i = 0; i < 4; i++) Assert.True(Phases.IsFinished(sim.State, i));
        Assert.Equal(soonTick, sim.State.Viewing[0].FinishTick);
        Assert.Equal(finishTick, sim.State.Viewing[1].FinishTick);
    }

    [Fact]
    public void ArenaViewersFrozenOthersFinishInPlace()
    {
        var sim = NearArena(12);
        while (Find(sim, RaceEventType.FinishSoon) < 0) sim.Step(TickInputs.Empty.With(0, new PlayerInput(0, 0, false)));
        Assert.Equal(RacePhase.Finishing, sim.State.Phase);
        long v0 = sim.State.Viewing[0].Viewers, d0 = sim.State.Viewing[0].Donations;
        long v1 = sim.State.Viewing[1].Viewers;
        long s1 = sim.State.Runners[1].S;
        sim.Step(TickInputs.Empty);
        while (sim.State.Phase == RacePhase.Finishing)
        {
            sim.Step(TickInputs.Empty);
            Assert.Equal(v0, sim.State.Viewing[0].Viewers);
            Assert.Equal(d0, sim.State.Viewing[0].Donations);
        }
        Assert.Equal(RacePhase.Over, sim.State.Phase);
        Assert.NotEqual(v1, sim.State.Viewing[1].Viewers); // не добежавший копил до конца таймера
        // после закрытия состояние неподвижно
        ulong h = sim.StateHash();
        long viewers = sim.State.Viewing[1].Viewers, s = sim.State.Runners[1].S;
        for (int t = 0; t < 30; t++) sim.Step(TickInputs.Empty.With(1, new PlayerInput(0, 1, false)));
        Assert.Equal(viewers, sim.State.Viewing[1].Viewers);
        Assert.Equal(s, sim.State.Runners[1].S);
        Assert.True(s >= s1);
        Assert.Equal(0, sim.Events.Count);
        Assert.NotEqual(h, 0UL);
    }

    [Fact]
    public void AllOnArenaClosesEarly()
    {
        var sim = new Simulation(Cfg(13), Data).Running();
        for (int i = 0; i < 4; i++) sim.State.Runners[i].S = sim.ArenaStart.Raw + Fixed.FromInt(i).Raw;
        sim.Step(TickInputs.Empty);
        Assert.Equal(RacePhase.Over, sim.State.Phase);
        Assert.True(Find(sim, RaceEventType.FinishSoon) >= 0);
        Assert.True(Find(sim, RaceEventType.RaceFinish) >= 0);
    }

    [Fact]
    public void WinnerByDonationsThenViewersThenSeat()
    {
        var sim = new Simulation(Cfg(14), Data);
        var st = sim.State;
        var order = new int[4];
        long[] don = { 1000, 3000, 3000, 2000 };
        long[] vw = { 900, 200, 500, 300 };
        for (int i = 0; i < 4; i++)
        {
            st.Viewing[i].Donations = Fixed.FromInt((int)don[i]).Raw;
            st.Viewing[i].Viewers = Fixed.FromInt((int)vw[i]).Raw;
        }
        Results.Standings(st, order);
        Assert.Equal(new[] { 2, 1, 3, 0 }, order); // равные донаты 3000: больше зрителей у места 2
        Assert.Equal(2, Results.Winner(st));
        // полное равенство — меньшее место выше
        st.Viewing[1].Viewers = st.Viewing[2].Viewers;
        Results.Standings(st, order);
        Assert.Equal(new[] { 1, 2, 3, 0 }, order);
        Assert.Equal(1, Results.Winner(st));
    }

    [Fact]
    public void HighlightsMatchRecordedHistory()
    {
        int checkedFails = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var sim = new Simulation(Cfg(seed), Data);
            var rng = new DetRandom(seed * 31 + 7);
            var viewers = new List<long[]>(); var hot = new List<bool[]>(); var noSig = new List<bool[]>(); var fin = new List<bool[]>();
            for (int t = 0; t < 6000 && sim.State.Phase != RacePhase.Over; t++)
            {
                sim.Step(RandomInputs(ref rng));
                var v = new long[4]; var h = new bool[4]; var n = new bool[4]; var f = new bool[4];
                for (int i = 0; i < 4; i++)
                {
                    v[i] = sim.State.Viewing[i].Viewers;
                    h[i] = sim.State.Viewing[i].Zone == ViewerZone.Hot;
                    n[i] = sim.State.Tosses[i].NoSignalTicks > 0;
                    f[i] = Phases.IsFinished(sim.State, i);
                }
                viewers.Add(v); hot.Add(h); noSig.Add(n); fin.Add(f);
            }
            int ticks = viewers.Count;
            var best = sim.State.Best;

            // максимум зрителей: первый строго больший в порядке (тик, место); финишировавшие уже заморожены
            long maxV = 0; int maxSeat = -1, maxTick = -1;
            for (int t = 0; t < ticks; t++)
                for (int i = 0; i < 4; i++)
                    if (viewers[t][i] > maxV) { maxV = viewers[t][i]; maxSeat = i; maxTick = t; }
            Assert.Equal(maxV, best.MaxViewers.Value);
            Assert.Equal(maxSeat, best.MaxViewers.Seat);
            Assert.Equal(maxTick, best.MaxViewers.Tick);

            // самый длинный отрезок в горячей зоне (отрезок рвётся финишем)
            long bestRun = 0;
            for (int i = 0; i < 4; i++)
            {
                int run = 0;
                for (int t = 0; t < ticks; t++)
                {
                    if (hot[t][i] && !(fin[t][i] && t > 0 && fin[t - 1][i])) { run++; if (run > bestRun) bestRun = run; }
                    else run = 0;
                }
            }
            Assert.Equal(bestRun, best.LongestHot.Value);
            if (bestRun > 0)
            {
                int s = best.LongestHot.Seat, start = best.LongestHot.Tick;
                Assert.True(start == 0 || !hot[start - 1][s]);
                for (int k = 0; k < bestRun; k++) Assert.True(hot[start + k][s]);
            }

            // самый дорогой фейл: потеря зрителей от подброса до подъёма
            long bestCost = 0;
            for (int i = 0; i < 4; i++)
            {
                for (int t = 0; t < ticks; t++)
                {
                    if (!noSig[t][i] || (t > 0 && noSig[t - 1][i])) continue;
                    long start = viewers[t][i];
                    int e = t;
                    while (e < ticks - 1 && noSig[e][i]) e++;
                    for (int k = t; k <= e; k++) if (start - viewers[k][i] > bestCost) bestCost = start - viewers[k][i];
                }
            }
            Assert.Equal(bestCost, best.CostliestFail.Value);
            if (bestCost > 0)
            {
                int s = best.CostliestFail.Seat, t0 = best.CostliestFail.Tick;
                Assert.True(noSig[t0][s] && (t0 == 0 || !noSig[t0 - 1][s]));
                checkedFails++;
            }
        }
        Assert.True(checkedFails > 0, "ни одного фейла в прогонах — тест ничего не проверил");
    }

    [Fact]
    public void FullRaceTwoRunsGiveEqualHashEveryTick()
    {
        var a = new Simulation(Cfg(9), Data);
        var b = new Simulation(Cfg(9), Data);
        var ra = new DetRandom(3); var rb = new DetRandom(3);
        for (int t = 0; t < 4000; t++)
        {
            a.Step(RandomInputs(ref ra)); b.Step(RandomInputs(ref rb));
            Assert.Equal(a.StateHash(), b.StateHash());
        }
        // и клон догоняет прямой прогон: фазы и моменты входят в Clone/хэш
        var c = a.Clone();
        Assert.Equal(a.StateHash(), c.StateHash());
        Assert.Equal(a.State.Best.MaxViewers.Value, c.State.Best.MaxViewers.Value);
    }

    [Fact]
    public void PhaseStateInHash()
    {
        var a = new Simulation(Cfg(2), Data);
        var b = new Simulation(Cfg(2), Data).Running();
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
