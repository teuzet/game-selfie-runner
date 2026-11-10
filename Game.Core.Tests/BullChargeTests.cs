using System;
using System.Text;
using Game.Core.Bull;
using Game.Core.Math;
using Xunit;
using Xunit.Abstractions;

namespace Game.Core.Tests
{
    public class BullChargeTests
    {
        static readonly GameData Data = GameData.Load();
        const int Glamour = 0, Athlete = 1, Grandma = 2, Nerd = 3;
        readonly ITestOutputHelper _out;
        public BullChargeTests(ITestOutputHelper o) { _out = o; }

        static RaceConfig Cfg(ulong seed, SeatKind kind, int a = Nerd, int b = Nerd, int c = Nerd, int d = Nerd) => new RaceConfig(seed, new[]
        {
            new SeatConfig(kind, a), new SeatConfig(kind, b), new SeatConfig(kind, c), new SeatConfig(kind, d),
        });

        static Fixed Fx(double v) => Fixed.FromRaw((long)(v * Fixed.OneRaw));
        static double Dbl(long raw) => raw / (double)Fixed.OneRaw;

        /// <summary>Тики до выхода быка из загона (3 + 2 с).</summary>
        const int Release = 150;

        static void RunTo(Simulation sim, int tick) { while (sim.Tick < tick) sim.Step(TickInputs.Empty); }

        /// <summary>Ведёт забег до начала рывка: бегун 0 держится в 6 м впереди (в досягаемости, F-102 §3.2), остальные далеко.</summary>
        static void WaitForCharge(Simulation sim)
        {
            for (int t = 0; t < 3000 && sim.State.Charge.ChargeLeft == 0; t++)
            {
                double bs = Dbl(sim.State.Bull.S);
                sim.State.Runners[0].S = Fx(bs + 6).Raw; sim.State.Runners[0].X = sim.State.Bull.X;
                for (int i = 1; i < 4; i++) sim.State.Runners[i].S = Fx(bs + 200 + i * 10).Raw;
                sim.Step(TickInputs.Empty);
            }
            Assert.True(sim.State.Charge.ChargeLeft > 0, "рывок не начался");
        }

        static TickInputs RandomInputs(ref DetRandom rng)
        {
            var ti = TickInputs.Empty;
            for (int i = 0; i < 4; i++)
                ti = ti.With(i, new PlayerInput(rng.Range(-1, 2), rng.Range(-1, 2), rng.Range(0, 4) == 0));
            return ti;
        }

        [Fact]
        public void Deterministic_TwoRunsEqualHashes()
        {
            var a = new Simulation(Cfg(5, SeatKind.Human, Glamour, Athlete, Grandma, Nerd), Data);
            var b = new Simulation(Cfg(5, SeatKind.Human, Glamour, Athlete, Grandma, Nerd), Data);
            var ra = new DetRandom(99); var rb = new DetRandom(99);
            for (int t = 0; t < 3000; t++)
            {
                a.Step(RandomInputs(ref ra)); b.Step(RandomInputs(ref rb));
                Assert.Equal(a.StateHash(), b.StateHash());
            }
            Assert.True(a.State.Charge.Count > 0);
        }

        [Fact]
        public void CloneStepsGiveSameHash()
        {
            var a = new Simulation(Cfg(6, SeatKind.Human), Data);
            RunTo(a, 1200);
            var c = a.Clone();
            for (int t = 0; t < 600; t++) { a.Step(TickInputs.Empty); c.Step(TickInputs.Empty); }
            Assert.Equal(a.StateHash(), c.StateHash());
        }

        [Fact]
        public void NoChargeBeforeRelease_FirstChargeAfter8s()
        {
            var sim = new Simulation(Cfg(3, SeatKind.Human), Data);
            RunTo(sim, Release);
            Assert.Equal(0, sim.State.Charge.Count);
            int first = -1;
            for (int t = 0; t < 600 && first < 0; t++)
            {
                sim.Step(TickInputs.Empty);
                if (sim.State.Charge.Count > 0) first = sim.Tick;
            }
            Assert.True(first >= Release + Data.BullData.ChargeMinIntervalSec * 30 - 1, "первый рывок слишком рано: " + first);
            Assert.True(first <= Release + Data.BullData.ChargeMaxIntervalSec * 30 + 60, "первый рывок слишком поздно: " + first);
        }

        [Fact(Skip = "F-081: на живом рывке интервалы между рывками вышли за F-100 §3.1 п.6 — F-068 подбирает ручки bull.json и возвращает тест (решение Nick по вопросу F-081)")]
        public void IntervalsBetweenChargesFollowBullData()
        {
            var sim = new Simulation(Cfg(11, SeatKind.Human), Data);
            int prevCount = 0, prevTick = -1;
            for (int t = 0; t < 9000; t++)
            {
                sim.Step(TickInputs.Empty);
                int cnt = sim.State.Charge.Count;
                if (cnt > prevCount)
                {
                    if (prevTick >= 0)
                    {
                        int gap = sim.State.Charge.LastChargeTick - prevTick;
                        // F-100 §3.1 п.6: если допустимых целей нет (лежащий, охлаждение 30 с, порог арены), рывок пропускается и
                        // следующий — через новый интервал: пропуск даёт до двух интервалов подряд плюс охлаждение
                        int slack = (Data.BullData.TossCooldownSec + Data.BullData.ChargeMaxIntervalSec) * 30;
                        Assert.InRange(gap, Data.BullData.ChargeMinIntervalSec * 30, Data.BullData.ChargeMaxIntervalSec * 30 + slack);
                    }
                    prevTick = sim.State.Charge.LastChargeTick; prevCount = cnt;
                }
            }
            Assert.True(prevCount > 0);
        }

        [Fact]
        public void TossMovesTarget8mAndSilencesInput_ThenInvulnerable()
        {
            var sim = new Simulation(Cfg(4, SeatKind.Human, Nerd, Nerd, Nerd, Nerd), Data);
            RunTo(sim, Release + 20);
            double bs = Dbl(sim.State.Bull.S);
            // место 1 — прямо перед быком на 0.5 м, остальные далеко впереди
            ref var r1 = ref sim.State.Runners[1];
            r1.S = Fx(bs + 0.5).Raw; r1.X = sim.State.Bull.X; r1.Speed = 0;
            for (int i = 0; i < 4; i++) if (i != 1) { sim.State.Runners[i].S = Fx(bs + 200 + i * 10).Raw; }
            long sBefore = r1.S;
            sim.Step(TickInputs.Empty);
            ref var t1 = ref sim.State.Runners[1].Toss;
            Assert.Equal(1, t1.Tossed);
            Assert.Equal(sim.State.Tick - 1, t1.LastTossTick);
            Assert.InRange(Dbl(t1.LastTossFromS), Dbl(sBefore) - 0.01, Dbl(sBefore) + 0.5); // бегун успел шагнуть в этом тике
            Assert.Equal(8.0, Dbl(sim.State.Runners[1].S) - Dbl(t1.LastTossFromS), 3);
            Assert.Equal(90, t1.NoSignalTicks);
            Assert.Equal(90, t1.InvulnTicks);
            Assert.True(sim.State.Bull.SlowTicks > 0);

            // вход игнорируется: руль вправо не сдвигает x (кроме внешних сил)
            long x0 = sim.State.Runners[1].X;
            var steer = TickInputs.Empty.With(1, new PlayerInput(1, 0, true));
            for (int i = 0; i < 30; i++) sim.Step(steer);
            Assert.Equal(x0, sim.State.Runners[1].X);
            Assert.Equal(0, t1.Rose);

            // «встал» через 3 с и ещё 3 с неуязвим
            while (sim.State.Runners[1].Toss.NoSignalTicks > 0) sim.Step(TickInputs.Empty);
            Assert.Equal(1, sim.State.Runners[1].Toss.Rose);
            Assert.Equal(sim.State.Tick - 1, sim.State.Runners[1].Toss.LastRoseTick);
            Assert.True(sim.State.Runners[1].Toss.InvulnTicks > 0);
        }

        [Fact]
        public void TossedRunnerLiesDuringNoSignal_ThenAcceleratesToRun_AndIsNotTossedAgain()
        {
            var sim = new Simulation(Cfg(4, SeatKind.Human, Nerd, Nerd, Nerd, Nerd), Data);
            RunTo(sim, Release + 20);
            double bs = Dbl(sim.State.Bull.S);
            ref var r1 = ref sim.State.Runners[1];
            r1.S = Fx(bs + 0.5).Raw; r1.X = sim.State.Bull.X; r1.Speed = Fx(5).Raw;
            for (int i = 0; i < 4; i++) if (i != 1) sim.State.Runners[i].S = Fx(bs + 200 + i * 10).Raw;
            sim.Step(TickInputs.Empty);
            Assert.Equal(1, sim.State.Runners[1].Toss.Tossed);

            // «нет сигнала»: s не растёт, даже при входе «вперёд, спринт»
            long s0 = sim.State.Runners[1].S;
            var sprint = TickInputs.Empty.With(1, new PlayerInput(0, 1, false));
            int noSignal = sim.State.Runners[1].Toss.NoSignalTicks;
            for (int i = 0; i < noSignal; i++)
            {
                sim.Step(sprint);
                Assert.Equal(s0, sim.State.Runners[1].S);
                // в тик подъёма (последний) встающий получает темп бега сразу — F-100 §3.1 п.4
                if (i < noSignal - 1) Assert.Equal(0, sim.State.Runners[1].Speed);
                else Assert.True(Fixed.FromRaw(sim.State.Runners[1].Speed) >= sim.SeatData(1).RunSpeed * sim.Street.SlopeAt(Fixed.FromRaw(s0)) * Fx(0.99));
            }
            Assert.Equal(0, sim.State.Runners[1].Toss.NoSignalTicks);

            // неуязвимость: разгон до бега, подбросить нельзя
            int invuln = sim.State.Runners[1].Toss.InvulnTicks;
            Assert.True(invuln > 0);
            for (int i = 0; i < invuln; i++)
            {
                sim.Step(TickInputs.Empty);
                Assert.Equal(1, sim.State.Runners[1].Toss.Tossed);
            }
            var rd = sim.SeatData(1);
            Fixed runTarget = rd.RunSpeed * sim.Street.SlopeAt(Fixed.FromRaw(sim.State.Runners[1].S));
            Assert.True(sim.State.Runners[1].S > s0);
            Assert.True(Fixed.FromRaw(sim.State.Runners[1].Speed) >= runTarget * Fx(0.99), "не разогнался до бега");
        }

        [Fact]
        public void NoSignalDurationComesFromCharacter()
        {
            foreach (var (ch, sec) in new[] { (Athlete, 2.5), (Grandma, 5.0), (Nerd, 3.0) })
            {
                var sim = new Simulation(Cfg(4, SeatKind.Human, ch, ch, ch, ch), Data);
                RunTo(sim, Release + 20);
                double bs = Dbl(sim.State.Bull.S);
                sim.State.Runners[2].S = Fx(bs + 0.5).Raw; sim.State.Runners[2].X = sim.State.Bull.X;
                for (int i = 0; i < 4; i++) if (i != 2) sim.State.Runners[i].S = Fx(bs + 200 + i * 10).Raw;
                sim.Step(TickInputs.Empty);
                Assert.Equal((int)(sec * 30), sim.State.Runners[2].Toss.NoSignalTicks);
                Assert.True(sim.State.Runners[2].Toss.NoSignalTicks + sim.State.Runners[2].Toss.InvulnTicks >= 180);
            }
        }

        [Fact]
        public void InvulnerableRunnerIsNotTargetedNorTossed_BullDoesNotPassHim()
        {
            var sim = new Simulation(Cfg(4, SeatKind.Human), Data);
            RunTo(sim, Release + 20);
            double bs = Dbl(sim.State.Bull.S);
            ref var t = ref sim.State.Runners[0].Toss;
            t.InvulnTicks = 200;
            sim.State.Runners[0].S = Fx(bs + 0.3).Raw; sim.State.Runners[0].X = sim.State.Bull.X; sim.State.Runners[0].Speed = 0;
            for (int i = 1; i < 4; i++) sim.State.Runners[i].S = Fx(bs + 400 + i * 10).Raw;
            for (int k = 0; k < 60; k++)
            {
                sim.State.Runners[0].S = Fx(bs + 0.3).Raw; // стоит
                sim.Step(TickInputs.Empty);
                Assert.Equal(0, sim.State.Runners[0].Toss.Tossed);
                Assert.True(sim.State.Runners[0].S >= sim.State.Bull.S);
            }
        }

        [Fact]
        public void ChargeTargetWeightFavoursTastyAndNear()
        {
            // Атлет (вкусность 1.6) против бабули (0.6) на одинаковой дистанции: выбирается атлет заметно чаще.
            int athlete = 0, grandma = 0;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var sim = new Simulation(Cfg(seed, SeatKind.Human, Athlete, Grandma, Nerd, Nerd), Data);
                RunTo(sim, Release + 20);
                double bs = Dbl(sim.State.Bull.S);
                // 7 м: обе цели в досягаемости (атлет на беге 7.35 м/с — 8.3 м, бабуля 6.16 — 10.7 м; F-102 §3.2); один выбор цели в этом тике
                sim.State.Charge.Cooldown = 1;
                sim.State.Runners[0].S = Fx(bs + 7).Raw; sim.State.Runners[0].X = sim.State.Bull.X;
                sim.State.Runners[1].S = Fx(bs + 7).Raw; sim.State.Runners[1].X = sim.State.Bull.X;
                sim.State.Runners[2].S = Fx(bs + 100).Raw; sim.State.Runners[3].S = Fx(bs + 110).Raw;
                sim.Step(TickInputs.Empty);
                Assert.Equal(1, sim.State.Charge.Count);
                if (sim.State.Charge.LastChargeTarget == 0) athlete++;
                else if (sim.State.Charge.LastChargeTarget == 1) grandma++;
            }
            _out.WriteLine($"athlete={athlete} grandma={grandma}");
            Assert.True(athlete > grandma * 2, $"athlete={athlete} grandma={grandma}");
        }

        // F-071, design/F-100.md → «Подброс закрывает эпизод» (3.1):
        // «Бык держит дистанцию позади защищённого, а не паркуется на его `s` и не обходит: ближе 4 м к защищённому впереди бык не подходит — идёт не быстрее его.»
        // «Защищённые. Бык не выбирает целью рывка и не подбрасывает: (а) лежащего и неуязвимого; (б) бегуна на пороге арены — последние 8 м перед ней — и на арене; (в) финишировавшего.»
        // «Охлаждение цели: 30 с от подброса бык не выбирает этого бегуна целью рывка. Контакт при этом работает.»
        // «Подброс с причиной — «рывок» (бык был в рывке) или «контакт» (вне рывка).»

        static Simulation Prepared(ulong seed)
        {
            var sim = new Simulation(Cfg(seed, SeatKind.Human), Data);
            RunTo(sim, Release + 20);
            return sim;
        }

        [Fact]
        public void BullHoldsGapBehindTossedRunner_NotOnHisS()
        {
            var sim = Prepared(4);
            double bs = Dbl(sim.State.Bull.S);
            sim.State.Runners[1].S = Fx(bs + 0.5).Raw; sim.State.Runners[1].X = sim.State.Bull.X;
            for (int i = 0; i < 4; i++) if (i != 1) sim.State.Runners[i].S = Fx(bs + 200 + i * 10).Raw;
            sim.Step(TickInputs.Empty);
            Assert.Equal(1, sim.State.Runners[1].Toss.Tossed);
            double gap = Dbl(Data.BullData.TossWaitGap.Raw);
            double minGap = double.MaxValue;
            int downTicks = sim.State.Runners[1].Toss.NoSignalTicks + sim.State.Runners[1].Toss.InvulnTicks;
            for (int t = 0; t < downTicks; t++)
            {
                sim.Step(TickInputs.Empty);
                minGap = System.Math.Min(minGap, Dbl(sim.State.Runners[1].S - sim.State.Bull.S));
                Assert.Equal(1, sim.State.Runners[1].Toss.Tossed); // повторного подброса нет
            }
            Assert.True(minGap >= gap - 0.01, $"бык подошёл к защищённому на {minGap:F2} м, отступ {gap}");
        }

        [Fact]
        public void TossCauseIsChargeOrContact()
        {
            // контакт без рывка
            var sim = Prepared(4);
            double bs = Dbl(sim.State.Bull.S);
            sim.State.Runners[1].S = Fx(bs + 0.5).Raw; sim.State.Runners[1].X = sim.State.Bull.X;
            for (int i = 0; i < 4; i++) if (i != 1) sim.State.Runners[i].S = Fx(bs + 200 + i * 10).Raw;
            Assert.Equal(0, sim.State.Charge.ChargeLeft);
            sim.Step(TickInputs.Empty);
            Assert.Equal(TossCause.Contact, sim.State.Runners[1].Toss.LastCause);

            // рывок: цель в 5 м прямо перед быком
            sim = Prepared(4);
            for (int t = 0; t < 3000 && sim.State.Charge.ChargeLeft == 0; t++)
            {
                double b0 = Dbl(sim.State.Bull.S);
                sim.State.Runners[0].S = Fx(b0 + 5).Raw; sim.State.Runners[0].X = sim.State.Bull.X;
                for (int i = 1; i < 4; i++) sim.State.Runners[i].S = Fx(b0 + 200 + i * 10).Raw; // единственная цель — место 0
                sim.State.Bull.Speed = Fx(10.9).Raw; // как на разгоне: рывок стартует с высокой скорости
                sim.Step(TickInputs.Empty);
            }
            Assert.True(sim.State.Charge.ChargeLeft > 0 || sim.State.Charge.Tosses > 0);
            int tossed = -1;
            for (int t = 0; t < 120 && tossed < 0; t++)
            {
                for (int i = 0; i < 4; i++) if (sim.State.Runners[i].Toss.Tossed > 0) tossed = i;
                if (tossed < 0) { sim.State.Bull.Speed = Fx(10.9).Raw; sim.Step(TickInputs.Empty); }
            }
            Assert.True(tossed >= 0, $"рывок не закончился подбросом: left={sim.State.Charge.ChargeLeft} tg={sim.State.Charge.Target} bull={Dbl(sim.State.Bull.S):F1} r0={Dbl(sim.State.Runners[0].S):F1} r1={Dbl(sim.State.Runners[1].S):F1} ph={sim.State.Bull.Phase}");
            Assert.Equal(TossCause.Charge, sim.State.Runners[tossed].Toss.LastCause);
        }

        [Fact]
        public void FinishedRunnerIsNotTossed()
        {
            var sim = Prepared(4);
            double bs = Dbl(sim.State.Bull.S);
            sim.State.Runners[1].S = Fx(bs + 0.5).Raw; sim.State.Runners[1].X = sim.State.Bull.X;
            sim.State.Runners[1].Viewing.FinishTick = 1;
            for (int i = 0; i < 4; i++) if (i != 1) sim.State.Runners[i].S = Fx(bs + 200 + i * 10).Raw;
            sim.Step(TickInputs.Empty);
            Assert.Equal(0, sim.State.Runners[1].Toss.Tossed);
        }

        [Fact]
        public void RunnerOnArenaThresholdIsNotTossedNorTargeted()
        {
            var sim = Prepared(4);
            double arena = Dbl(sim.ArenaStart.Raw);
            double thr = Dbl(Data.BullData.ChargeTossDistance.Raw);
            double bs = Dbl(sim.State.Bull.S);
            // бык вплотную к бегуну на пороге (arena − 4 м): не подбрасывает и не выбирает целью
            sim.State.Runners[1].S = Fx(arena - thr + 4).Raw; sim.State.Runners[1].X = sim.State.Bull.X;
            sim.State.Bull.S = Fx(arena - thr + 3.5).Raw;
            for (int i = 0; i < 4; i++) if (i != 1) sim.State.Runners[i].S = Fx(arena - 200 - i).Raw;
            Assert.True(BullCharge.Shielded(sim, 1));
            Assert.False(BullCharge.Shielded(sim, 0));
            sim.Step(TickInputs.Empty);
            Assert.Equal(0, sim.State.Runners[1].Toss.Tossed);

            // несколько интервалов рывка: единственный кандидат на пороге (остальные на арене — тоже защищённые) не цель, бык не ближе tossWaitGap
            double gap = Dbl(Data.BullData.TossWaitGap.Raw);
            int charges = sim.State.Charge.Count;
            for (int t = 0; t < 900; t++)
            {
                sim.State.Bull.S = Fx(arena - thr - 1).Raw;
                sim.State.Runners[1].S = Fx(arena - thr + 4).Raw; sim.State.Runners[1].X = sim.State.Bull.X;
                for (int i = 0; i < 4; i++) if (i != 1) sim.State.Runners[i].S = Fx(arena + 20 + i).Raw;
                sim.Step(TickInputs.Empty);
                Assert.Equal(0, sim.State.Runners[1].Toss.Tossed);
                Assert.True(Dbl(sim.State.Runners[1].S - sim.State.Bull.S) >= gap - 0.01, "бык ближе tossWaitGap к бегуну на пороге");
                if (sim.State.Charge.Count != charges) Assert.NotEqual(1, sim.State.Charge.LastChargeTarget);
                charges = sim.State.Charge.Count;
                Assert.NotEqual(1, sim.State.Charge.Target);
            }
        }

        [Fact]
        public void TossedRunnerIsNotChargeTargetDuringCooldown_ContactStillTosses()
        {
            var sim = Prepared(4);
            int cd = Data.BullData.TossCooldownSec * 30;
            double bs = Dbl(sim.State.Bull.S);
            sim.State.Runners[1].S = Fx(bs + 0.5).Raw; sim.State.Runners[1].X = sim.State.Bull.X;
            for (int i = 0; i < 4; i++) if (i != 1) sim.State.Runners[i].S = Fx(bs + 500 + i * 10).Raw;
            sim.Step(TickInputs.Empty);
            int tossTick = sim.State.Runners[1].Toss.LastTossTick;
            int charges = sim.State.Charge.Count;
            // место 1 — единственное в досягаемости, остальные далеко: за охлаждение ни одного рывка на него
            while (sim.Tick < tossTick + cd - 5)
            {
                double b0 = Dbl(sim.State.Bull.S);
                for (int i = 0; i < 4; i++) if (i != 1) sim.State.Runners[i].S = Fx(b0 + 500 + i * 10).Raw;
                if (sim.State.Runners[1].S < sim.State.Bull.S + Fx(8).Raw) sim.State.Runners[1].S = sim.State.Bull.S + Fx(10).Raw;
                sim.Step(TickInputs.Empty);
                if (sim.State.Charge.Count != charges)
                    Assert.NotEqual(1, sim.State.Charge.LastChargeTarget);
                charges = sim.State.Charge.Count;
            }
            // защита кончилась, охлаждение ещё идёт: контакт работает (F-100 §3.1 п.6 «Контакт при этом работает»)
            Assert.False(BullCharge.Protected(sim.State.Runners[1].Toss));
            Assert.True(sim.Tick < tossTick + cd);
            sim.State.Runners[1].S = sim.State.Bull.S + Fx(0.5).Raw; sim.State.Runners[1].X = sim.State.Bull.X;
            sim.Step(TickInputs.Empty);
            Assert.Equal(2, sim.State.Runners[1].Toss.Tossed);
            Assert.Equal(TossCause.Contact, sim.State.Runners[1].Toss.LastCause);
        }

        /// <summary>Максимальная скорость быка за 120 тиков: бегун 0 в 6 м впереди (якорь), остальные так, что отставание от медианы толпы = lag.</summary>
        static double MaxBullSpeedWithAnchor(double lag)
        {
            var sim = Prepared(9);
            double max = 0;
            for (int t = 0; t < 120; t++)
            {
                double b0 = Dbl(sim.State.Bull.S);
                sim.State.Runners[0].S = Fx(b0 + 6).Raw; sim.State.Runners[0].X = sim.State.Bull.X;
                for (int i = 1; i < 4; i++) sim.State.Runners[i].S = Fx(b0 + lag).Raw;
                sim.State.Charge.Cooldown = 100000; // без рывков: проверяем только резинку
                sim.Step(TickInputs.Empty);
                max = System.Math.Max(max, Dbl(sim.State.Bull.Speed));
            }
            return max;
        }

        // F-083, design/F-102.md → «Резинка без якоря» (3.1):
        // «Якорь. Вне рывка бегун впереди быка ближе 8 м держит его на крейсере. Теперь — только пока отставание ≤ порога снятия якоря 20 м (тюнить 15–30).»
        // «Прорыв. Если отставание выросло выше порога, а впереди якорь, бык разгоняется до 9.5 м/с и держит разгон до отставания 12 м, на соседа не глядит.»
        [Fact]
        public void BullDoesNotAccelerateTo9_5WhileRunnerAheadWithin8m_LagWithinAnchorLag()
        {
            double cruise = Dbl(Data.BullData.CruiseSpeed.Raw);
            Assert.True(MaxBullSpeedWithAnchor(15) <= cruise * 1.2 + 0.01, "бык разогнался при бегуне впереди ближе 8 м и отставании 15 м");
        }

        [Fact]
        public void BullAcceleratesTo9_5WhileRunnerAheadWithin8m_LagAboveAnchorLag()
        {
            // 22 м: выше anchorLag (20), но ниже lagFar (25) — порог 20, а не 25
            double lag = Dbl(Data.BullData.AnchorLag.Raw) + 2;
            Assert.True(lag < Dbl(Data.BullData.LagFar.Raw));
            Assert.True(MaxBullSpeedWithAnchor(lag) > Dbl(Data.BullData.CatchUpSpeed.Raw) - 0.5, "отставание 22 м > порога: якорь не держит, бык рвётся к 9.5");
        }

        // F-083, design/F-102.md → «Достижимая цель рывка» (3.2):
        // «Вес цели 0, если она дальше эффективной досягаемости `(chargeSpeed − скорость цели) × chargeSec + chargeReach`. Дистанция берётся вдоль улицы в тик выбора, скорость цели — её текущая.»
        // «Целей нет — рывок пропускается до нового интервала 8–15 с, как сейчас.»
        // Бегущий ботаник 7.0 м/с — досягаемость 9 м, трусящий 5.5 м/с — 12 м.
        static (int Count, int Target, int Cooldown) ChargeRoll(double dist, int pace, double speed)
        {
            var sim = Prepared(9);
            double bs = Dbl(sim.State.Bull.S);
            Assert.Equal(0, sim.State.Charge.Count);
            sim.State.Charge.Cooldown = 1; // в этом тике рывок выбирает цель
            sim.State.Runners[0].S = Fx(bs + dist).Raw; sim.State.Runners[0].X = sim.State.Bull.X; sim.State.Runners[0].Speed = Fx(speed).Raw;
            for (int i = 1; i < 4; i++) sim.State.Runners[i].S = Fx(bs + 200 + i * 10).Raw;
            sim.Step(TickInputs.Empty.With(0, new PlayerInput(0, pace, false)));
            return (sim.State.Charge.Count, sim.State.Charge.Target, sim.State.Charge.Cooldown);
        }

        [Fact]
        public void ChargeTargetBeyondEffectiveReachIsNotChosen_RunnerAt20m()
        {
            var (count, _, cooldown) = ChargeRoll(20, 0, 7.0); // 20 м > 9 м
            Assert.Equal(0, count);
            Assert.True(cooldown >= Data.BullData.ChargeMinIntervalSec * Simulation.TicksPerSecond - 1, "рывок пропущен: новый интервал 8–15 с");
        }

        [Fact]
        public void ChargeTargetWithinEffectiveReachIsChosen_RunnerAt6m()
        {
            var (count, target, _) = ChargeRoll(6, 0, 7.0); // 6 м < 9 м
            Assert.Equal(1, count);
            Assert.Equal(0, target);
        }

        [Fact]
        public void JoggingTargetAt11mIsChosen()
        {
            var (count, target, _) = ChargeRoll(11, -1, 5.5); // 11 м < 12 м у трусящего (на беге 7 м/с была бы недостижима)
            Assert.Equal(1, count);
            Assert.Equal(0, target);
        }

        [Fact]
        public void FarRunnersAreNotTargeted()
        {
            var sim = new Simulation(Cfg(8, SeatKind.Human), Data);
            RunTo(sim, Release - 1);
            for (int t = 0; t < 900; t++)
            {
                double bs = Dbl(sim.State.Bull.S);
                for (int i = 0; i < 4; i++) { sim.State.Runners[i].S = Fx(bs + 31 + i).Raw; }
                sim.Step(TickInputs.Empty);
            }
            Assert.Equal(0, sim.State.Charge.Count);
        }

        [Fact]
        public void ChargeSpeedIsAbout11AndLasts2Seconds()
        {
            var sim = new Simulation(Cfg(21, SeatKind.Human), Data);
            RunTo(sim, Release);
            WaitForCharge(sim);
            // бегуны в 15–20 м впереди (вне режима разгона резинки 9.5 — он маскировал баг F-081), смотрим скорость
            long maxSpeed = 0, speedAt1s = 0; int ticks = 0;
            while (sim.State.Charge.ChargeLeft > 0 && ticks < 200)
            {
                double bs = Dbl(sim.State.Bull.S);
                for (int i = 0; i < 4; i++) sim.State.Runners[i].S = Fx(bs + 15 + i).Raw;
                sim.Step(TickInputs.Empty);
                if (sim.State.Bull.Speed > maxSpeed) maxSpeed = sim.State.Bull.Speed;
                ticks++;
                if (ticks == 30) speedAt1s = sim.State.Bull.Speed;
            }
            _out.WriteLine($"ticks={ticks} maxSpeed={Dbl(maxSpeed)} speedAt1s={Dbl(speedAt1s)}");
            Assert.InRange(ticks, Data.BullData.ChargeSec * 30 - 5, Data.BullData.ChargeSec * 30 + 1);
            Assert.True(Dbl(speedAt1s) >= 10.5, $"через 1 с рывка скорость {Dbl(speedAt1s)} < 10.5");
            Assert.True(Dbl(maxSpeed) <= 11.01 * 1.15, "скорость в рывке выше chargeSpeed × уклон");
        }

        [Fact]
        public void ChargeSpeedTrace_RisesByAccelPerTickToChargeSpeedAndHolds()
        {
            var sim = new Simulation(Cfg(21, SeatKind.Human), Data);
            RunTo(sim, Release);
            WaitForCharge(sim);
            var bd = Data.BullData;
            Fixed dv = bd.Accel * Fixed.FromFraction(1, 30);
            var sb = new StringBuilder();
            long prevSpeed = sim.State.Bull.Speed; int ticks = 0, atTarget = -1;
            long lastTarget = 0;
            while (sim.State.Charge.ChargeLeft > 0 && ticks < 200)
            {
                double bs = Dbl(sim.State.Bull.S);
                Fixed sPrev = Fixed.FromRaw(sim.State.Bull.S);
                for (int i = 0; i < 4; i++) sim.State.Runners[i].S = Fx(bs + 15 + i).Raw;
                sim.Step(TickInputs.Empty);
                Fixed mul = sim.Street.SlopeAt(sPrev);
                if (sim.Street.Blocks[sim.Street.BlockIndexAt(sPrev)].Def.Id == "stairs") mul = mul * bd.StairsMul;
                Fixed target = bd.ChargeSpeed * mul;
                long speed = sim.State.Bull.Speed;
                Fixed prev = Fixed.FromRaw(prevSpeed);
                Fixed expected = prev < target ? Fixed.Min(prev + dv, target) : Fixed.Max(prev - dv, target);
                sb.AppendLine($"tick {ticks}: speed={Dbl(speed):F3} target={(double)target:F3}");
                if (sim.State.Charge.ChargeLeft > 0 || ticks < 59) Assert.Equal(expected.Raw, speed);
                if (atTarget < 0 && speed == target.Raw) atTarget = ticks;
                lastTarget = target.Raw; prevSpeed = speed; ticks++;
            }
            _out.WriteLine(sb.ToString());
            Assert.True(atTarget >= 0 && atTarget <= 30, "скорость не дошла до chargeSpeed × уклон за ≈0.6–1 с");
            Assert.Equal(lastTarget, prevSpeed); // удержание до конца рывка
        }

        [Fact]
        public void ChargeSpeedChangesTheRace_12VsDifferentFrom11()
        {
            string json = System.IO.File.ReadAllText(BullJsonPath(), Encoding.UTF8);
            var fast = new GameData(Data.RunnerData, BullData.Parse(json.Replace("\"chargeSpeed\": 11,", "\"chargeSpeed\": 12,")),
                Data.StreetData, Data.CharacterData, Data.ActionData, Data.EconomyData, Game.Core.Bots.BotData.None(), Data.HintData);
            var a = new Simulation(Cfg(7, SeatKind.Human), Data).Running();
            var b = new Simulation(Cfg(7, SeatKind.Human), fast).Running();
            var rng = new DetRandom(7);
            for (int t = 0; t < 3000; t++) { var ti = RandomInputs(ref rng); a.Step(ti); b.Step(ti); }
            Assert.True(a.State.Charge.Count > 0);
            Assert.NotEqual(a.StateHash(), b.StateHash());
        }

        [Fact]
        public void TossOfNonTargetInChargeWindowIsContact_TargetIsCharge()
        {
            var sim = Prepared(4);
            sim.State.Charge.Cooldown = 1;
            for (int t = 0; t < 300 && sim.State.Charge.ChargeLeft == 0; t++)
            {
                double b0 = Dbl(sim.State.Bull.S);
                sim.State.Runners[0].S = Fx(b0 + 6).Raw; sim.State.Runners[0].X = sim.State.Bull.X; // в досягаемости (F-102 §3.2)
                for (int i = 1; i < 4; i++) sim.State.Runners[i].S = Fx(b0 + 500 + i * 10).Raw;
                sim.Step(TickInputs.Empty);
            }
            Assert.True(sim.State.Charge.ChargeLeft > 0);
            Assert.Equal(0, sim.State.Charge.Target);
            // место 1 — под рогами в окне рывка, но не цель
            sim.State.Runners[1].S = sim.State.Bull.S + Fx(0.3).Raw; sim.State.Runners[1].X = sim.State.Bull.X;
            sim.State.Runners[0].S = sim.State.Bull.S + Fx(12).Raw; sim.State.Runners[0].X = sim.State.Bull.X;
            sim.Step(TickInputs.Empty);
            Assert.Equal(1, sim.State.Runners[1].Toss.Tossed);
            Assert.Equal(TossCause.Contact, sim.State.Runners[1].Toss.LastCause);
        }

        static string BullJsonPath()
        {
            string? d = AppContext.BaseDirectory;
            while (d != null)
            {
                string c = System.IO.Path.Combine(d, "Game.Core", "Data", "bull.json");
                if (System.IO.File.Exists(c)) return c;
                d = System.IO.Path.GetDirectoryName(d.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            }
            throw new System.IO.FileNotFoundException("Game.Core/Data/bull.json");
        }

        static double MaxChargeSpeed(GameData data)
        {
            var sim = new Simulation(Cfg(21, SeatKind.Human), data);
            RunTo(sim, Release);
            WaitForCharge(sim);
            long max = 0; int ticks = 0;
            while (sim.State.Charge.ChargeLeft > 0 && ticks < 400)
            {
                double bs = Dbl(sim.State.Bull.S);
                for (int i = 0; i < 4; i++) sim.State.Runners[i].S = Fx(bs + 25 + i).Raw;
                sim.Step(TickInputs.Empty);
                max = sim.State.Bull.Speed; // скорость к концу рывка (старт идёт с крейсерской)
                ticks++;
            }
            return Dbl(max);
        }

        [Fact]
        public void ChargeNumbersComeFromJson_ChangingSpeedChangesCharge()
        {
            Assert.Equal(11.0, (double)Data.BullData.ChargeSpeed);
            Assert.Equal(9.5, (double)Data.BullData.CatchUpSpeed); // разгон/резинка — другой ключ
            string json = System.IO.File.ReadAllText(BullJsonPath(), Encoding.UTF8);
            Assert.Contains("\"chargeSpeed\": 11,", json);
            var slow = new GameData(Data.RunnerData, BullData.Parse(json.Replace("\"chargeSpeed\": 11,", "\"chargeSpeed\": 7,")),
                Data.StreetData, Data.CharacterData, Data.ActionData, Data.EconomyData, Game.Core.Bots.BotData.None(), Data.HintData);
            double normal = MaxChargeSpeed(Data), slowed = MaxChargeSpeed(slow);
            _out.WriteLine($"normal={normal} slowed={slowed}");
            // F-081: рывок ведёт только MoveCharging, скорость идёт к chargeSpeed × уклон (спуск до ×1.1)
            Assert.True(slowed < normal - 3.0, $"normal={normal} slowed={slowed}");
            Assert.InRange(normal, 10.5, 11.01 * 1.15);
        }

        [Fact]
        public void Random500Runs_NoRunnerBehindBull_TossGapAtLeast6s()
        {
            var sb = new StringBuilder();
            int totalTosses = 0;
            for (ulong seed = 1; seed <= 500; seed++)
            {
                var kind = (seed & 1) == 0 ? SeatKind.Human : SeatKind.Bot;
                var sim = new Simulation(Cfg(seed, kind, (int)(seed % 4), (int)((seed / 4) % 4), (int)((seed / 16) % 4), (int)((seed / 64) % 4)), Data);
                var rng = new DetRandom(seed * 7919 + 13);
                int[] lastToss = { -1000, -1000, -1000, -1000 };
                int[] lastCount = new int[4];
                int ticks = seed % 5 == 0 ? 9000 : 2400;
                for (int t = 0; t < ticks; t++)
                {
                    sim.Step(RandomInputs(ref rng));
                    if (sim.State.Bull.Phase == (int)BullPhase.Pen) continue;
                    for (int i = 0; i < 4; i++)
                    {
                        if (sim.State.Runners[i].S < sim.State.Bull.S)
                            Assert.Fail($"seed {seed} tick {sim.Tick}: бегун {i} позади быка: {Dbl(sim.State.Runners[i].S)} < {Dbl(sim.State.Bull.S)}");
                        if (sim.State.Runners[i].Toss.Tossed != lastCount[i])
                        {
                            int gap = sim.Tick - lastToss[i];
                            if (gap < 180) Assert.Fail($"seed {seed}: бегун {i} подброшен дважды за {gap} тиков");
                            lastToss[i] = sim.Tick; lastCount[i] = sim.State.Runners[i].Toss.Tossed; totalTosses++;
                        }
                    }
                }
            }
            _out.WriteLine("tosses=" + totalTosses);
            Assert.True(totalTosses > 100, "подбросов подозрительно мало: " + totalTosses);
        }

        // F-072, design/F-100.md → «Рывок, от которого можно уйти» (3.2):
        // «Начало рывка — событие ядра «бык рвётся на X» (цель X…). Боты по-прежнему видят «рывок на меня» из состояния.»
        // «Рывок — 2 с на 11 м/с (было 3 с). Поперёк бык доворачивает за целью не быстрее 1 м/с (было 6): шаг вбок 1.2 м при руле 3 м/с
        //  выводит из-под рогов (≤ 1 м между центрами) за 0.5 с.»
        // «Промах. Бык в рывке поравнялся по s с любым бегуном, который дальше 1 м поперёк, — рывок кончается: бык не обгоняет (встаёт вровень),
        //  2 с идёт на 30% медленнее и 1 с не доворачивает.»
        // «Достал (≤ 1 м) — подброс с причиной «рывок»; кончился рывок, никого не достав, — бык возвращается к резинке.»
        // «вбок + спринт — уходит при d ≥ 2.5 м»; «Кто не ответил — подброшен.»

        /// <summary>Манекен в 6 м впереди быка (остальные далеко) в тик начала рывка; reaction — тиков до ответа, side — шаг вбок.</summary>
        static (Simulation sim, int tossedTick, int cause, int events) RunDodge(ulong seed, bool stand, bool sidestep, bool sprint, int reaction, int stepTicks = 12)
        {
            var sim = Prepared(seed);
            sim.State.Charge.Cooldown = 1;
            double bs = Dbl(sim.State.Bull.S);
            long s0 = Fx(bs + 6).Raw, x0 = sim.State.Bull.X;
            long runSpeed = Fx(6.5).Raw;
            int started = -1, tossed = -1, events = 0;
            for (int t = 0; t < 120 && tossed < 0; t++)
            {
                // толпа в 15–17 м впереди: отставание от медианы ≤ anchorLag (F-102 §3.1), бык не прорывается на 9.5, а недостижимых целей рывок не берёт (F-102 §3.2)
                for (int i = 1; i < 4; i++) sim.State.Runners[i].S = sim.State.Bull.S + Fx(14 + i).Raw;
                if (started < 0)
                {
                    sim.State.Runners[0].S = s0; sim.State.Runners[0].X = x0; sim.State.Runners[0].Speed = stand ? 0 : runSpeed;
                    sim.State.Charge.Cooldown = 1;
                }
                else if (stand) { sim.State.Runners[0].S = s0; sim.State.Runners[0].X = x0; sim.State.Runners[0].Speed = 0; }
                int since = started < 0 ? -1 : sim.Tick - started;
                bool react = since >= reaction;
                int steer = react && sidestep && since < reaction + stepTicks ? 1 : 0; // 12 тиков × 3 м/с ≈ 1.2 м
                int tempo = react && sprint ? 1 : 0;
                sim.Step(TickInputs.Empty.With(0, new PlayerInput(steer, tempo, false)));
                events += sim.Events.CountOf(Game.Core.Events.RaceEventType.BullCharge);
                if (started < 0 && sim.State.Charge.Count > 0) started = sim.Tick - 1;
                if (sim.State.Runners[0].Toss.Tossed > 0) tossed = sim.Tick;
            }
            return (sim, tossed, sim.State.Runners[0].Toss.LastCause, events);
        }

        [Fact]
        public void DummyAt6m_SidestepAndSprint_EscapesTheCharge()
        {
            for (ulong seed = 4; seed < 9; seed++)
            {
                var (sim, tossed, _, events) = RunDodge(seed, stand: false, sidestep: true, sprint: true, reaction: 9);
                Assert.Equal(-1, tossed);
                Assert.Equal(1, events);
                Assert.True(sim.State.Runners[0].S >= sim.State.Bull.S, "бегун позади быка");
                Assert.Equal(0, sim.State.Charge.ChargeLeft); // рывок кончился промахом
            }
        }

        [Fact(Skip = "F-081: на живом рывке 11 м/с шаг вбок 1.2 м не уводит из-под рогов (бык доворачивает 1 м/с) — таблицу F-100 §3.2 правит геймдизайнер, тест возвращает F-068 (решение Nick по вопросу F-081)")]
        public void DummyAt6m_SidestepOnly_EscapesTheCharge()
        {
            var (_, tossed, _, _) = RunDodge(4, stand: false, sidestep: true, sprint: false, reaction: 9);
            Assert.Equal(-1, tossed);
        }

        [Fact]
        public void DummyAt6m_StandingStill_IsTossedByTheCharge()
        {
            for (ulong seed = 4; seed < 9; seed++)
            {
                var (sim, tossed, cause, events) = RunDodge(seed, stand: true, sidestep: false, sprint: false, reaction: 0);
                Assert.True(tossed > 0, "стоящий манекен не подброшен");
                Assert.Equal(TossCause.Charge, cause);
                Assert.Equal(1, events);
            }
        }

        [Fact]
        public void MissEndsChargeAndBullDoesNotPassTheRunner()
        {
            var (sim, tossed, _, _) = RunDodge(4, stand: false, sidestep: true, sprint: true, reaction: 9);
            Assert.Equal(-1, tossed);
            for (int t = 0; t < 120; t++)
            {
                for (int i = 1; i < 4; i++) sim.State.Runners[i].S = sim.State.Bull.S + Fx(14 + i).Raw;
                sim.Step(TickInputs.Empty.With(0, new PlayerInput(0, 1, false)));
                Assert.True(sim.State.Runners[0].S >= sim.State.Bull.S);
                Assert.Equal(0, sim.State.Runners[0].Toss.Tossed);
            }
        }

        [Fact(Skip = "F-081: на живом рывке событий рывка в тесте ≤ 5 — F-068 подбирает ручки bull.json и возвращает тест (решение Nick по вопросу F-081)")]
        public void BullChargeEvent_OnePerCharge_SeatIsTarget_BotsStillSeeChargeState()
        {
            var sim = new Simulation(Cfg(12, SeatKind.Bot, Glamour, Athlete, Grandma, Nerd), Data).Running();
            int events = 0;
            for (int t = 0; t < 4500; t++)
            {
                sim.Step(TickInputs.Empty);
                int n = sim.Events.CountOf(Game.Core.Events.RaceEventType.BullCharge);
                Assert.InRange(n, 0, 1);
                if (n == 0) continue;
                events++;
                for (int k = 0; k < sim.Events.Count; k++)
                    if (sim.Events[k].Type == Game.Core.Events.RaceEventType.BullCharge)
                    {
                        Assert.Equal(sim.State.Charge.LastChargeTarget, sim.Events[k].Seat);
                        Assert.Equal(sim.Tick - 1, sim.Events[k].Tick);
                        Assert.Equal(sim.State.Charge.Count, events);
                    }
            }
            Assert.True(events > 5);
            Assert.Equal(sim.State.Charge.Count, events);
        }

        [Fact]
        public void ChargeNumbers_FromF100Section4()
        {
            Assert.Equal(2, Data.BullData.ChargeSec);
            Assert.InRange((double)Data.BullData.ChargeSteer, 0, 1);
            Assert.Equal(1.0, (double)Data.BullData.ChargeMissNoSteerSec);
            Assert.Equal(2.0, (double)Data.BullData.ChargeMissSlowSec);
        }

        [Fact(Skip = "F-081: на живом рывке рывков за забег 8–25 (<18 на части сидов) — F-068 подбирает ручки bull.json и возвращает 18–30 (решение Nick по вопросу F-081)")]
        public void ChargesIn5Minutes_Between18And30()
        {
            var sb = new StringBuilder();
            int bad = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                // боты в составе плейтеста (F-100 §3.2 считан по ним): случайный ввод людей подбрасывает контактом и оставляет быка без целей
                var sim = new Simulation(Game.Sim.RaceRecorder.MakeConfig(seed, Data), Data).Running();
                var rng = new DetRandom(seed);
                int ticks = 0;
                // забег закрыт (F-020) — рывков больше нет, как и после входа быка на арену
                while (ticks < 9000 && sim.State.Bull.Phase != (int)BullPhase.Arena && sim.State.Phase != Game.Core.Race.RacePhase.Over) { sim.Step(RandomInputs(ref rng)); ticks++; }
                int n = sim.State.Charge.Count;
                // нормируем на 9000 тиков, если до арены дошли раньше
                double per5 = n * 9000.0 / System.Math.Max(ticks, 1);
                sb.AppendLine($"seed {seed}: ticks={ticks} charges={n} per5min={per5:F1} tosses={sim.State.Charge.Tosses}");
                // F-100 §3.2: «Рывков ≈ 20–25 за забег (тест — 18–30)» (F-071 временно держала нижнюю границу 2, F-072 вернула)
                if (per5 < 18 || per5 > 30) bad++;
            }
            _out.WriteLine(sb.ToString());
            Assert.True(bad == 0, sb.ToString());
        }
    }
}
