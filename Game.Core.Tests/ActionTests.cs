using Game.Core.Math;
using Game.Core.Runners;
using Game.Core.Street;
using Xunit;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Tests
{
    public class ActionTests
    {
        static readonly GameData Data = GameData.Load();
        const int Nerd = 3, Grandma = 2;

        static RaceConfig Cfg(int a, int b, int c, int d) => new RaceConfig(7, new[]
        {
            new SeatConfig(SeatKind.Human, a), new SeatConfig(SeatKind.Human, b),
            new SeatConfig(SeatKind.Human, c), new SeatConfig(SeatKind.Human, d),
        });

        static double Dbl(Fixed f) => (double)f;
        static Fixed Fx(double v) => Fixed.FromRaw((long)(v * Fixed.OneRaw));

        static readonly TickInputs Push = TickInputs.Empty.With(0, new PlayerInput(0, 0, true));

        /// <summary>Место seat — на (s, x) с заданной скоростью; остальные места убираем далеко вперёд.</summary>
        static void Place(Simulation sim, int seat, double s, double x, double speed = 7)
        {
            ref RunnerState r = ref sim.State.Runners[seat];
            r.S = Fx(s).Raw; r.X = Fx(x).Raw; r.Speed = Fx(speed).Raw; r.StunTicks = 0;
        }

        static void Park(Simulation sim, double s0, params int[] seats)
        {
            double s = s0 + 300;
            foreach (int k in seats) { Place(sim, k, s, 0); s += 20; }
        }

        static double BaseS(Simulation sim) => Dbl(sim.Street.Blocks[0].Start) + 30;

        /// <summary>Толкающий (место 0) на x=pushX, цель (место 1) на dx правее него.</summary>
        static Simulation Setup(int pusherChar, int targetChar, double dx, double ds = 0, double pushX = -2.4)
        {
            var sim = new Simulation(Cfg(pusherChar, targetChar, Nerd, Nerd), Data).Running();
            double s = BaseS(sim);
            Place(sim, 0, s, pushX);
            Place(sim, 1, s + ds, pushX + dx);
            Park(sim, s, 2, 3);
            return sim;
        }

        [Fact]
        public void DataMatchesDesign()
        {
            var a = Data.ActionData;
            Assert.Equal(1.5, Dbl(a.ShoveRange), 3);
            Assert.Equal(2.0, Dbl(a.ShoveCooldown), 3);
            Assert.Equal(1.2, Dbl(a.TripRange), 3);
            Assert.Equal(4.0, Dbl(a.TripCooldown), 3);
            Assert.Equal(0.6, Dbl(a.StairsPushBack), 3);
        }

        [Fact]
        public void ShoveMovesAndSlowsTarget()
        {
            var sim = Setup(Nerd, Nerd, 1.2);
            double x0 = Dbl(Fixed.FromRaw(sim.State.Runners[1].X));
            sim.Step(Push);
            ref RunnerState t = ref sim.State.Runners[1];
            Assert.Equal(x0 + 2.0 / 0.9, Dbl(Fixed.FromRaw(t.X)), 1);
            Assert.Equal(7 * 0.75, Dbl(Fixed.FromRaw(t.Speed)), 1);
            Assert.Equal(1, sim.State.Runners[0].Action.Shoved);
            Assert.Equal(1, sim.State.Runners[1].Action.GotShoved);
            Assert.Equal(1, sim.State.Runners[0].Action.LastShoveTarget);
            Assert.Equal(0, sim.State.Runners[1].Action.LastShovedBy);
            Assert.Equal(0, sim.State.Runners[0].Action.LastShoveTick);
        }

        [Fact]
        public void NoActionWithoutInputOrOutOfRange()
        {
            var sim = Setup(Nerd, Nerd, 1.2);
            sim.Step(TickInputs.Empty);
            Assert.Equal(0, sim.State.Runners[0].Action.Shoved);
            sim = Setup(Nerd, Nerd, 1.6);
            sim.Step(Push);
            Assert.Equal(0, sim.State.Runners[0].Action.Shoved);
        }

        [Fact]
        public void CooldownHoldsShoves()
        {
            var sim = Setup(Nerd, Nerd, 1.2);
            for (int i = 0; i < 60; i++)
            {
                Place(sim, 0, Dbl(Fixed.FromRaw(sim.State.Runners[0].S)), -2.4);
                Place(sim, 1, Dbl(Fixed.FromRaw(sim.State.Runners[0].S)), -1.2);
                sim.Step(Push);
            }
            Assert.Equal(1, sim.State.Runners[0].Action.Shoved); // 2 с = 60 тиков: второй толчок ещё не разрешён
            Place(sim, 0, Dbl(Fixed.FromRaw(sim.State.Runners[0].S)), -2.4);
            Place(sim, 1, Dbl(Fixed.FromRaw(sim.State.Runners[0].S)), -1.2);
            sim.Step(Push);
            Assert.Equal(2, sim.State.Runners[0].Action.Shoved);
        }

        [Fact]
        public void TripStunsTargetWithinRange()
        {
            var sim = Setup(Nerd, Nerd, 0, ds: 1.1, pushX: 0);
            sim.Step(Push);
            ref RunnerState t = ref sim.State.Runners[1];
            Assert.True(t.StunTicks > 0);
            Assert.Equal(Collisions.StunTicks(Data.ActionData.TripStun, Fx(0.9)), t.StunTicks);
            Assert.Equal(0, t.Speed);
            Assert.Equal(1, sim.State.Runners[0].Action.Tripped);
            Assert.Equal(1, sim.State.Runners[1].Action.GotTripped);
            Assert.Equal(0, sim.State.Runners[1].Action.LastTrippedBy);
            Assert.Equal(1, sim.State.Runners[0].Action.LastTripTarget);
        }

        [Fact]
        public void TripDoesNotReachBeyond1_2m()
        {
            var sim = Setup(Nerd, Nerd, 0, ds: 1.3, pushX: 0);
            sim.Step(Push);
            Assert.Equal(0, sim.State.Runners[0].Action.Tripped);
            Assert.Equal(0, sim.State.Runners[1].StunTicks);
        }

        [Fact]
        public void TripHasPriorityOverShoveAndCooldownIs4s()
        {
            var sim = Setup(Nerd, Nerd, 0, ds: 1.1, pushX: 0);
            double s = Dbl(Fixed.FromRaw(sim.State.Runners[0].S));
            Place(sim, 2, s, 1.2); // сосед сбоку
            sim.Step(Push);
            Assert.Equal(1, sim.State.Runners[0].Action.Tripped);
            Assert.Equal(0, sim.State.Runners[0].Action.Shoved);
            Assert.Equal(0, sim.State.Runners[2].Action.GotShoved);
            // Подножка на перезарядке (120 тиков) — тот же вход толкает соседа сбоку.
            Place(sim, 1, s + 1.1, 0);
            sim.Step(Push);
            Assert.Equal(1, sim.State.Runners[0].Action.Tripped);
            Assert.Equal(1, sim.State.Runners[0].Action.Shoved);
            for (int i = 0; i < 118; i++) sim.Step(TickInputs.Empty);
            Place(sim, 0, s, 0); Place(sim, 1, s + 1.1, 0); Park(sim, s, 2);
            sim.Step(Push);
            Assert.Equal(2, sim.State.Runners[0].Action.Tripped);
        }

        [Fact]
        public void GrandmaReaches2_5mAndShovesLikeEveryone()
        {
            var normal = Setup(Nerd, Nerd, 2.0);
            normal.Step(Push);
            Assert.Equal(0, normal.State.Runners[0].Action.Shoved);

            var gran = Setup(Grandma, Nerd, 2.0);
            double x0 = Dbl(Fixed.FromRaw(gran.State.Runners[1].X));
            gran.Step(Push);
            Assert.Equal(1, gran.State.Runners[0].Action.Shoved);
            Assert.Equal(x0 + 2.0 / 0.9, Dbl(Fixed.FromRaw(gran.State.Runners[1].X)), 1);

            var ref1 = Setup(Nerd, Nerd, 1.2);
            double rx0 = Dbl(Fixed.FromRaw(ref1.State.Runners[1].X));
            ref1.Step(Push);
            double ref1Move = Dbl(Fixed.FromRaw(ref1.State.Runners[1].X)) - rx0;
            Assert.Equal(ref1Move, Dbl(Fixed.FromRaw(gran.State.Runners[1].X)) - x0, 1);

            var far = Setup(Grandma, Nerd, 2.7);
            far.Step(Push);
            Assert.Equal(0, far.State.Runners[0].Action.Shoved);
        }

        [Fact]
        public void StairsPushTargetBack()
        {
            StreetMap? street = null;
            PlacedBlock stairs = default;
            for (ulong seed = 1; seed < 400 && street == null; seed++)
            {
                var c = StreetGenerator.Build(seed, Data.StreetData, StreetPool.CorePlusLater);
                foreach (var pb in c.Blocks) if (pb.Def.Id == "stairs") { street = c; stairs = pb; break; }
            }
            Assert.NotNull(street);
            double s = Dbl(stairs.Start) + 20;
            foreach (bool trip in new[] { false, true })
            {
                var sim = new Simulation(Cfg(Nerd, Nerd, Nerd, Nerd), Data, street).Running();
                Place(sim, 0, s, trip ? 0 : -2.4);
                Place(sim, 1, s + (trip ? 1.1 : 0), trip ? 0 : -1.2);
                Park(sim, s, 2, 3);
                double before = Dbl(Fixed.FromRaw(sim.State.Runners[1].S));
                sim.Step(Push);
                Assert.Equal(1, sim.State.Runners[0].Action.Shoved + sim.State.Runners[0].Action.Tripped);
                double after = Dbl(Fixed.FromRaw(sim.State.Runners[1].S));
                Assert.True(after < before - 0.3, $"trip={trip} before={before} after={after}");
            }
        }

        [Fact]
        public void DeterministicWithActions()
        {
            var a = new Simulation(Cfg(0, 1, 2, 3), Data);
            var b = new Simulation(Cfg(0, 1, 2, 3), Data);
            uint x = 99;
            int acts = 0;
            for (int t = 0; t < 2500; t++)
            {
                var ti = TickInputs.Empty;
                for (int s = 0; s < 4; s++)
                {
                    x = x * 1664525u + 1013904223u;
                    ti = ti.With(s, new PlayerInput((int)((x >> 8) % 3) - 1, (int)((x >> 12) % 3) - 1, ((x >> 20) & 3) == 0));
                }
                a.Step(ti); b.Step(ti);
                Assert.Equal(a.StateHash(), b.StateHash());
            }
            for (int i = 0; i < 4; i++) acts += a.State.Runners[i].Action.Shoved + a.State.Runners[i].Action.Tripped;
            Assert.True(acts >= 0);
            var c = a.Clone();
            Assert.Equal(a.StateHash(), c.StateHash());
        }
    }
}
