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
            Assert.True(first >= Release + 8 * 30 - 1, "первый рывок слишком рано: " + first);
            Assert.True(first <= Release + 15 * 30 + 60, "первый рывок слишком поздно: " + first);
        }

        [Fact]
        public void IntervalsBetweenChargesAre8To15Seconds()
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
                        Assert.InRange(gap, 8 * 30, 15 * 30 + 90); // +3 с: цель может не найтись сразу
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
            ref var t1 = ref sim.State.Tosses[1];
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
            while (sim.State.Tosses[1].NoSignalTicks > 0) sim.Step(TickInputs.Empty);
            Assert.Equal(1, sim.State.Tosses[1].Rose);
            Assert.Equal(sim.State.Tick - 1, sim.State.Tosses[1].LastRoseTick);
            Assert.True(sim.State.Tosses[1].InvulnTicks > 0);
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
                Assert.Equal((int)(sec * 30), sim.State.Tosses[2].NoSignalTicks);
                Assert.True(sim.State.Tosses[2].NoSignalTicks + sim.State.Tosses[2].InvulnTicks >= 180);
            }
        }

        [Fact]
        public void InvulnerableRunnerIsNotTargetedNorTossed_BullDoesNotPassHim()
        {
            var sim = new Simulation(Cfg(4, SeatKind.Human), Data);
            RunTo(sim, Release + 20);
            double bs = Dbl(sim.State.Bull.S);
            ref var t = ref sim.State.Tosses[0];
            t.InvulnTicks = 200;
            sim.State.Runners[0].S = Fx(bs + 0.3).Raw; sim.State.Runners[0].X = sim.State.Bull.X; sim.State.Runners[0].Speed = 0;
            for (int i = 1; i < 4; i++) sim.State.Runners[i].S = Fx(bs + 400 + i * 10).Raw;
            for (int k = 0; k < 60; k++)
            {
                sim.State.Runners[0].S = Fx(bs + 0.3).Raw; // стоит
                sim.Step(TickInputs.Empty);
                Assert.Equal(0, sim.State.Tosses[0].Tossed);
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
                RunTo(sim, Release - 1);
                double bs = Dbl(sim.State.Bull.S) + 40;
                for (int tries = 0; tries < 2000 && sim.State.Charge.Count == 0; tries++)
                {
                    bs = Dbl(sim.State.Bull.S);
                    sim.State.Runners[0].S = Fx(bs + 10).Raw; sim.State.Runners[0].X = 0;
                    sim.State.Runners[1].S = Fx(bs + 10).Raw; sim.State.Runners[1].X = 0;
                    sim.State.Runners[2].S = Fx(bs + 100).Raw; sim.State.Runners[3].S = Fx(bs + 110).Raw;
                    sim.Step(TickInputs.Empty);
                }
                Assert.Equal(1, sim.State.Charge.Count);
                if (sim.State.Charge.LastChargeTarget == 0) athlete++;
                else if (sim.State.Charge.LastChargeTarget == 1) grandma++;
            }
            _out.WriteLine($"athlete={athlete} grandma={grandma}");
            Assert.True(athlete > grandma * 2, $"athlete={athlete} grandma={grandma}");
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
        public void ChargeSpeedIsAbout11AndLasts3Seconds()
        {
            var sim = new Simulation(Cfg(21, SeatKind.Human), Data);
            RunTo(sim, Release);
            while (sim.State.Charge.ChargeLeft == 0) sim.Step(TickInputs.Empty);
            // держим бегунов вне досягаемости, смотрим скорость
            long maxSpeed = 0; int ticks = 0;
            while (sim.State.Charge.ChargeLeft > 0 && ticks < 200)
            {
                double bs = Dbl(sim.State.Bull.S);
                for (int i = 0; i < 4; i++) sim.State.Runners[i].S = Fx(bs + 25 + i).Raw;
                sim.Step(TickInputs.Empty);
                if (sim.State.Bull.Speed > maxSpeed) maxSpeed = sim.State.Bull.Speed;
                ticks++;
            }
            _out.WriteLine($"ticks={ticks} maxSpeed={Dbl(maxSpeed)}");
            Assert.InRange(ticks, 85, 91);
            Assert.InRange(Dbl(maxSpeed), 9.5, 11.01);
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
                        if (sim.State.Tosses[i].Tossed != lastCount[i])
                        {
                            int gap = sim.Tick - lastToss[i];
                            if (gap < 180) Assert.Fail($"seed {seed}: бегун {i} подброшен дважды за {gap} тиков");
                            lastToss[i] = sim.Tick; lastCount[i] = sim.State.Tosses[i].Tossed; totalTosses++;
                        }
                    }
                }
            }
            _out.WriteLine("tosses=" + totalTosses);
            Assert.True(totalTosses > 100, "подбросов подозрительно мало: " + totalTosses);
        }

        [Fact]
        public void ChargesIn5Minutes_Between20And30()
        {
            var sb = new StringBuilder();
            int bad = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var sim = new Simulation(Cfg(seed, SeatKind.Human, (int)(seed % 4), Nerd, Athlete, Glamour), Data);
                var rng = new DetRandom(seed);
                int ticks = 0;
                while (ticks < 9000 && sim.State.Bull.Phase != (int)BullPhase.Arena) { sim.Step(RandomInputs(ref rng)); ticks++; }
                int n = sim.State.Charge.Count;
                // нормируем на 9000 тиков, если до арены дошли раньше
                double per5 = n * 9000.0 / System.Math.Max(ticks, 1);
                sb.AppendLine($"seed {seed}: ticks={ticks} charges={n} per5min={per5:F1} tosses={sim.State.Charge.Tosses}");
                if (per5 < 20 || per5 > 30) bad++;
            }
            _out.WriteLine(sb.ToString());
            Assert.True(bad == 0, sb.ToString());
        }
    }
}
