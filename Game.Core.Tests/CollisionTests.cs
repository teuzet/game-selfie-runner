using System;
using Game.Core.Math;
using Game.Core.Runners;
using Game.Core.Street;
using Xunit;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Tests
{
    public class CollisionTests
    {
        static readonly StreetData Data = StreetData.Load();

        static RaceConfig Cfg(ulong seed) => new RaceConfig(seed, new[]
        {
            new SeatConfig(SeatKind.Human, 0), new SeatConfig(SeatKind.Human, 1),
            new SeatConfig(SeatKind.Human, 2), new SeatConfig(SeatKind.Bot, 3, 1),
        });

        /// <summary>Первая улица (пул core либо core+later) из первых 200 сидов, где есть препятствие нужного типа.</summary>
        static (Simulation sim, int idx) Find(string type, StreetPool pool = StreetPool.Core, Func<StreetObstacle, bool>? pred = null)
        {
            for (ulong seed = 1; seed < 200; seed++)
            {
                var street = StreetGenerator.Build(seed, Data, pool);
                for (int i = 0; i < street.Obstacles.Length; i++)
                    if (street.Obstacles[i].Type == type && (pred == null || pred(street.Obstacles[i])))
                        return (new Simulation(Cfg(seed), street), i);
            }
            throw new InvalidOperationException("нет препятствия " + type);
        }

        static void Place(Simulation sim, double s, Fixed x, bool atSpeed = true)
        {
            ref var r = ref sim.State.Runners[0];
            r.S = Fixed.FromRaw((long)(s * Fixed.OneRaw)).Raw;
            r.X = x.Raw;
            if (atSpeed) r.Speed = sim.RunnerData.RunSpeed.Raw;
        }

        /// <summary>Идти бегуном 0 до первого столкновения (не больше max тиков).</summary>
        static int RunUntilHit(Simulation sim, int max = 120)
        {
            for (int t = 0; t < max; t++)
            {
                sim.Step(default);
                if (sim.State.Collisions[0].Count > 0) return t;
            }
            return -1;
        }

        static double Dbl(Fixed f) => (double)f;

        [Fact]
        public void PoleStunsPointSixSecondsAtStability1()
        {
            var (sim, i) = Find("pole");
            var o = sim.Street.Obstacles[i];
            Place(sim, Dbl(o.S) - 1.0, o.X);
            Assert.True(RunUntilHit(sim) >= 0);
            ref var r = ref sim.State.Runners[0];
            Assert.Equal(18, r.StunTicks); // 0.6 с × 30
            Assert.Equal(0, r.Speed);
            Assert.Equal((int)CollisionKind.Pole, sim.State.Collisions[0].LastKind);
        }

        [Fact]
        public void PoleStunsShorterWithHigherStability()
        {
            var (sim, i) = Find("pole");
            var o = sim.Street.Obstacles[i];
            sim.State.Stability[0] = Fixed.FromFraction(8, 5).Raw; // 1.6
            Place(sim, Dbl(o.S) - 1.0, o.X);
            Assert.True(RunUntilHit(sim) >= 0);
            int ticks = sim.State.Runners[0].StunTicks;
            Assert.InRange(ticks, 11, 11); // 0.375 с × 30 = 11.25 → 11
            Assert.InRange(ticks / 30.0, 0.375 - 1.0 / 30, 0.375 + 1.0 / 30);
        }

        [Fact]
        public void StunThenAccelerates()
        {
            var (sim, i) = Find("pole");
            var o = sim.Street.Obstacles[i];
            Place(sim, Dbl(o.S) - 1.0, o.X);
            RunUntilHit(sim);
            for (int t = 0; t < 18; t++) { sim.Step(default); }
            Assert.Equal(0, sim.State.Runners[0].StunTicks);
            long v0 = sim.State.Runners[0].Speed;
            for (int t = 0; t < 10; t++) sim.Step(default);
            Assert.True(sim.State.Runners[0].Speed > v0);
        }

        [Fact]
        public void SameObstacleHitsOnlyOnce()
        {
            var (sim, i) = Find("pole");
            var o = sim.Street.Obstacles[i];
            Place(sim, Dbl(o.S) - 1.0, o.X);
            RunUntilHit(sim);
            // пройти сквозь столб после стана
            for (int t = 0; t < 60; t++) sim.Step(default);
            Assert.True(sim.State.Runners[0].S > o.S.Raw + Fixed.OneRaw / 2);
            Assert.Equal(1, sim.State.Collisions[0].Count);
        }

        [Fact]
        public void PotsStunPointThree()
        {
            var (sim, i) = Find("pots");
            var o = sim.Street.Obstacles[i];
            Place(sim, Dbl(o.S) - 1.0, o.X);
            Assert.True(RunUntilHit(sim) >= 0);
            Assert.Equal(9, sim.State.Runners[0].StunTicks);
        }

        [Fact]
        public void LaundrySlowsWithoutStun()
        {
            var (sim, i) = Find("laundry");
            var o = sim.Street.Obstacles[i];
            Place(sim, Dbl(o.S) - 1.0, Fixed.Zero);
            double prev = 0;
            bool hit = false;
            for (int t = 0; t < 120 && !hit; t++)
            {
                prev = Dbl(Fixed.FromRaw(sim.State.Runners[0].Speed));
                sim.Step(default);
                hit = sim.State.Collisions[0].Count > 0;
            }
            Assert.True(hit);
            ref var r = ref sim.State.Runners[0];
            Assert.Equal(0, r.StunTicks);
            Assert.Equal((int)CollisionKind.Laundry, sim.State.Collisions[0].LastKind);
            // ×0.6 от скорости в момент столкновения (в тик разгон/торможение ≤ 0.2 м/с)
            double v = Dbl(Fixed.FromRaw(r.Speed));
            Assert.InRange(v, 0.6 * (prev - 0.21), 0.6 * (prev + 0.21));
            // держится ~1 с, затем разгон
            for (int t = 0; t < 25; t++) sim.Step(default);
            Assert.True(Dbl(Fixed.FromRaw(sim.State.Runners[0].Speed)) <= v + 0.01);
            for (int t = 0; t < 40; t++) sim.Step(default);
            Assert.True(Dbl(Fixed.FromRaw(sim.State.Runners[0].Speed)) > v + 0.5);
            Assert.Equal(1, sim.State.Collisions[0].Count);
        }

        [Fact]
        public void GawkerMovesDeterministicallyAndTurnsAtWall()
        {
            var (a, i) = Find("gawker");
            var b = new Simulation(Cfg(a.Street.Seed), a.Street);
            var o = a.Street.Obstacles[i];
            MovingObstacles.GawkerLimits(a.Street, o, out Fixed lo, out Fixed hi);
            long prev = a.State.Dyn[i].X;
            bool turned = false; int dir0 = a.State.Dyn[i].Dir;
            for (int t = 0; t < 600; t++)
            {
                a.Step(default); b.Step(default);
                Assert.Equal(a.StateHash(), b.StateHash());
                long x = a.State.Dyn[i].X;
                Assert.InRange(x, lo.Raw, hi.Raw);
                if (a.State.Dyn[i].Dir != dir0) turned = true;
                Assert.True(System.Math.Abs(x - prev) <= Fixed.FromFraction(1, 30).Raw + 1);
                prev = x;
            }
            Assert.True(turned);
            // зазор до стены не меньше 0.5 м у края тела
            Fixed w = a.Street.MinWidthOn(o.S - o.SizeAlong * Fixed.Half, o.S + o.SizeAlong * Fixed.Half);
            Assert.True(hi + Fixed.FromFraction(2, 5) + Fixed.FromFraction(1, 2) <= w / Fixed.FromInt(2) + Fixed.FromRaw(4));
        }

        [Fact]
        public void GawkerStunsRunnerAndKeepsWalking()
        {
            var (sim, i) = Find("gawker");
            var o = sim.Street.Obstacles[i];
            Place(sim, Dbl(o.S) - 1.0, Fixed.FromRaw(sim.State.Dyn[i].X));
            long x0 = sim.State.Dyn[i].X;
            Assert.True(RunUntilHit(sim) >= 0);
            Assert.Equal((int)CollisionKind.Gawker, sim.State.Collisions[0].LastKind);
            Assert.Equal(18, sim.State.Runners[0].StunTicks);
            sim.Step(default);
            Assert.NotEqual(x0, sim.State.Dyn[i].X);
        }

        [Fact]
        public void DoorOpensOnApproachAndStuns()
        {
            var (sim, i) = Find("door");
            var o = sim.Street.Obstacles[i];
            Assert.InRange(Dbl(o.Param), 8, 16);
            Assert.Equal(-1, sim.State.Dyn[i].OpenTick);
            Assert.Equal(Fixed.Zero, MovingObstacles.DoorOpenWidth(o, sim.State.Dyn[i], 0));
            // бегун за порогом + 0.5 м от двери: дверь ещё закрыта
            Place(sim, Dbl(o.S) - Dbl(o.Param) - 0.5, o.X, atSpeed: false);
            sim.State.Runners[0].Speed = 0;
            sim.Step(new TickInputs()); // темп 0 — бежит, но шагов мало
            Assert.Equal(-1, sim.State.Dyn[i].OpenTick);
            // подошёл в порог
            Place(sim, Dbl(o.S) - Dbl(o.Param) + 0.5, o.X);
            sim.Step(default);
            int open = sim.State.Dyn[i].OpenTick;
            Assert.True(open >= 0);
            // ширина растёт 0 → 0.8 за 1.5 с
            var dyn = sim.State.Dyn[i];
            Assert.Equal(0.4, Dbl(MovingObstacles.DoorOpenWidth(o, dyn, open + 22)), 1);
            Assert.Equal(0.8, Dbl(MovingObstacles.DoorOpenWidth(o, dyn, open + 45)), 3);
            Assert.Equal(0.8, Dbl(MovingObstacles.DoorOpenWidth(o, dyn, open + 500)), 3);
        }

        [Fact]
        public void RunnerStandingInOpeningDoorIsStunnedSameTick()
        {
            var (sim, i) = Find("door");
            var o = sim.Street.Obstacles[i];
            // бегун стоит у самой стены рядом с дверью (внутри будущей открытой части) и ждёт
            Fixed wallX = o.X >= Fixed.Zero ? o.X + o.SizeAcross * Fixed.Half - Fixed.FromFraction(1, 2)
                                            : o.X - o.SizeAcross * Fixed.Half + Fixed.FromFraction(1, 2);
            Place(sim, Dbl(o.S), wallX, atSpeed: false);
            ref var r = ref sim.State.Runners[0];
            r.StunTicks = 0;
            // открывает дверь бегун 1, подошедший на порог
            sim.State.Runners[1].S = (o.S - o.Param + Fixed.Half).Raw;
            int hitTick = -1;
            for (int t = 0; t < 60 && hitTick < 0; t++)
            {
                // бегун 0 удерживаем на месте
                sim.State.Runners[0].S = o.S.Raw; sim.State.Runners[0].X = wallX.Raw; sim.State.Runners[0].Speed = 0;
                sim.Step(default);
                if (sim.State.Collisions[0].Count > 0) hitTick = t;
            }
            Assert.True(hitTick >= 0);
            Assert.Equal((int)CollisionKind.Door, sim.State.Collisions[0].LastKind);
            Assert.InRange(hitTick, 1, 45);
        }

        [Fact]
        public void WaterStunsFixedTimeThenPlacesAtEdgeSameS()
        {
            var (sim, _) = Find("pole", StreetPool.CorePlusLater, null);
            // ищем улицу с прудом
            StreetMap? street = null; int bi = -1;
            for (ulong seed = 1; seed < 200 && street == null; seed++)
            {
                var st = StreetGenerator.Build(seed, Data, StreetPool.CorePlusLater);
                for (int b = 0; b < st.Blocks.Length; b++)
                    if (st.Blocks[b].WaterSide != 0) { street = st; bi = b; sim = new Simulation(Cfg(seed), st); break; }
            }
            Assert.NotNull(street);
            var pb = street!.Blocks[bi];
            Fixed s = pb.Start + Fixed.FromInt(50);
            Fixed w = street.WidthAt(s);
            Fixed x = Fixed.FromInt(pb.WaterSide) * (w / Fixed.FromInt(2) - Fixed.One); // посреди полосы воды
            sim.State.Stability[0] = Fixed.FromInt(2).Raw; // не должна уменьшать стан воды
            sim.State.Runners[0].S = s.Raw; sim.State.Runners[0].X = x.Raw; sim.State.Runners[0].Speed = 0;
            sim.Step(default);
            ref var c = ref sim.State.Collisions[0];
            Assert.Equal(1, c.Count);
            Assert.Equal(1, c.WaterCount);
            Assert.Equal((int)CollisionKind.Water, c.LastKind);
            Assert.Equal(0, c.LastTick);
            Assert.Equal(45, sim.State.Runners[0].StunTicks); // 1.5 с
            long sHit = sim.State.Runners[0].S;
            for (int t = 0; t < 44; t++) sim.Step(default);
            Assert.Equal(1, sim.State.Runners[0].StunTicks);
            sim.Step(default); // 45-й тик стана: после него — у кромки
            Assert.Equal(0, sim.State.Runners[0].StunTicks);
            ref var r = ref sim.State.Runners[0];
            Assert.Equal(sHit, r.S);
            long sHitRaw = sHit;
            Fixed edge = w / Fixed.FromInt(2) - Fixed.FromInt(3);
            Assert.True(Fixed.Abs(Fixed.FromRaw(r.X)) <= edge);
            Assert.Equal(pb.WaterSide, System.Math.Sign(r.X));
            Assert.Equal(1, c.Count);
        }

        [Fact]
        public void FountainActsLikePole()
        {
            ulong seed = 1; StreetMap? street = null; int bi = -1;
            for (; seed < 200 && street == null; seed++)
            {
                var st = StreetGenerator.Build(seed, Data, StreetPool.Core);
                for (int b = 0; b < st.Blocks.Length; b++)
                    if (st.Blocks[b].Def.Id == "plaza") { street = st; bi = b; break; }
            }
            Assert.NotNull(street);
            var sim = new Simulation(Cfg(seed), street);
            Place(sim, Dbl(street!.Blocks[bi].Start) + 40 - 3.5, Fixed.Zero);
            Assert.True(RunUntilHit(sim) >= 0);
            Assert.Equal((int)CollisionKind.Fountain, sim.State.Collisions[0].LastKind);
            Assert.Equal(18, sim.State.Runners[0].StunTicks);
        }

        [Fact]
        public void FullRunIsDeterministicAndCloneMatches()
        {
            var a = new Simulation(Cfg(7));
            var b = new Simulation(Cfg(7));
            Simulation? c = null;
            for (int t = 0; t < 1500; t++)
            {
                var inp = new TickInputs();
                a.Step(inp); b.Step(inp);
                Assert.Equal(a.StateHash(), b.StateHash());
                if (t == 700) c = a.Clone();
            }
            for (int t = 700 + 1; t < 1500; t++) c!.Step(new TickInputs());
            Assert.Equal(a.StateHash(), c!.StateHash());
            int total = 0;
            for (int i = 0; i < 4; i++) total += a.State.Collisions[i].Count;
            Assert.True(total > 0, "за 50 с бегуны вслепую должны во что-то врезаться");
        }
    }
}
