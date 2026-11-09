using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Events;
using Game.Core.Hints;
using Game.Core.Math;
using Game.Core.Runners;
using Game.Core.Street;
using Xunit;
using Xunit.Abstractions;

/// <summary>F-021: подсказки чата (design/overview.md → «Подсказки чата»).</summary>
public class HintTests
{
    static readonly GameData Data = GameData.Load();
    readonly ITestOutputHelper _out;
    public HintTests(ITestOutputHelper o) { _out = o; }

    static int NerdId()
    {
        for (int i = 0; i < Data.CharacterData.Characters.Length; i++)
            if (Data.CharacterData.Characters[i].Perk == Perk.NerdModerator) return i;
        throw new InvalidOperationException("нет ботаника");
    }

    static RaceConfig Humans(ulong seed, int character = 0) => new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Human, character), new SeatConfig(SeatKind.Human, character),
        new SeatConfig(SeatKind.Human, character), new SeatConfig(SeatKind.Human, character),
    });

    static RaceConfig NerdBots(ulong seed, int character) => new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Bot, character, 0), new SeatConfig(SeatKind.Bot, character, 0),
        new SeatConfig(SeatKind.Bot, character, 0), new SeatConfig(SeatKind.Bot, character, 0),
    });

    static RaceConfig Cowards(ulong seed) => new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Bot, 0, 0), new SeatConfig(SeatKind.Bot, 1, 0),
        new SeatConfig(SeatKind.Bot, 2, 0), new SeatConfig(SeatKind.Bot, 3, 0),
    });

    // Первое препятствие со стан/замедлением, не на всю ширину, достаточно далеко от старта.
    static int PickObstacle(Simulation sim)
    {
        var obs = sim.Street.Obstacles;
        for (int i = 0; i < obs.Length; i++)
            if (obs[i].S > Fixed.FromInt(100) && !obs[i].Def.FullWidth && (obs[i].Def.Effect == "stun" || obs[i].Def.Effect == "slow")
                && obs[i].Def.MotionKind == "none")
                return i;
        throw new InvalidOperationException("нет препятствия");
    }

    /// <summary>Бегун 0 на ticksBefore/30 с до препятствия i при 7 м/с, на его трети; бегун 1 — впереди на leadDist м.</summary>
    static void Place(Simulation sim, int i, int ticksBefore, double leadDist, int mover = 0, int leader = 1)
    {
        var o = sim.Street.Obstacles[i];
        ref RunnerState r = ref sim.State.Runners[mover];
        r.S = (o.S - Fixed.FromFraction(7 * ticksBefore, 30)).Raw;
        r.X = o.X.Raw; r.Speed = Fixed.FromInt(7).Raw;
        sim.State.Runners[leader].S = (Fixed.FromRaw(r.S) + Fixed.FromRaw((long)(leadDist * Fixed.OneRaw))).Raw;
        sim.State.Runners[leader].Speed = Fixed.FromInt(7).Raw;
    }

    static List<RaceEvent> HintsOf(Simulation sim, int seat)
    {
        var l = new List<RaceEvent>();
        for (int k = 0; k < sim.Events.Count; k++)
            if (sim.Events[k].Type == RaceEventType.Hint && sim.Events[k].Seat == seat) l.Add(sim.Events[k]);
        return l;
    }

    [Fact]
    public void HintComesExactly60TicksBeforeObstacleAtRunSpeed()
    {
        // без шума (ботаник), чтобы подсказка не потерялась
        var sim = new Simulation(Humans(3, NerdId()), Data);
        sim.State.Tick = 5;
        int i = PickObstacle(sim);
        int fired = -1, count = 0;
        for (int t = 75; t >= 40; t--)
        {
            Place(sim, i, t, 26);
            sim.Events.Clear();
            ChatHints.Step(sim);
            foreach (var e in HintsOf(sim, 0)) { fired = t; count++; Assert.Equal(60, ChatHints.TicksOf(e.Param1)); }
        }
        Assert.Equal(1, count);
        Assert.Equal(60, fired);
        // 7 м/с × 2.0 с = 14 м
        Assert.Equal(14.0, 7.0 * (double)Data.HintData.LeadSec);
    }

    [Fact]
    public void EventCarriesObstacleTypeAndSide()
    {
        var sim = new Simulation(Humans(3, NerdId()), Data);
        int i = PickObstacle(sim);
        var o = sim.Street.Obstacles[i];
        Place(sim, i, 60, 26);
        sim.Events.Clear();
        ChatHints.Step(sim);
        var h = HintsOf(sim, 0);
        Assert.Single(h);
        Assert.Equal((int)Collisions.KindOf(o.Def.Id), ChatHints.TypeOf(h[0].Param1));
        double w = (double)sim.Street.WidthAt(o.S);
        double ox = (double)o.X;
        int expect = ox < -w / 6 ? -1 : ox > w / 6 ? 1 : 0;
        Assert.Equal(expect, h[0].Param2);
    }

    [Fact]
    public void LeaderGetsNoHints_AndNobodyWithoutRunnerAheadInRange()
    {
        var sim = new Simulation(Humans(3, NerdId()), Data);
        int i = PickObstacle(sim);
        // 0 впереди всех: у него нет бегуна впереди
        Place(sim, i, 60, 26);
        sim.State.Runners[1].S = sim.State.Runners[0].S - Fixed.FromInt(20).Raw;
        sim.Events.Clear(); ChatHints.Step(sim);
        Assert.Empty(HintsOf(sim, 0));

        // бегун впереди ближе 10 м и дальше 45 м — не источник
        foreach (double d in new[] { 9.0, 46.0 })
        {
            var s2 = new Simulation(Humans(3, NerdId()), Data);
            Place(s2, i, 60, d);
            s2.Events.Clear(); ChatHints.Step(s2);
            Assert.Empty(HintsOf(s2, 0));
        }
        // препятствие впереди бегуна-источника (не в его кадре) — не подсказывается
        var s3 = new Simulation(Humans(3, NerdId()), Data);
        Place(s3, i, 60, 12); // 12 м < 14 м до препятствия: препятствие дальше лидера
        s3.Events.Clear(); ChatHints.Step(s3);
        Assert.Empty(HintsOf(s3, 0));
        // а в диапазоне — приходит
        var s4 = new Simulation(Humans(3, NerdId()), Data);
        Place(s4, i, 60, 26);
        s4.Events.Clear(); ChatHints.Step(s4);
        Assert.Single(HintsOf(s4, 0));
    }

    [Fact]
    public void OtherThirdOfStreetGetsNoHint()
    {
        var sim = new Simulation(Humans(3, NerdId()), Data);
        int i = PickObstacle(sim);
        var o = sim.Street.Obstacles[i];
        Place(sim, i, 60, 26);
        double w = (double)sim.Street.WidthAt(o.S);
        sim.State.Runners[0].X = Fixed.FromRaw((long)(((double)o.X > 0 ? -w / 2 + 0.3 : w / 2 - 0.3) * Fixed.OneRaw)).Raw;
        sim.Events.Clear(); ChatHints.Step(sim);
        Assert.Empty(HintsOf(sim, 0));
    }

    [Fact]
    public void OneHintPerObstaclePerRunner()
    {
        var sim = new Simulation(Humans(3, NerdId()), Data);
        int i = PickObstacle(sim);
        Place(sim, i, 60, 26);
        int total = 0;
        for (int k = 0; k < 5; k++) { sim.Events.Clear(); ChatHints.Step(sim); total += HintsOf(sim, 0).Count; }
        Assert.Equal(1, total);
    }

    [Fact]
    public void NoiseIs15PercentWrongSideAnd10PercentDropped()
    {
        var rng = new DetRandom(12345, 3);
        int dropped = 0, wrong = 0, n = 10000;
        for (int k = 0; k < n; k++)
        {
            int side = 1;
            if (ChatHints.Noise(Data.HintData, ref rng, false, false, ref side)) dropped++;
            else if (side != 1) wrong++;
        }
        // доли от всех подсказок: не пришло 10%, а «соврало» 15% (независимо) — из пришедших 90% врёт 13.5% всех
        double dropRate = dropped / (double)n, wrongRate = wrong / (double)(n - dropped);
        _out.WriteLine($"drop={dropRate:P2} wrongOfDelivered={wrongRate:P2}");
        Assert.InRange(dropRate, 0.08, 0.12);
        Assert.InRange(wrongRate, 0.13, 0.17);
        Assert.Equal(0.10, (double)Data.HintData.DropChance, 3);
        Assert.Equal(0.15, (double)Data.HintData.WrongSideChance, 3);
    }

    [Fact]
    public void NerdHasNoNoise()
    {
        var rng = new DetRandom(1, 1);
        var before = rng;
        for (int k = 0; k < 1000; k++)
        {
            int side = -1;
            Assert.False(ChatHints.Noise(Data.HintData, ref rng, true, false, ref side));
            Assert.Equal(-1, side);
        }
        Assert.Equal(before.State, rng.State);

        // в забеге: у ботаников ГСЧ шума не трогается, у остальных — продвигается; подсказки приходят
        int nerd = NerdId();
        var nerds = new Simulation(NerdBots(7, nerd), Data);
        var others = new Simulation(NerdBots(7, 0), Data);
        ulong[] init = new ulong[4];
        for (int s = 0; s < 4; s++) init[s] = nerds.State.Runners[s].Hints.Rng.State;
        int nerdHints = 0, otherHints = 0;
        for (int t = 0; t < 9000; t++) // F-072: рывок 2 с без доворота меняет ход забега — подсказок за первые 4000 тиков у ботаников может не быть
        {
            nerds.Step(TickInputs.Empty); others.Step(TickInputs.Empty);
            nerdHints += nerds.Events.CountOf(RaceEventType.Hint); otherHints += others.Events.CountOf(RaceEventType.Hint);
        }
        _out.WriteLine($"nerdHints={nerdHints} otherHints={otherHints}");
        Assert.True(nerdHints > 0 && otherHints > 0);
        for (int s = 0; s < 4; s++) Assert.Equal(init[s], nerds.State.Runners[s].Hints.Rng.State);
        bool moved = false;
        for (int s = 0; s < 4; s++) moved |= others.State.Runners[s].Hints.Rng.State != init[s];
        Assert.True(moved);
    }

    [Fact]
    public void BotsGetHintsByDefault_AndHintsAreHashedAndDeterministic()
    {
        var a = new Simulation(Cowards(21), Data);
        var b = new Simulation(Cowards(21), Data);
        Assert.NotNull(a.BotHints);
        int hints = 0;
        for (int t = 0; t < 4000; t++)
        {
            a.Step(TickInputs.Empty); b.Step(TickInputs.Empty);
            Assert.Equal(a.StateHash(), b.StateHash());
            hints += a.Events.CountOf(RaceEventType.Hint);
        }
        var c = a.Clone();
        for (int t = 0; t < 300; t++)
        {
            a.Step(TickInputs.Empty); c.Step(TickInputs.Empty);
            Assert.Equal(a.StateHash(), c.StateHash());
        }
        Assert.True(hints > 0, "за 4000 тиков ни одной подсказки");
    }

    [Fact]
    public void CowardsWithNeighborAheadCollideLessWithChatHints_AndDodgeAtLeast60Percent()
    {
        // Подсказанное препятствие = событие Hint; «столкнулся» = событие Collision того же места и типа в пределах 75 тиков после него.
        // Подсказки модуль считает всегда, поэтому метрика одинакова для ботов с подсказками и без (BotHints = null).
        int told = 0, hits = 0, toldBlind = 0, hitsBlind = 0;
        var table = new System.Text.StringBuilder();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            Measure(new Simulation(Cowards(seed), Data), out int h, out int c);
            Measure(new Simulation(Cowards(seed), Data) { BotHints = null }, out int hb, out int cb);
            told += h; hits += c; toldBlind += hb; hitsBlind += cb;
            table.AppendLine($"seed {seed}: hinted={h} collided={c} | blind hinted={hb} collided={cb}");
        }
        double rate = 1.0 - (double)hits / told, rateBlind = 1.0 - (double)hitsBlind / toldBlind;
        table.AppendLine($"dodge with hints {rate:P0}, blind {rateBlind:P0}");
        _out.WriteLine(table.ToString());
        Assert.True(rate > rateBlind, "с подсказками не реже сталкиваются\n" + table);
        Assert.True(rate >= 0.6, "уворот < 60%\n" + table);
    }

    static void Measure(Simulation sim, out int hinted, out int collided)
    {
        hinted = 0; collided = 0;
        var pending = new List<(int seat, int kind, int tick)>();
        for (int t = 0; t < 14000 && sim.State.Phase != Game.Core.Race.RacePhase.Over; t++)
        {
            sim.Step(TickInputs.Empty);
            for (int k = 0; k < sim.Events.Count; k++)
            {
                var e = sim.Events[k];
                if (e.Type == RaceEventType.Hint) { hinted++; pending.Add((e.Seat, ChatHints.TypeOf(e.Param1), e.Tick)); }
                else if (e.Type == RaceEventType.Collision)
                {
                    int m = pending.FindIndex(p => p.seat == e.Seat && p.kind == e.Param1 && e.Tick - p.tick <= 75);
                    if (m >= 0) { pending.RemoveAt(m); collided++; }
                }
            }
        }
    }
}
