using System;
using Game.Core.Math;
using Game.Core.Runners;
using Game.Core.Street;
using Xunit;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Tests
{
    using Math = System.Math;

    public class RunnerMovementTests
    {
        static readonly RunnerData D = RunnerData.Load();
        static readonly StreetMap Flat = StreetGenerator.Build(1, StreetData.Load(), StreetPool.Core);

        static RaceConfig Cfg(ulong seed) => new RaceConfig(seed, new[]
        {
            new SeatConfig(SeatKind.Human, 0), new SeatConfig(SeatKind.Human, 1),
            new SeatConfig(SeatKind.Human, 2), new SeatConfig(SeatKind.Bot, 3, 1),
        });

        static RunnerState Fresh(double s = 5, double x = 0)
        {
            var r = new RunnerState { S = Fixed.FromFraction((int)(s * 100), 100).Raw, X = Fixed.FromFraction((int)(x * 100), 100).Raw };
            RunnerMovement.InitRunner(D, ref r);
            return r;
        }

        static double Dbl(long raw) => raw / (double)Fixed.OneRaw;

        [Fact]
        public void DataMatchesDesign()
        {
            Assert.Equal(5.5, (double)D.JogSpeed);
            Assert.Equal(7.0, (double)D.RunSpeed);
            Assert.Equal(8.5, (double)D.SprintSpeed);
            Assert.Equal(100, (double)D.StaminaMax);
            Assert.Equal(25, (double)D.SprintDrain);
            Assert.Equal(20, (double)D.StaminaRegen);
            Assert.Equal(6, (double)D.Accel);
            Assert.Equal(3, (double)D.SteerSpeed);
            Assert.Equal(0.4, (double)D.Radius, 3);
        }

        [Fact]
        public void AcceleratesToCruiseInExpectedTime()
        {
            var r = Fresh();
            int ticks = 0;
            while (r.Speed < D.RunSpeed.Raw && ticks < 300)
            {
                RunnerMovement.StepRunner(D, Flat, ref r, default);
                ticks++;
            }
            // 7 / 6 ≈ 1.17 с ≈ 35 тиков
            Assert.InRange(ticks, 34, 36);
            Assert.Equal(7.0, Dbl(r.Speed), 3);
        }

        [Fact]
        public void SprintDrainsStaminaInAbout4Seconds_AndRegensIn5()
        {
            var r = Fresh();
            int ticks = 0;
            while (r.Stamina > 0 && ticks < 600)
            {
                RunnerMovement.StepRunner(D, Flat, ref r, new PlayerInput(0, 1, false));
                ticks++;
            }
            Assert.InRange(ticks, 118, 122); // 4 с
            Assert.Equal(1, r.Exhausted);

            ticks = 0;
            while (r.Stamina < D.StaminaMax.Raw && ticks < 600)
            {
                RunnerMovement.StepRunner(D, Flat, ref r, default);
                ticks++;
            }
            Assert.InRange(ticks, 148, 152); // 5 с
        }

        [Fact]
        public void ExhaustedSprintFallsBackToRun()
        {
            var r = Fresh();
            int guard = 0;
            while (r.Exhausted == 0 && guard++ < 300) RunnerMovement.StepRunner(D, Flat, ref r, new PlayerInput(0, 1, false));
            Assert.Equal(1, r.Exhausted);
            double before = Dbl(r.Speed);
            for (int i = 0; i < 20; i++) RunnerMovement.StepRunner(D, Flat, ref r, new PlayerInput(0, 1, false));
            // спринт заблокирован (выносливость < порога): темп падает к бегу
            Assert.Equal(1, r.Exhausted);
            Assert.True(Dbl(r.Speed) < before);
        }

        static double CruiseOnSlope(Slope slope)
        {
            for (ulong seed = 1; seed < 500; seed++)
            {
                var st = StreetGenerator.Build(seed, StreetData.Load(), StreetPool.Core);
                for (int b = 0; b < st.Blocks.Length; b++)
                {
                    if (st.Blocks[b].Def.Slope != slope) continue;
                    var r = Fresh();
                    r.S = (st.Blocks[b].Start + Fixed.FromInt(2)).Raw;
                    r.Speed = D.RunSpeed.Raw;
                    for (int i = 0; i < 30; i++) RunnerMovement.StepRunner(D, st, ref r, default);
                    Assert.True(Fixed.FromRaw(r.S) < st.Blocks[b].End);
                    return Dbl(r.Speed);
                }
            }
            throw new Exception("нет блока " + slope);
        }

        [Fact]
        public void DownhillIs20PercentFasterThanUphill()
        {
            double down = CruiseOnSlope(Slope.Down), up = CruiseOnSlope(Slope.Up), flat = (double)D.RunSpeed;
            Assert.Equal(0.20, (down - up) / flat, 2);
            Assert.True(down > flat && up < flat);
        }

        [Fact]
        public void CannotLeaveThroughWall()
        {
            foreach (int steer in new[] { -1, 1 })
            {
                var r = Fresh(5, 0);
                for (int i = 0; i < 300; i++)
                {
                    RunnerMovement.StepRunner(D, Flat, ref r, new PlayerInput(steer, 0, false));
                    double lim = (double)Flat.WidthAt(Fixed.FromRaw(r.S)) / 2 - 0.4;
                    Assert.True(Math.Abs(Dbl(r.X)) <= lim + 1e-3);
                }
                Assert.True(Math.Abs(Dbl(r.X)) > 3.0); // дошёл до стены (ширина 8 → предел 3.6)
            }
        }

        [Fact]
        public void SteeringSpeedIs3MetersPerSecond()
        {
            var r = Fresh(5, 0);
            for (int i = 0; i < 30; i++) RunnerMovement.StepRunner(D, Flat, ref r, new PlayerInput(1, 0, false));
            Assert.Equal(3.0, Dbl(r.X), 1);
        }

        [Fact]
        public void RunnersPushedApartAndDoNotOverlap()
        {
            var rs = new RunnerState[4];
            for (int i = 0; i < 4; i++) { rs[i] = Fresh(10, 0); }
            for (int t = 0; t < 120; t++) RunnerMovement.Separate(D, Flat, rs);
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                {
                    double dx = Dbl(rs[j].X - rs[i].X), ds = Dbl(rs[j].S - rs[i].S);
                    Assert.True(Math.Sqrt(dx * dx + ds * ds) > 0.75, $"{i},{j}");
                }
        }

        [Fact]
        public void FasterRunnerDoesNotPassThroughSlowOneInSameLane()
        {
            var sim = new Simulation(Cfg(3));
            sim.State.Runners[0].S = Fixed.FromInt(10).Raw;
            sim.State.Runners[1].S = Fixed.FromInt(12).Raw;
            sim.State.Runners[2].S = Fixed.FromInt(40).Raw;
            sim.State.Runners[3].S = Fixed.FromInt(41).Raw;
            var inp = TickInputs.Empty.With(0, new PlayerInput(0, 1, false)).With(1, new PlayerInput(0, -1, false));
            for (int t = 0; t < 150; t++)
            {
                sim.Step(inp);
                var a = sim.State.Runners[0]; var b = sim.State.Runners[1];
                double dx = Dbl(b.X - a.X), ds = Dbl(b.S - a.S);
                Assert.True(Math.Sqrt(dx * dx + ds * ds) > 0.7, "tick " + t);
            }
        }

        static ulong RunHash(ulong seed)
        {
            var sim = new Simulation(Cfg(seed));
            uint x = 12345u;
            ulong h = 0;
            for (int t = 0; t < 3000; t++)
            {
                var ti = TickInputs.Empty;
                for (int s = 0; s < 4; s++)
                {
                    x = x * 1664525u + 1013904223u;
                    ti = ti.With(s, new PlayerInput((int)((x >> 8) % 3) - 1, (int)((x >> 12) % 3) - 1, ((x >> 20) & 1) != 0));
                }
                sim.Step(ti);
                h = h * 31 + sim.StateHash();
            }
            return h;
        }

        [Fact]
        public void Deterministic3000TicksRandomInputs() => Assert.Equal(RunHash(9), RunHash(9));

        [Fact]
        public void CloneStepMatchesDirectRun()
        {
            var a = new Simulation(Cfg(5));
            for (int t = 0; t < 100; t++) a.Step(TickInputs.Empty.With(0, new PlayerInput(1, 1, false)));
            var b = a.Clone();
            a.Step(TickInputs.Empty); b.Step(TickInputs.Empty);
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }
}
