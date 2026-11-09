using System;
using Game.Core;
using Game.Core.Economy;
using Game.Core.Math;
using Xunit;

public class ViewersTests
{
    // Места: 0 гламур (1.4), 1 спортсмен (0.9), 2 бабуля (1.2), 3 ботаник (1.0).
    static Simulation Make(ulong seed = 7) => new Simulation(new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Human, 0), new SeatConfig(SeatKind.Human, 1),
        new SeatConfig(SeatKind.Human, 2), new SeatConfig(SeatKind.Human, 3),
    }));

    /// <summary>Ставит всех бегунов на dist метров впереди быка и гонит только экономику.</summary>
    static void Run(Simulation sim, double dist, int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                sim.State.Runners[i].S = sim.State.Bull.S + Fixed.FromFraction((int)(dist * 10), 10).Raw;
                sim.State.Runners[i].X = sim.State.Bull.X;
            }
            Viewers.Step(sim);
            sim.State.Tick++;
        }
    }

    [Fact]
    public void HotZoneTenSecondsNerdAndGlamour()
    {
        var sim = Make();
        Run(sim, 4, 300);
        double nerd = (double)Fixed.FromRaw(sim.State.Runners[3].Viewing.Viewers);
        double glam = (double)Fixed.FromRaw(sim.State.Runners[0].Viewing.Viewers);
        Assert.InRange(nerd, 249.9, 250.01);
        Assert.InRange(glam, 309.8, 310.01);
        Assert.Equal(ViewerZone.Hot, sim.State.Runners[3].Viewing.Zone);
    }

    [Fact]
    public void RunningAwayLosesViewers()
    {
        // Числа зон — ручки плейтеста (playtest/iter-1.md): «эфир пустой» −6/с, «холодно» 20–50 м −2/с.
        var sim = Make();
        // Набранное сверх пола уходит; ниже пола (F-101 §3.1) — нет.
        int floor = sim.EconomyData.Floor.ToInt();
        sim.State.Runners[3].Viewing.Viewers = Fixed.FromInt(floor + 60).Raw;
        Run(sim, 60, 300); // −6/с × 10 с = −60 → ровно пол
        Assert.Equal(floor, Viewers.ViewersOf(sim.State, 3));
        var sim2 = Make();
        sim2.State.Runners[3].Viewing.Viewers = Fixed.FromInt(floor + 20).Raw;
        Run(sim2, 45, 150); // −2/с × 5 с = −10 (45 м — «холодно»)
        Assert.InRange((double)Fixed.FromRaw(sim2.State.Runners[3].Viewing.Viewers), floor + 9.9, floor + 10.01);
    }

    [Fact]
    public void RunnerFarFromBullKeepsFloorAndEarns()
    {
        // design/F-101.md → «3.1 Доход вдали от быка: пол зрителей = подписчики (вариант А, число 100)»:
        // «Зрители места никогда не опускаются ниже пола 100 (= старт)»; «Пассивный доход не меняется:
        // зрители / 100 в секунду, то есть на полу — 1 донат/с».
        var sim = Make();
        var d = sim.EconomyData;
        Assert.Equal(d.Start, d.Floor);
        Run(sim, 60, 60 * 30); // 60 м от быка, 60 с
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(d.Floor.ToInt(), Viewers.ViewersOf(sim.State, i));
            Assert.InRange(Viewers.DonationsOf(sim.State, i), 59, 60);
        }
    }

    [Fact]
    public void NoSignalAndFrameStealDoNotGoBelowFloor()
    {
        var sim = Make();
        sim.State.Runners[3].Toss.NoSignalTicks = 100000;
        Run(sim, 4, 30 * 20);
        Assert.Equal(sim.EconomyData.Floor.ToInt(), Viewers.ViewersOf(sim.State, 3));
        // «Влез в кадр»: вор у жертвы на полу ничего не получает (F-101 §3.1 п.3)
        var s2 = Make();
        int floor = s2.EconomyData.Floor.ToInt();
        s2.State.Runners[0].S = s2.State.Runners[1].S + Fixed.FromInt(1).Raw; // жертва 0 впереди вора 1
        s2.State.Runners[0].X = s2.State.Runners[1].X;
        for (int t = 0; t < 30 * 5; t++) { Game.Core.Economy.FrameSteal.Step(s2); s2.State.Tick++; }
        Assert.Equal(floor, Viewers.ViewersOf(s2.State, 0));
        Assert.Equal(floor, Viewers.ViewersOf(s2.State, 1));
    }

    [Fact]
    public void FewZeroViewerPlacesAtFinishOver40Seeds()
    {
        int zero = 0, total = 0;
        var table = new System.Text.StringBuilder("seed place viewers\n");
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var rec = Game.Sim.RaceRecorder.Run(seed, GameData.Load());
            foreach (var s in rec.Seats)
            {
                total++;
                if (s.Viewers == 0) zero++;
                table.Append(seed).Append(' ').Append(s.Place).Append(' ').Append(s.Viewers).Append('\n');
            }
        }
        Assert.True(zero <= 10, $"мест с 0 зрителей {zero} из {total} (≤ 10):\n{table}");
    }

    [Fact]
    public void WarmZoneAndMultiplierOnGainsOnly()
    {
        var sim = Make();
        // Зона «тепло» +8/с — ручка плейтеста итерации 2 (playtest/iter-2.md → «Тюнинг»; предел +2…+8), было +5/с.
        Run(sim, 12, 300); // +8/с × 10 с
        Assert.InRange((double)Fixed.FromRaw(sim.State.Runners[3].Viewing.Viewers), 179.9, 180.01);
        Assert.InRange((double)Fixed.FromRaw(sim.State.Runners[0].Viewing.Viewers), 211.9, 212.01); // ×1.4
        var sim2 = Make();
        Run(sim2, 30, 150); // убыль без множителя: у гламура как у ботаника
        Assert.Equal(sim2.State.Runners[3].Viewing.Viewers, sim2.State.Runners[0].Viewing.Viewers);
    }

    [Fact]
    public void CapAtTwoThousand()
    {
        var sim = Make();
        Run(sim, 2, 30 * 200);
        for (int i = 0; i < 4; i++) Assert.Equal(2000, Viewers.ViewersOf(sim.State, i));
    }

    [Fact]
    public void FiveMinutesAtFiveHundredViewersGivesAbout1500Donations()
    {
        var sim = Make();
        // Держим 500 зрителей: зона 20–40 м убывает, поэтому фиксируем значение каждый тик.
        for (int t = 0; t < 300 * 30; t++)
        {
            sim.State.Runners[3].Viewing.Viewers = Fixed.FromInt(500).Raw;
            sim.State.Runners[3].S = sim.State.Bull.S + Fixed.FromInt(4).Raw;
            Viewers.Step(sim);
            sim.State.Tick++;
        }
        Assert.InRange(Viewers.DonationsOf(sim.State, 3), 1495, 1505);
    }

    [Fact]
    public void NoSignalDrainsTenPercentAndNoIncome()
    {
        var sim = Make();
        sim.State.Runners[3].Viewing.Viewers = Fixed.FromInt(1000).Raw;
        sim.State.Runners[3].Toss.NoSignalTicks = 1000;
        Run(sim, 4, 30); // 1 с: ×0.9 с непрерывным начислением по тикам ≈ 905
        double v = (double)Fixed.FromRaw(sim.State.Runners[3].Viewing.Viewers);
        Assert.InRange(v, 900, 910);
        Assert.Equal(0, Viewers.DonationsOf(sim.State, 3));
    }

    [Fact]
    public void ZoneChangeIsRecorded()
    {
        var sim = Make();
        Run(sim, 4, 3);
        Assert.Equal(0, sim.State.Runners[3].Viewing.ZoneChanges); // первая зона — не смена
        Run(sim, 12, 1);
        ref var v = ref sim.State.Runners[3].Viewing;
        Assert.Equal(1, v.ZoneChanges);
        Assert.Equal(ViewerZone.Hot, v.PrevZone);
        Assert.Equal(ViewerZone.Warm, v.Zone);
        Assert.Equal(3, v.LastZoneTick);
    }

    [Fact]
    public void ParseReadsEconomyJson()
    {
        var d = EconomyData.Parse(System.IO.File.ReadAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Game.Core", "Data", "economy.json")));
        var def = EconomyData.Default();
        // Нетронутые плейтестом числа совпадают с Default() ядра.
        Assert.Equal(def.HotRate, d.HotRate);
        Assert.Equal(def.Cap, d.Cap);
        Assert.InRange(d.NoSignalLoss.Raw - def.NoSignalLoss.Raw, -1, 1);
        // Ручки плейтеста итерации 1 (playtest/iter-1.md): холодно 20–50 м −2/с, эфир пустой −6/с.
        Assert.Equal(Fixed.FromInt(50), d.CoolRange);
        Assert.Equal(Fixed.FromInt(-2), d.CoolRate);
        Assert.Equal(Fixed.FromInt(-6), d.ColdRate);
        // Ручки плейтеста итерации 2 (playtest/iter-2.md → «Тюнинг»): тепло +8/с (предел +2…+8), донаты за подброс 20 (предел 0–80).
        Assert.Equal(Fixed.FromInt(8), d.WarmRate);
        Assert.Equal(20, d.TossDonations);
    }

    [Fact]
    public void DeterministicAndCloneEqual()
    {
        ulong Go(Simulation s)
        {
            var inp = TickInputs.Empty;
            for (int t = 0; t < 3000; t++) { inp = inp.With(t % 4, new PlayerInput(1, 1, false)); s.Step(inp); }
            return s.StateHash();
        }
        var a = Make(42); var b = Make(42);
        Assert.Equal(Go(a), Go(b));
        var c = a.Clone();
        Assert.Equal(a.StateHash(), c.StateHash());
        c.Step(TickInputs.Empty); a.Step(TickInputs.Empty);
        Assert.Equal(a.StateHash(), c.StateHash());
    }
}
