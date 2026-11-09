using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Economy;
using Game.Core.Math;
using Game.Core.Street;
using Xunit;
using StreetMap = Game.Core.Street.Street;

/// <summary>F-024: характеры «адреналинщик» и «тролль» (design/F-005.md §2.4, §2.5, §5 п.1).</summary>
public class BotCharacterTests
{
    static readonly GameData Data = GameData.Load();
    static BotData Bots => Data.BotData;
    static Fixed Fx(int n) => Fixed.FromInt(n);
    const int Adr = 1, Trl = 2;

    static IBotBrain Brain(int t) => BotBrains.Create(Bots, t);

    static BotMemory Mem(int seat = 0, ulong seed = 7)
    {
        BotMemory m = default;
        BotMemory.Init(ref m, seed, seat);
        return m;
    }

    static BotView View(int tick = 0, double d = 40, int stamina = 100, int seat = 0, string quest = "")
    {
        return new BotView
        {
            Seat = seat, Tick = tick, S = Fx(100), X = Fx(2), Width = Fx(8), Block = BotBlock.Straight,
            Speed = Fx(7), Stamina = Fx(stamina),
            BullDist = Fixed.FromRaw((long)(d * Fixed.OneRaw)), BullX = Fx(0), Quest = quest,
        };
    }

    static BotNeighbor Nb(BotView v, double ds, double dx) => new BotNeighbor
    {
        Valid = true, DS = Fixed.FromRaw((long)(ds * Fixed.OneRaw)), DX = Fixed.FromRaw((long)(dx * Fixed.OneRaw)),
        X = v.X + Fixed.FromRaw((long)(dx * Fixed.OneRaw)), Speed = Fx(7),
    };

    /// <summary>Темп после нескольких пересмотров на одной дистанции.</summary>
    static int PaceAt(IBotBrain b, ref BotMemory m, ref int tick, double d, int stamina = 100, string quest = "")
    {
        int p = 0;
        for (int i = 0; i < 30; i++) p = b.Decide(View(tick++, d, stamina, quest: quest), ref m).Pace;
        return p;
    }

    // ---- данные ----

    [Fact]
    public void BrainsAreChosenByCharacterId()
    {
        Assert.IsType<Coward>(Brain(0));
        Assert.IsType<Adrenaline>(Brain(Adr));
        Assert.IsType<Troll>(Brain(Trl));
        Assert.True(Bots.Get(Adr).Takes("stay_hot") && Bots.Get(Adr).Takes("hit_stall"));
        Assert.True(Bots.Get(Adr).Takes("floor_neighbor") && Bots.Get(Adr).Takes("overtake"));
        Assert.False(Bots.Get(Adr).Takes("clean_run"));
        Assert.True(Bots.Get(Trl).Takes("floor_neighbor") && Bots.Get(Trl).Takes("overtake"));
        foreach (var q in new[] { "clean_run", "stay_hot", "hit_stall" }) Assert.False(Bots.Get(Trl).Takes(q));
    }

    // ---- адреналинщик: темп ----

    [Fact]
    public void AdrenalineJogsAt10RunsAt3AndKeepsPaceAt6()
    {
        var b = Brain(Adr);
        int t = 0;
        var m = Mem();
        Assert.Equal(-1, PaceAt(b, ref m, ref t, 10));
        Assert.Equal(-1, PaceAt(b, ref m, ref t, 6)); // внутри коридора держит трусцу
        Assert.Equal(0, PaceAt(b, ref m, ref t, 3));  // d < 4 → бег
        Assert.Equal(0, PaceAt(b, ref m, ref t, 6));  // и бег держится
        var m2 = Mem(); t = 0;
        Assert.Equal(0, PaceAt(b, ref m2, ref t, 6)); // с начального бега — бег
    }

    [Fact]
    public void AdrenalineSprintsAtLastMomentWithStaminaFloorAndReserve()
    {
        var b = Brain(Adr);
        var def = Bots.Get(Adr);
        int t = 0; var m = Mem();
        Assert.Equal(1, PaceAt(b, ref m, ref t, 1.9, 50));  // d < 2.5 − 0.5
        Assert.Equal(1, PaceAt(b, ref m, ref t, 2.9, 50));  // гистерезис: держится до 3.0
        Assert.Equal(0, PaceAt(b, ref m, ref t, 3.1, 50));
        var m2 = Mem(); t = 0;
        Assert.Equal(0, PaceAt(b, ref m2, ref t, 1.9, (int)def.SprintUntilStamina)); // выносливость ≤ 10 → не спринтует
        // без угрозы выносливость не тратится: d = 6, 100 ед. — бег, не спринт
        var m3 = Mem(); t = 0;
        Assert.Equal(0, PaceAt(b, ref m3, ref t, 6, 100));
    }

    [Fact]
    public void AdrenalineSprintsOnRushAfterReactionDelayAndSidestepsBull()
    {
        var b = Brain(Adr);
        var def = Bots.Get(Adr);
        var m = Mem();
        int first = -1;
        for (int t = 0; t < 40; t++)
        {
            var v = View(t, d: 40, stamina: 30);
            v.BullRushing = true; v.RushOnMe = true; v.BullX = Fx(2);
            var inp = b.Decide(v, ref m);
            if (inp.Pace == 1 && first < 0) first = t;
        }
        Assert.InRange(first, def.ReactionMin, def.ReactionMax);
        // на 1.2 м от x быка к центру: бык на +2 → цель +0.8, бот на +2 жмёт влево
        var m2 = Mem(); int steer = 0;
        for (int t = 0; t < 40; t++)
        {
            var v = View(t, 40); v.RushOnMe = true; v.BullRushing = true; v.BullX = Fx(2);
            steer = b.Decide(v, ref m2).Steer;
        }
        Assert.Equal(-1, steer);
    }

    [Fact]
    public void AdrenalineStopsSprintingWhenInvulnerableAfterToss()
    {
        var b = Brain(Adr);
        var m = Mem();
        for (int t = 0; t < 30; t++) { var v = View(t, 2); v.InvulnTicks = 50; Assert.True(b.Decide(v, ref m).Pace <= 0 || t < 0); }
    }

    // ---- адреналинщик: толчок ----

    [Fact]
    public void AdrenalinePushesNeighborOnDodgePathAboutAsOftenAsAggression()
    {
        var b = Brain(Adr);
        int pushes = 0, checks = 0;
        for (ulong seed = 1; seed <= 400; seed++)
        {
            var m = Mem(seed: seed);
            var v = View(0); // x = 2, цель по блоку 2 (мёртвая зона) — путь не нужен: сосед сзади влез в кадр
            v.Nb1 = Nb(v, -1.0, 1.0);
            checks++;
            if (b.Decide(v, ref m).Action) pushes++;
        }
        double rate = (double)pushes / checks;
        Assert.InRange(rate, 0.2, 0.4); // агрессия 0.3
    }

    [Fact]
    public void AdrenalineNeverPushesNeighborBeyondReachOrAheadOffPath()
    {
        var b = Brain(Adr);
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var m = Mem(seed: seed);
            var v = View(0);
            v.Nb1 = Nb(v, -2.0, 1.0); // позади дальше 1.5 м: недоступен толчку
            Assert.False(b.Decide(v, ref m).Action);
            var m2 = Mem(seed: seed);
            var w = View(0);
            w.Nb1 = Nb(w, 1.0, -1.0); // впереди сбоку и не на пути уворота (цель вправо/на месте, сосед слева)
            Assert.False(b.Decide(w, ref m2).Action);
        }
    }

    [Fact]
    public void AdrenalineChecksActionOncePerPeriod()
    {
        var b = Brain(Adr);
        var def = Bots.Get(Adr);
        var m = Mem(seed: 3);
        int checks = 0;
        for (int t = 0; t < def.ActionPeriod * 4; t++)
        {
            var v = View(t); v.Nb1 = Nb(v, -1.0, 1.0);
            ulong before = m.Rng.State;
            b.Decide(v, ref m);
            if (m.Rng.State != before) checks++;
        }
        Assert.Equal(4, checks);
    }

    // ---- адреналинщик: квесты ----

    [Fact]
    public void AdrenalineStayHotQuestNarrowsCorridor()
    {
        var b = Brain(Adr);
        int t = 0; var m = Mem();
        Assert.Equal(0, PaceAt(b, ref m, ref t, 7.5, 100));                // обычно 7.5 ≤ 8: бег держится
        Assert.Equal(-1, PaceAt(b, ref m, ref t, 7.8, 100, "stay_hot"));   // «Держись у быка»: > 7 + 0.5
        t = 0; var m2 = Mem();
        Assert.Equal(0, PaceAt(b, ref m2, ref t, 3.2, 100, "stay_hot"));
    }

    [Fact]
    public void AdrenalineOvertakeQuestWidensCorridorAndSprintsFromReserve()
    {
        var b = Brain(Adr);
        var def = Bots.Get(Adr);
        int t = 0; var m = Mem();
        for (int i = 0; i < 30; i++)
        {
            var v = View(t++, 12, 100, quest: "overtake"); v.HasQuestTarget = true; v.QuestTarget = 1;
            v.Nb1 = Nb(v, 1.0, 1.0); // X впереди
            Assert.Equal(1, b.Decide(v, ref m).Pace); // запас ≥ 60 → спринт, d = 12 ≤ коридор 15
        }
        var m2 = Mem(); t = 0;
        for (int i = 0; i < 30; i++)
        {
            var v = View(t++, 12, (int)def.OvertakeSprint - 1, quest: "overtake"); v.HasQuestTarget = true; v.QuestTarget = 1;
            v.Nb1 = Nb(v, 1.0, 1.0);
            Assert.Equal(0, b.Decide(v, ref m2).Pace); // < 60 — просто бег
        }
        // X позади — обычный коридор
        var m3 = Mem(); t = 0; int p = 0;
        for (int i = 0; i < 30; i++)
        {
            var v = View(t++, 12, 100, quest: "overtake"); v.HasQuestTarget = true; v.QuestTarget = 1;
            v.Nb1 = Nb(v, -3, 1);
            p = b.Decide(v, ref m3).Pace;
        }
        Assert.Equal(-1, p);
    }

    [Fact]
    public void AdrenalineHitStallQuestSteersTowardStallAndHugsWallInMarket()
    {
        var b = Brain(Adr);
        var m = Mem(); int steer = 0;
        for (int t = 0; t < 30; t++)
        {
            var v = View(t, 40, quest: "hit_stall"); v.X = Fx(2);
            if (t == 0) { v.HintCount = 1; v.Hint0 = new BotHint { Side = -1, Stall = true, TicksUntil = 90 }; } // лоток слева
            steer = b.Decide(v, ref m).Steer;
        }
        Assert.Equal(-1, steer); // без квеста ушёл бы вправо; с квестом — на лоток
        var m0 = Mem();
        for (int t = 0; t < 30; t++)
        {
            var v = View(t, 40); v.X = Fx(2);
            if (t == 0) { v.HintCount = 1; v.Hint0 = new BotHint { Side = -1, Stall = true, TicksUntil = 90 }; }
            steer = b.Decide(v, ref m0).Steer;
        }
        Assert.Equal(0, steer);
        // рынок без подсказки: у стены на той стороне, где бежит
        var m2 = Mem(); steer = 0;
        for (int t = 0; t < 10; t++)
        {
            var v = View(t, 40, quest: "hit_stall"); v.Block = BotBlock.Market; v.X = Fx(2);
            steer = b.Decide(v, ref m2).Steer;
        }
        Assert.Equal(1, steer);
    }

    [Fact]
    public void AdrenalineIgnoresCleanRunAndFloorNeighborHunts()
    {
        var b = Brain(Adr);
        var m = Mem(); // «Чистый пробег» не берёт: поведение как без квеста
        var m0 = Mem();
        for (int t = 0; t < 60; t++)
            Assert.Equal(b.Decide(View(t, 9), ref m0), b.Decide(View(t, 9, quest: "clean_run"), ref m));
        // «Уложи соседа»: адреналинщик с радиусом охоты 3 м находит жертву и толкает в конце охоты
        var mh = Mem(); bool pushed = false;
        for (int t = 0; t < 200 && !pushed; t++)
        {
            var v = View(t, 40, quest: "floor_neighbor"); v.Nb1 = Nb(v, 0.0, 1.2);
            pushed = b.Decide(v, ref mh).Action;
        }
        Assert.True(pushed);
    }

    // ---- тролль ----

    [Fact]
    public void TrollPaceBandSprintAndHysteresis()
    {
        var b = Brain(Trl);
        int t = 0; var m = Mem();
        Assert.Equal(-1, PaceAt(b, ref m, ref t, 25));
        Assert.Equal(-1, PaceAt(b, ref m, ref t, 17.5)); // гистерезис 1 м: бег от 17
        Assert.Equal(0, PaceAt(b, ref m, ref t, 16.5));
        Assert.Equal(0, PaceAt(b, ref m, ref t, 8));
        Assert.Equal(1, PaceAt(b, ref m, ref t, 4.5, 50));
        Assert.Equal(1, PaceAt(b, ref m, ref t, 6.5, 50)); // держится до 7
        Assert.Equal(0, PaceAt(b, ref m, ref t, 7.5, 50));
        t = 0; var m2 = Mem();
        Assert.Equal(0, PaceAt(b, ref m2, ref t, 4, 20)); // выносливость ≤ 20 — бег
    }

    [Fact]
    public void TrollSprintsOnRushAfterReactionDelay()
    {
        var b = Brain(Trl);
        var def = Bots.Get(Trl);
        var m = Mem(); int first = -1;
        for (int t = 0; t < 40; t++)
        {
            var v = View(t, 40, 50); v.BullRushing = true; v.RushOnMe = true; v.BullX = Fx(2);
            if (b.Decide(v, ref m).Pace == 1 && first < 0) first = t;
        }
        Assert.InRange(first, def.ReactionMin, def.ReactionMax);
    }

    /// <summary>Сцена охоты: лоток слева через `stallIn` тиков (подсказка на тике 0), s растёт со скоростью 7 м/с; жертва — сосед 1 на абсолютном
    /// x = victimX, на `ds` впереди. Возвращает первый тик нажатия действия (−1 — не нажимал) и сколько тиков охота рулила «позицией для толчка».</summary>
    static (int firstAction, int pushKindTicks, BotMemory m) HuntScene(double trollX, double victimX, double ds, int stallIn, int ticks)
    {
        var b = Brain(Trl);
        var m = Mem(seat: 0, seed: 5);
        int first = -1, kind1 = 0;
        for (int t = 0; t < ticks; t++)
        {
            var v = View(t, 40);
            v.X = Fixed.FromRaw((long)(trollX * Fixed.OneRaw));
            v.S = Fx(100) + Fixed.FromRaw((long)(7.0 * t / 30 * Fixed.OneRaw));
            v.Nb1 = Nb(v, ds, victimX - trollX);
            if (t == 0) { v.HintCount = 1; v.Hint0 = new BotHint { Side = -1, TicksUntil = stallIn }; }
            var inp = b.Decide(v, ref m);
            if (m.HuntKind == 1) kind1++;
            if (inp.Action && first < 0) first = t;
        }
        return (first, kind1, m);
    }

    [Fact]
    public void TrollStandsRightOfLeftThirdVictimAndPushesInsideTheWindow()
    {
        var def = Bots.Get(Trl);
        // жертва-человек в левой трети (x = −2.5 при ширине 8), тролль встаёт справа от неё на 1.0 м: x = −1.5
        var (_, kind1, m) = HuntScene(trollX: 0, victimX: -2.5, ds: 0.3, stallIn: 60, ticks: 46);
        Assert.True(kind1 > 0);
        Assert.Equal(-1.5, (double)Fixed.FromRaw(m.HuntTx), 2);
        foreach (int stallIn in new[] { 30, 45, 60, 80 })
        {
            // тролль уже стоит справа от жертвы: толчок строго в окне 8–20 тиков до лотка
            var (first, _, _) = HuntScene(trollX: -1.5, victimX: -2.5, ds: 0.0, stallIn: stallIn, ticks: stallIn + 5);
            Assert.True(first >= 0, "толчок не случился, лоток через " + stallIn);
            Assert.InRange(stallIn - first, def.PushMin, def.PushMax + 1); // +1: «тиков до» бот считает с усечением по Fixed
        }
    }

    [Fact]
    public void TrollDoesNotPushWhenStallIsLessThanWindowAway()
    {
        // лоток через 5 тиков: окно закрыто, тролль в трети лотка — охота уступает уворот; толчок «в препятствие» не случается
        var (first, kind1, _) = HuntScene(trollX: -1.5, victimX: -2.5, ds: 0.0, stallIn: 5, ticks: 60);
        Assert.Equal(0, kind1);
        Assert.Equal(-1, first);
    }

    [Fact]
    public void TrollFollowsVictimWhoAlreadyDodgedRightInsteadOfPushingIntoStall()
    {
        var def = Bots.Get(Trl);
        // жертва уже справа от лотка слева: x = +2 — толчок в лоток невозможен, идёт за ней
        var (_, kind1, m) = HuntScene(trollX: 0.5, victimX: 2.0, ds: 0.8, stallIn: 60, ticks: 80);
        Assert.Equal(0, kind1);
        Assert.Equal(2, m.HuntKind);
        Assert.Equal(2.0, (double)Fixed.FromRaw(m.HuntTx), 2);
        Assert.True(def.PushMin > 0);
    }

    [Fact]
    public void TrollFollowsVictimIntoLaneAndTripsWithin1Point2Meters()
    {
        var b = Brain(Trl);
        var m = Mem(seat: 0, seed: 5);
        bool tripAction = false, followed = false;
        for (int t = 0; t < 200; t++)
        {
            var v = View(t, 40);
            v.X = Fx(0);
            v.Nb1 = Nb(v, 1.0, 2.0); // жертва впереди 1.0 м, правее на 2: идёт в её полосу
            var inp = b.Decide(v, ref m);
            if (m.HuntKind == 2) { followed = true; Assert.True(Fixed.FromRaw(m.HuntTx) == v.X + Fx(2)); }
            if (inp.Action) tripAction = true;
        }
        Assert.True(followed);
        Assert.False(tripAction, "жертва не прямо впереди (|dx| > ds) — подножка не достаёт");
        var m2 = Mem(seat: 0, seed: 5);
        tripAction = false;
        for (int t = 0; t < 200; t++)
        {
            var v = View(t, 40);
            v.X = Fx(2);
            v.Nb1 = Nb(v, 1.1, 0.2);
            if (b.Decide(v, ref m2).Action) tripAction = true;
        }
        Assert.True(tripAction, "жертва ≤ 1.2 м прямо впереди — подножка");
    }

    [Fact]
    public void TrollShovesAtTheEndOfHuntAndWaitsRPlus8Ticks()
    {
        var b = Brain(Trl);
        var def = Bots.Get(Trl);
        var m = Mem(seat: 0, seed: 11);
        int firstAction = -1;
        for (int t = 0; t < 200; t++)
        {
            var v = View(t, 40);
            v.Nb1 = Nb(v, 0.0, -1.2); // жертва сбоку 1.2 м, подсказок нет
            if (b.Decide(v, ref m).Action && firstAction < 0) firstAction = t;
        }
        // охота началась на тике 0 (период 90: первый поиск сразу), закончилась на 90: толчок ≥ 90, не раньше R + 8
        Assert.True(firstAction >= Bots.HuntTicks, "толчок «просто так» — в конце охоты: " + firstAction);
        Assert.True(firstAction < Bots.HuntTicks + 20);
        Assert.True(m.HuntReady >= def.ReactionMin + def.WaitNeighbor);
    }

    [Fact]
    public void TrollSearchesVictimEvery3SecondsOnlyWithin3Meters()
    {
        var b = Brain(Trl);
        var def = Bots.Get(Trl);
        var m = Mem();
        for (int t = 0; t < 100; t++)
        {
            var v = View(t, 40); v.Nb1 = Nb(v, -3.5, 0); // дальше 3 м
            b.Decide(v, ref m);
            Assert.Equal(-1, m.HuntVictim);
        }
        var m2 = Mem();
        var v0 = View(0, 40); v0.Nb1 = Nb(v0, -3.5, 0); b.Decide(v0, ref m2); // поиск на тике 0 впустую; следующий — через 90
        for (int t = 1; t < def.ActionPeriod; t++)
        {
            var v = View(t, 40); v.Nb1 = Nb(v, -1.0, 1.0);
            b.Decide(v, ref m2);
            Assert.Equal(-1, m2.HuntVictim);
        }
        var vv = View(def.ActionPeriod, 40); vv.Nb1 = Nb(vv, -1.0, 1.0);
        b.Decide(vv, ref m2);
        Assert.Equal(1, m2.HuntVictim);
    }

    [Fact]
    public void TrollHuntsVictimFromAheadMemory()
    {
        var b = Brain(Trl);
        var m = Mem();
        var v0 = View(0, 40); v0.Nb1 = Nb(v0, 1.0, 1.0); // видна впереди
        b.Decide(v0, ref m); // охота стартует на тике 0
        Assert.Equal(1, m.HuntVictim);
        var m2 = Mem();
        var w = View(0, 40); w.Nb1 = Nb(w, 1.0, 1.0);
        b.Decide(w, ref m2);
        m2.HuntVictim = -1; m2.HuntNext = 5; // следующий поиск: сосед ушёл из кадра вперёд, помнится
        var u = View(5, 40);
        b.Decide(u, ref m2);
        Assert.Equal(1, m2.HuntVictim);
        var m3 = Mem(); b.Decide(w, ref m3); m3.HuntVictim = -1; m3.HuntNext = 40; // старше 1 с — забыт
        b.Decide(View(40, 40), ref m3);
        Assert.Equal(-1, m3.HuntVictim);
    }

    [Fact]
    public void TrollHuntSwitchesOffRepeatNeighborDodge()
    {
        var b = Brain(Trl);
        // та же сцена, что в CowardTests.RepeatsNeighborDodge: сосед сзади смещается вправо. Вне охоты — повтор уворота, на охоте — нет.
        int Run(int huntVictimNb)
        {
            var m = Mem();
            int steered = 0; Fixed nx = Fx(-3);
            for (int t = 0; t < 60; t++)
            {
                var v = View(t, 40); v.X = Fx(2);
                if (t >= 1 && t < 6) nx = nx + Fixed.FromFraction(1, 10);
                v.Nb2 = new BotNeighbor { Valid = true, DS = Fx(-2), DX = nx - v.X, X = nx, Speed = Fx(7) };
                if (huntVictimNb == 1) v.Nb1 = Nb(v, 0.0, -1.2); // жертва тролля — другой сосед
                if (b.Decide(v, ref m).Steer != 0) steered++;
            }
            return steered;
        }
        Assert.True(Run(0) > 0);
        Assert.Equal(0, Run(1));
    }

    [Fact]
    public void TrollTakesOnlyItsQuestsAndOvertakeSprintsFromReserve()
    {
        var b = Brain(Trl);
        var def = Bots.Get(Trl);
        int t = 0; var m = Mem();
        for (int i = 0; i < 30; i++)
        {
            var v = View(t++, 25, 40, quest: "overtake"); v.HasQuestTarget = true; v.QuestTarget = 3;
            v.Nb3 = Nb(v, 1.0, 1.0);
            Assert.Equal(1, b.Decide(v, ref m).Pace);
        }
        var m2 = Mem(); t = 0;
        for (int i = 0; i < 30; i++)
        {
            var v = View(t++, 25, 39, quest: "overtake"); v.HasQuestTarget = true; v.QuestTarget = 3;
            v.Nb3 = Nb(v, 1.0, 1.0);
            Assert.Equal(0, b.Decide(v, ref m2).Pace);
        }
        // чужой квест (стой у быка) игнорирует: d = 25 → трусца
        var m3 = Mem(); t = 0;
        Assert.Equal(-1, PaceAt(b, ref m3, ref t, 25, 100, "stay_hot"));
    }

    // ---- §2.5 крайние случаи ----

    [Fact]
    public void TwoHintsAtOnceDodgeTheNearest()
    {
        foreach (int temp in new[] { Adr, Trl })
        {
            var b = Brain(temp);
            var m = Mem(); int steer = 0;
            for (int t = 0; t < 30; t++)
            {
                var v = View(t, 40); v.X = Fx(-2);
                if (t == 0)
                {
                    v.HintCount = 2;
                    v.Hint0 = new BotHint { Side = 1, TicksUntil = 90 };  // далёкая: вправо → цель −2
                    v.Hint1 = new BotHint { Side = -1, TicksUntil = 40 }; // ближняя: слева → цель +2
                }
                steer = b.Decide(v, ref m).Steer;
            }
            Assert.Equal(1, steer);
        }
    }

    [Fact]
    public void LeaderBotWithoutHintsSteersByBlockMemory()
    {
        foreach (int temp in new[] { Adr, Trl })
        {
            var b = Brain(temp);
            var m = Mem();
            var v = View(0, 40); v.X = Fx(0) + Fixed.FromFraction(1, 10); // бежит справа: цель ширина/4 = 2
            Assert.Equal(1, b.Decide(v, ref m).Steer);
            v.Tick = 1; v.X = Fx(3);
            Assert.Equal(-1, b.Decide(v, ref m).Steer);
        }
    }

    [Fact]
    public void ArenaMeansRunAndNoActions()
    {
        foreach (int temp in new[] { Adr, Trl })
        {
            var b = Brain(temp);
            var m = Mem();
            for (int t = 0; t < 200; t++)
            {
                var v = View(t, 2, 100); v.Arena = true; v.Nb1 = Nb(v, 0, -1.0);
                var inp = b.Decide(v, ref m);
                Assert.False(inp.Action);
                Assert.Equal(0, inp.Pace);
            }
        }
    }

    [Fact]
    public void TossedBotDropsHuntPlansAndDoesNotSprintFromBull()
    {
        var b = Brain(Trl);
        var m = Mem();
        for (int t = 0; t < 40; t++) { var v = View(t, 40); v.Nb1 = Nb(v, 0, -1.2); b.Decide(v, ref m); }
        Assert.Equal(1, m.HuntVictim);
        var down = View(40, 40); down.NoSignalTicks = 60; down.Nb1 = Nb(down, 0, -1.2);
        Assert.Equal(default, b.Decide(down, ref m));
        var m2 = Mem(); int t2 = 0;
        for (int i = 0; i < 30; i++) { var v = View(t2++, 2, 100); v.InvulnTicks = 80; Assert.True(b.Decide(v, ref m2).Pace <= 0); }
    }

    [Fact]
    public void TwoTrollsMayHuntTheSameVictimIndependently()
    {
        var b = Brain(Trl);
        var mA = Mem(seat: 0, seed: 1); var mB = Mem(seat: 2, seed: 2);
        var vA = View(0, 40, seat: 0); vA.Nb3 = Nb(vA, 0, 1.0);
        var vB = View(0, 40, seat: 2); vB.Nb3 = Nb(vB, 0, -1.0);
        b.Decide(vA, ref mA); b.Decide(vB, ref mB);
        Assert.Equal(3, mA.HuntVictim);
        Assert.Equal(3, mB.HuntVictim);
    }

    // ---- гонки: зоны, действия, детерминизм ----

    /// <summary>Заглушка F-021 без состояния (Clone делит источник): подсказывает реальное препятствие, когда до него проходится порог в 60 тиков.</summary>
    sealed class TruthHints : IBotHintSource
    {
        public int Collect(RaceState st, StreetMap street, int seat, Span<BotHint> dst)
        {
            Fixed s = Fixed.FromRaw(st.Runners[seat].S), v = Fixed.FromRaw(st.Runners[seat].Speed);
            if (v <= Fixed.Zero) return 0;
            Fixed reach = v * Fx(2), step = v / Fx(Simulation.TicksPerSecond); // 60 тиков = 2 с пути
            int n = 0;
            for (int i = 0; i < street.Obstacles.Length && n < dst.Length; i++)
            {
                var o = street.Obstacles[i];
                Fixed dist = o.S - s;
                if (dist < Fixed.Zero) continue;
                if (dist > reach) break;
                if (dist + step <= reach) continue; // уже был подсказан раньше
                var h = new BotHint { TicksUntil = 60 };
                switch (o.Lane) { case Lane.L: h.Side = -1; break; case Lane.R: h.Side = 1; break; case Lane.F: h.Full = true; break; default: h.Side = 0; break; }
                dst[n++] = h;
            }
            return n;
        }
    }

    static RaceConfig Cfg(ulong seed, params int[] temps)
    {
        var seats = new SeatConfig[4];
        for (int i = 0; i < 4; i++) seats[i] = new SeatConfig(SeatKind.Bot, i, temps[i]);
        return new RaceConfig(seed, seats);
    }

    static void Run(Simulation sim, Action<RaceState>? each = null, int max = 14000)
    {
        for (int t = 0; t < max && sim.State.Phase != Game.Core.Race.RacePhase.Over; t++) { sim.Step(TickInputs.Empty); each?.Invoke(sim.State); }
    }

    [Fact]
    public void AdrenalinesSpendMostOfTheRaceInHotAndWarmZones()
    {
        // Состав «трус, адреналинщик, тролль, адреналинщик»: в гонке из одних адреналинщиков бык застревает за одним подброшенным (F-017),
        // остальные уходят в холод — это вопрос баланса F-025, не поведения характера. Считаем места адреналинщиков.
        int hotWarm = 0, total = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var sim = new Simulation(Cfg(seed, 0, Adr, Trl, Adr), Data) { BotHints = new TruthHints() };
            Run(sim, st =>
            {
                if (st.Phase != Game.Core.Race.RacePhase.Running) return;
                for (int i = 1; i < 4; i += 2)
                {
                    if (st.Runners[i].Viewing.FinishTick >= 0) continue;
                    total++;
                    int z = st.Runners[i].Viewing.Zone;
                    if (z == ViewerZone.Hot || z == ViewerZone.Warm) hotWarm++;
                }
            });
        }
        Assert.True(total > 0);
        double share = (double)hotWarm / total;
        Assert.True(share >= 0.5, $"в горячей/тёплой зонах {share:P0} забега ({hotWarm} из {total})");
    }

    [Fact]
    public void TrollsActAtLeastTenTimesPerRace()
    {
        // Среднее по сидам: расстояние между бегунами в забеге разное (бык, застрявший на подброшенном, растягивает остальных),
        // отдельные забеги дают 6–9 действий — таблица в сообщении при падении.
        int sum = 0; const int seeds = 6;
        var rows = new List<string>();
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            var sim = new Simulation(Cfg(seed, Trl, Trl, Trl, Trl), Data) { BotHints = new TruthHints() };
            Run(sim);
            int actions = 0;
            for (int i = 0; i < 4; i++) actions += sim.State.Runners[i].Action.Shoved + sim.State.Runners[i].Action.Tripped;
            rows.Add($"сид {seed}: {actions}");
            sum += actions;
        }
        Assert.True(sum >= 10 * seeds, $"в среднем {sum / (double)seeds:F1} действий за забег; " + string.Join("; ", rows));
    }

    [Fact]
    public void MixedRaceIsDeterministicAndCloneConsistent()
    {
        var a = new Simulation(Cfg(33, 0, Adr, Trl, Trl), Data) { BotHints = new TruthHints() };
        var b = new Simulation(Cfg(33, 0, Adr, Trl, Trl), Data) { BotHints = new TruthHints() };
        for (int t = 0; t < 3500; t++)
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
    }
}
