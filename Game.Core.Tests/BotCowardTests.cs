using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Math;
using Game.Core.Street;
using Xunit;
using StreetMap = Game.Core.Street.Street;

/// <summary>F-023: мозг бота и характер «трус» (design/F-005.md §2, §3, §5).</summary>
public class BotCowardTests
{
    static readonly GameData Data = GameData.Load();
    static BotData Bots => Data.BotData;
    static Fixed Fx(int n) => Fixed.FromInt(n);

    static IBotBrain Brain(int temperament = 0) => BotBrains.Create(Bots, temperament);

    static BotMemory Mem(int seat = 0, ulong seed = 7)
    {
        BotMemory m = default;
        BotMemory.Init(ref m, seed, seat);
        return m;
    }

    /// <summary>Спокойный вид: прямая шириной 8, бык далеко, никого рядом.</summary>
    static BotView View(int tick = 0, double d = 40, int stamina = 100, int seat = 0)
    {
        return new BotView
        {
            Seat = seat, Tick = tick, S = Fx(100), X = Fx(2), Width = Fx(8), Block = BotBlock.Straight,
            Speed = Fx(7), Stamina = Fx(stamina),
            BullDist = Fixed.FromRaw((long)(d * Fixed.OneRaw)), BullX = Fx(0), Quest = "",
        };
    }

    static RaceConfig Cowards(ulong seed) => new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Bot, 0, 0), new SeatConfig(SeatKind.Bot, 1, 0),
        new SeatConfig(SeatKind.Bot, 2, 0), new SeatConfig(SeatKind.Bot, 3, 0),
    });

    [Fact]
    public void CowardSprintsWhileInvulnerableAfterTossIfStaminaAboveFloor()
    {
        // design/F-005.md §2.5: «Подброшен: ... пока идут 3 с неуязвимости, бот любого характера спринтует от быка,
        // пока выносливость выше его «спринт до выносливости» (трус 5 ...)» (F-073).
        var b = Brain(); var m = Mem(); int p = 0;
        for (int t = 0; t < 30; t++) { var v = View(t, 40, 100); v.InvulnTicks = 80; p = b.Decide(v, ref m).Pace; }
        Assert.Equal(1, p);
        var m2 = Mem();
        for (int t = 0; t < 30; t++) { var v = View(t, 40, (int)Bots.Get(0).SprintUntilStamina); v.InvulnTicks = 80; p = b.Decide(v, ref m2).Pace; }
        Assert.NotEqual(1, p);
    }

    // ---- §5 п.1: трус в сценариях ----

    [Fact]
    public void CowardSprintsAt12RunsAt20AndNeverActs()
    {
        var brain = Brain();
        var m = Mem();
        var near = brain.Decide(View(d: 12, stamina: 50), ref m);
        Assert.Equal(1, near.Pace);
        Assert.False(near.Action);
        m = Mem();
        var far = brain.Decide(View(d: 20, stamina: 50), ref m);
        Assert.Equal(0, far.Pace);
        Assert.False(far.Action);
    }

    [Fact]
    public void CowardNeverJogsAndNeverActsAcrossDistances()
    {
        var brain = Brain();
        var m = Mem();
        for (int t = 0; t < 600; t++)
        {
            double d = 5 + (t * 37 % 60);
            var v = View(t, d, stamina: 10 + (t * 13 % 90));
            var inp = brain.Decide(v, ref m);
            Assert.True(inp.Pace >= 0);
            Assert.False(inp.Action);
        }
    }

    [Fact]
    public void CowardHysteresisAndStaminaFloor()
    {
        var brain = Brain();
        var def = Bots.Get(0);
        var m = Mem();
        double on = (double)(def.SprintBelow - def.Hysteresis);
        Assert.Equal(1, brain.Decide(View(0, on - 0.1), ref m).Pace);
        // между порогами 14..16 спринт держится
        int t = def.TempoPeriod;
        Assert.Equal(1, brain.Decide(View(t, 15.5), ref m).Pace);
        // выше 16 — выключается на ближайшем пересмотре
        t += def.TempoPeriod;
        Assert.Equal(0, brain.Decide(View(t, 16.5), ref m).Pace);
        // а в зоне гистерезиса снизу не включается
        t += def.TempoPeriod;
        Assert.Equal(0, brain.Decide(View(t, 15.5), ref m).Pace);
        // выносливость ≤ порога: бег, пока бык рядом
        var m2 = Mem();
        Assert.Equal(0, brain.Decide(View(0, 8, stamina: (int)def.SprintUntilStamina), ref m2).Pace);
    }

    [Fact]
    public void CowardSprintsOnRushAfterReactionDelayRegardlessOfDistance()
    {
        var brain = Brain();
        var def = Bots.Get(0);
        var m = Mem();
        int firstSprint = -1;
        for (int t = 0; t < 40; t++)
        {
            var v = View(t, d: 40, stamina: 30);
            v.BullRushing = true; v.RushOnMe = true;
            var inp = brain.Decide(v, ref m);
            if (inp.Pace == 1 && firstSprint < 0) firstSprint = t;
        }
        Assert.InRange(firstSprint, def.ReactionMin, def.ReactionMax);
        // трус не уходит с линии от рывка: цель руля — по памяти о блоке
        var m2 = Mem();
        var v2 = View(0, 40);
        v2.X = Fx(2); v2.RushOnMe = true; v2.BullRushing = true; v2.BullX = Fx(2);
        for (int t = 0; t < 40; t++) { v2.Tick = t; Assert.Equal(0, brain.Decide(v2, ref m2).Steer); }
    }

    [Fact]
    public void CowardTakesOnlyCleanRun()
    {
        var def = Bots.Get(0);
        Assert.Equal("coward", def.Id);
        Assert.True(def.Takes("clean_run"));
        foreach (var q in new[] { "stay_hot", "hit_stall", "floor_neighbor", "overtake" }) Assert.False(def.Takes(q));
        Assert.Equal(Fixed.Zero, def.Aggression);
        Assert.Equal(Fixed.One, def.TrustHints);
        Assert.Equal(Fixed.One, def.TrustNeighbors);
    }

    // ---- §2.3: руль ----

    [Fact]
    public void SteersToBlockLaneByDefaultWithDeadZone()
    {
        var brain = Brain();
        var m = Mem();
        var v = View(0);
        v.X = Fx(0) + Fixed.FromFraction(1, 10); // x > 0 → сторона +; цель ширина/4 = 2
        Assert.Equal(1, brain.Decide(v, ref m).Steer);
        v.X = Fx(2) + Fixed.FromFraction(1, 10); // в мёртвой зоне
        Assert.Equal(0, brain.Decide(v, ref m).Steer);
        v.X = Fx(3);
        Assert.Equal(-1, brain.Decide(v, ref m).Steer);
    }

    [Fact]
    public void HintDodgeStartsAfterReactionDelayInAllThreeCharacters()
    {
        for (int temp = 0; temp < 3; temp++)
        {
            var brain = Brain(temp);
            var def = Bots.Get(temp);
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var m = Mem(seat: (int)(seed % 4), seed: seed);
                int first = -1;
                for (int t = 0; t < 30; t++)
                {
                    var v = View(t);
                    v.X = Fx(2); v.Block = BotBlock.Straight;
                    // бот на своей полосе +2 (руль 0); препятствие справа за 60 тиков → цель −2: первый руль = конец реакции
                    if (t == 0) { v.HintCount = 1; v.Hint0 = new BotHint { Side = 1, TicksUntil = 60 }; }
                    var inp = brain.Decide(v, ref m);
                    if (first < 0 && inp.Steer != 0) first = t;
                }
                Assert.InRange(first, 6, 10);
                Assert.InRange(first, def.ReactionMin, def.ReactionMax);
            }
        }
    }

    [Fact]
    public void ReactionDelayIsMeasuredToFirstSteerAwayFromBlockLane()
    {
        // Препятствие справа: цель −2, а память о блоке тянет на +2 (бот на +2: стоит в мёртвой зоне, руль 0).
        var brain = Brain();
        var def = Bots.Get(0);
        var m = Mem();
        int first = -1, steer = 0;
        for (int t = 0; t < 30; t++)
        {
            var v = View(t);
            v.X = Fx(2);
            if (t == 3) { v.HintCount = 1; v.Hint0 = new BotHint { Side = 1, TicksUntil = 60 }; }
            steer = brain.Decide(v, ref m).Steer;
            if (first < 0 && steer != 0) first = t - 3;
        }
        Assert.InRange(first, def.ReactionMin, def.ReactionMax);
        Assert.Equal(-1, steer);
    }

    [Fact]
    public void FullWidthHintDoesNotSteerAndCenterHintPicksOccupiedSide()
    {
        var brain = Brain();
        var m = Mem();
        for (int t = 0; t < 20; t++)
        {
            var v = View(t); v.X = Fx(2);
            if (t == 0) { v.HintCount = 1; v.Hint0 = new BotHint { Side = 0, Full = true, TicksUntil = 60 }; }
            Assert.Equal(0, brain.Decide(v, ref m).Steer);
        }
        var m2 = Mem();
        int steer = 0;
        for (int t = 0; t < 20; t++)
        {
            var v = View(t); v.X = Fx(-1);
            if (t == 0) { v.HintCount = 1; v.Hint0 = new BotHint { Side = 0, TicksUntil = 60 }; }
            steer = brain.Decide(v, ref m2).Steer;
        }
        Assert.Equal(-1, steer); // по центру: ±(4 − 0.8) в сторону, где стоит
    }

    [Fact]
    public void PlanForgottenOnceObstacleIsBehind()
    {
        var brain = Brain();
        var m = Mem();
        var v = View(0); v.X = Fx(2);
        v.HintCount = 1; v.Hint0 = new BotHint { Side = 1, TicksUntil = 30 };
        brain.Decide(v, ref m);
        Assert.Equal(1, m.P0.Used);
        v = View(40); v.X = Fx(2); v.S = Fx(100) + Fx(20); // обогнали ожидаемый s больше чем на 1 м
        brain.Decide(v, ref m);
        Assert.Equal(0, m.P0.Used);
    }

    [Fact]
    public void SeparatesFromNeighborCloserThanOneMeter()
    {
        var brain = Brain();
        var m = Mem();
        var v = View(0); v.X = Fx(2);
        v.Nb1 = new BotNeighbor { Valid = true, DS = Fixed.Zero, DX = Fixed.FromFraction(1, 2), X = Fx(2) + Fixed.FromFraction(1, 2), Speed = Fx(7) };
        Assert.Equal(-1, brain.Decide(v, ref m).Steer); // сосед справа на 0.5 м → отодвигаемся влево до 1.0
    }

    [Fact]
    public void RepeatsNeighborDodgeAfterReactionDelay()
    {
        var brain = Brain();
        var def = Bots.Get(0);
        var m = Mem();
        int steered = -1;
        Fixed nx = Fx(-3);
        for (int t = 0; t < 40; t++)
        {
            var v = View(t); v.X = Fx(2);
            if (t >= 1 && t < 6) nx = nx + Fixed.FromFraction(1, 10); // сосед сзади в 2 м смещается вправо
            v.Nb2 = new BotNeighbor { Valid = true, DS = Fx(-2), DX = nx - v.X, X = nx, Speed = Fx(7) };
            var inp = brain.Decide(v, ref m);
            if (steered < 0 && inp.Steer != 0) steered = t;
        }
        Assert.True(steered >= 1 + def.ReactionMin, "реакция не раньше R после начала смещения: " + steered + " hold " + m.NbHoldUntil + " dir " + m.NbDir + " tgt " + m.NbTargetX);
    }

    [Fact]
    public void AheadNeighborIsRememberedForOneSecondOnly()
    {
        var brain = Brain();
        var m = Mem();
        var v = View(0);
        v.Nb1 = new BotNeighbor { Valid = true, DS = Fixed.FromFraction(3, 2), DX = Fx(1), X = Fx(3), Speed = Fx(7) };
        brain.Decide(v, ref m);
        for (int t = 1; t <= 31; t++)
        {
            brain.Decide(View(t), ref m); // сосед ушёл из кадра вперёд
            Assert.Equal(t <= Bots.AheadMemory, m.AheadMemory(1, t, Bots.AheadMemory, out _, out _));
        }
    }

    // ---- §5 п.2: решение зависит только от наблюдений ----

    [Fact]
    public void DecisionDependsOnlyOnObservations()
    {
        var cfg = new RaceConfig(11, new[]
        {
            new SeatConfig(SeatKind.Bot, 0, 0), new SeatConfig(SeatKind.Human, 1),
            new SeatConfig(SeatKind.Human, 2), new SeatConfig(SeatKind.Human, 3),
        });
        var sim = new Simulation(cfg, Data).Running();
        var alt = StreetGenerator.Build(cfg.Seed, Data.StreetData, StreetPool.Core);
        // Препятствия «другого мира»: те же блоки и ширины, другие места и размеры.
        for (int i = 0; i < alt.Obstacles.Length; i++)
        {
            var o = alt.Obstacles[i];
            alt.Obstacles[i] = new StreetObstacle(o.Def, o.Block, o.S, -o.X, o.SizeAlong, o.SizeAcross, o.Lane, o.Param);
        }
        var brain = Brain();
        var rng = new DetRandom(5);
        var steerIn = TickInputs.Empty;
        for (int t = 0; t < 900; t++)
        {
            var st = sim.State;
            var vA = BotView.Build(st, sim.Street, Bots, 0, null);
            // скрытое для бота: чужая выносливость, чужие зрители/донаты, чужие подсказки/квесты
            var other = sim.Clone();
            for (int s = 1; s < 4; s++)
            {
                other.State.Runners[s].Stamina = rng.Range(0, 100) * Fixed.OneRaw;
                other.State.Runners[s].Exhausted = rng.Range(0, 2);
                other.State.Runners[s].Viewing.Viewers = rng.Range(0, 5000) * Fixed.OneRaw;
                other.State.Runners[s].Viewing.Donations = rng.Range(0, 500) * Fixed.OneRaw;
            }
            var vB = BotView.Build(other.State, alt, Bots, 0, null);
            var mA = st.Bots[0];
            var mB = other.State.Bots[0];
            var a = brain.Decide(vA, ref mA);
            var b = brain.Decide(vB, ref mB);
            Assert.Equal(a, b);
            Assert.Equal(mA.Hash(1), mB.Hash(1));
            steerIn = TickInputs.Empty.With(1, new PlayerInput(((t / 40) & 1) == 0 ? 1 : -1, 0, false));
            sim.Step(steerIn);
        }
    }

    // ---- §5 п.3: таблица чисел 1:1 ----

    [Fact]
    public void BotsJsonHoldsTheTableForAllThreeCharacters()
    {
        Assert.Equal(3, Bots.Temperaments.Length);
        var c = Bots.Get(0); var a = Bots.Get(1); var tr = Bots.Get(2);
        Assert.Equal(new[] { "coward", "adrenaline", "troll" }, new[] { c.Id, a.Id, tr.Id });
        Fixed F(double v) => Game.Core.Data.Json.ParseFixed(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        void Row(BotDef d, double lo, double hi, double below, double hyst, double until, double reserve, int tempo, int action,
            int r0, int r1, double hints, double nb, int wait, double aggr, double hunt, int p0, int p1, bool rush, string[] quests)
        {
            Assert.Equal(F(lo), d.BandMin); Assert.Equal(F(hi), d.BandMax);
            Assert.Equal(F(below), d.SprintBelow); Assert.Equal(F(hyst), d.Hysteresis);
            Assert.Equal(F(until), d.SprintUntilStamina); Assert.Equal(F(reserve), d.ReserveStamina);
            Assert.Equal(tempo, d.TempoPeriod); Assert.Equal(action, d.ActionPeriod);
            Assert.Equal(r0, d.ReactionMin); Assert.Equal(r1, d.ReactionMax);
            Assert.Equal(F(hints), d.TrustHints); Assert.Equal(F(nb), d.TrustNeighbors);
            Assert.Equal(wait, d.WaitNeighbor); Assert.Equal(F(aggr), d.Aggression); Assert.Equal(F(hunt), d.HuntRadius);
            Assert.Equal(p0, d.PushMin); Assert.Equal(p1, d.PushMax); Assert.Equal(rush, d.DodgeRush);
            Assert.Equal(quests, d.Quests);
        }
        Row(c, 15, 30, 15, 1, 5, 0, 15, 0, 8, 10, 1, 1, 0, 0, 0, 0, 0, false, new[] { "clean_run" });
        Row(a, 4, 8, 2.5, 0.5, 10, 40, 6, 15, 6, 8, 1, 0.5, 0, 0.3, 1.5, 8, 20, true, new[] { "stay_hot", "hit_stall", "floor_neighbor", "overtake" });
        Row(tr, 12, 18, 6, 1, 20, 50, 10, 90, 7, 9, 1, 1, 8, 1.0, 3, 8, 20, true, new[] { "floor_neighbor", "overtake" });
        // задержка между подсказкой и первым рулём — 6–10 тиков у всех
        foreach (var d in Bots.Temperaments) { Assert.InRange(d.ReactionMin, 6, 10); Assert.InRange(d.ReactionMax, 6, 10); }
        // общие числа
        Assert.Equal(F(0.15), Bots.DeadZone); Assert.Equal(F(0.5), Bots.WallMargin);
        Assert.Equal(F(1.5), Bots.SideFrame); Assert.Equal(Fx(30), Bots.RearFrame); Assert.Equal(30, Bots.AheadMemory);
    }

    [Fact]
    public void EveryNumberInBotsJsonHasASourceComment()
    {
        string? d = AppContext.BaseDirectory; string? file = null;
        while (d != null)
        {
            string c = Path.Combine(d, "Game.Core", "Data", "bots.json");
            if (File.Exists(c)) { file = c; break; }
            d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        Assert.NotNull(file);
        string text = File.ReadAllText(file!);
        int ti = text.IndexOf("\"tableNote\"", StringComparison.Ordinal), si = text.IndexOf("\"srcNote\"", StringComparison.Ordinal);
        Assert.True(ti > 0 && si > 0);
        string tableNote = text.Substring(ti), srcNote = text.Substring(si, ti - si);
        foreach (var k in new[] { "band", "sprintBelow", "hysteresis", "sprintUntilStamina", "reserveStamina", "tempoPeriod", "actionPeriod",
            "reaction", "trustHints", "trustNeighbors", "waitNeighbor", "aggression", "huntRadius", "pushWindow", "dodgeRush", "quests" })
            Assert.Contains(k, tableNote);
        foreach (var k in new[] { "deadZone", "wallMargin", "dodgeDiv", "centerDodgeMargin", "sideFrame", "rearFrame", "aheadMemory",
            "neighborReact", "neighborSeparation", "rushSidestep", "passMargin", "marketAxis", "plazaExtra", "pondEdge", "laneSwitch", "shiftEps", "neighborHold" })
            Assert.Contains(k, srcNote);
    }

    // ---- гонки: уворот, толчки, детерминизм ----

    /// <summary>Тестовая заглушка F-021: подсказывает реальные препятствия за 60 тиков, без шума.</summary>
    sealed class TruthHints : IBotHintSource
    {
        readonly HashSet<long> _told = new HashSet<long>();
        public int Told;
        public int Collect(RaceState st, StreetMap street, int seat, Span<BotHint> dst)
        {
            Fixed s = Fixed.FromRaw(st.Runners[seat].S), v = Fixed.FromRaw(st.Runners[seat].Speed);
            if (v <= Fixed.Zero) return 0;
            int n = 0;
            for (int i = 0; i < street.Obstacles.Length && n < dst.Length; i++)
            {
                var o = street.Obstacles[i];
                if (o.S <= s) continue;
                int ticks = ((o.S - s) / v * Fx(Simulation.TicksPerSecond)).ToInt();
                if (ticks > 60) break;
                long key = (long)seat * 1_000_000 + i;
                if (!_told.Add(key)) continue;
                var h = new BotHint { TicksUntil = Math.Max(1, ticks) };
                switch (o.Lane) { case Lane.L: h.Side = -1; break; case Lane.R: h.Side = 1; break; case Lane.F: h.Full = true; break; default: h.Side = 0; break; }
                dst[n++] = h; Told++;
            }
            return n;
        }
    }

    static void RunRace(Simulation sim, int maxTicks = 14000)
    {
        for (int t = 0; t < maxTicks && sim.State.Phase != Game.Core.Race.RacePhase.Over; t++) sim.Step(TickInputs.Empty);
    }

    [Fact]
    public void CowardsDodgeMostHintedObstaclesWithNeighborsAround()
    {
        int told = 0, hits = 0, hitsNoHints = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var src = new TruthHints();
            var sim = new Simulation(Cowards(seed), Data) { BotHints = src };
            RunRace(sim);
            for (int i = 0; i < 4; i++) hits += sim.State.Runners[i].Collision.Count;
            told += src.Told;
            var blind = new Simulation(Cowards(seed), Data) { BotHints = null };
            RunRace(blind);
            for (int i = 0; i < 4; i++) hitsNoHints += blind.State.Runners[i].Collision.Count;
        }
        double rate = 1.0 - (double)hits / told;
        Assert.True(rate >= 0.6, $"уворот по подсказкам {rate:P0}: подсказано {told}, столкновений {hits} (вслепую {hitsNoHints})");
        Assert.True(hits < hitsNoHints, $"подсказки не помогают: {hits} против {hitsNoHints}");
    }

    [Fact]
    public void CowardsNeverShoveOrTrip()
    {
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var sim = new Simulation(Cowards(seed), Data);
            for (int t = 0; t < 14000 && sim.State.Phase != Game.Core.Race.RacePhase.Over; t++)
            {
                sim.Step(TickInputs.Empty.With(0, new PlayerInput(0, 0, true))); // внешний вход бота игнорируется
                for (int i = 0; i < 4; i++)
                {
                    Assert.Equal(0, sim.State.Runners[i].Action.Shoved);
                    Assert.Equal(0, sim.State.Runners[i].Action.Tripped);
                }
            }
        }
    }

    [Fact]
    public void RaceOfFourCowardsIsDeterministicAndCloneConsistent()
    {
        var a = new Simulation(Cowards(21), Data);
        var b = new Simulation(Cowards(21), Data);
        for (int t = 0; t < 3000; t++)
        {
            a.Step(TickInputs.Empty); b.Step(TickInputs.Empty);
            Assert.Equal(a.StateHash(), b.StateHash());
        }
        var c = a.Clone();
        for (int t = 0; t < 400; t++)
        {
            a.Step(TickInputs.Empty); c.Step(TickInputs.Empty);
            Assert.Equal(a.StateHash(), c.StateHash());
        }
        // и бот действительно бежит: все прошли заметный путь
        for (int i = 0; i < 4; i++) Assert.True(a.State.Runners[i].S > Fx(100).Raw);
    }

    [Fact]
    public void BotSeatWithoutBotsJsonStaysIdle()
    {
        // Без bots.json: BotData.None() — сборка вручную, из файлов такой набор не получить.
        var bare = new GameData(Data.RunnerData, Data.BullData, Data.StreetData, Data.CharacterData, Data.ActionData,
            Data.EconomyData, Game.Core.Bots.BotData.None(), Data.HintData);
        var a = new Simulation(Cowards(3), bare).Running();
        var b = new Simulation(Cowards(3), bare).Running();
        for (int t = 0; t < 60; t++)
        {
            a.Step(TickInputs.Empty.With(0, new PlayerInput(1, 1, true)));
            b.Step(TickInputs.Empty);
            Assert.Equal(a.StateHash(), b.StateHash()); // внешний вход бота игнорируется
        }
    }
}
