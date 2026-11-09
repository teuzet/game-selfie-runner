using System;
using Game.Core.Economy;
using Game.Core.Events;
using Game.Core.Math;
using Game.Core.Runners;
using Xunit;

namespace Game.Core.Tests
{
    public class EventsTests
    {
        static readonly GameData Data = GameData.Load();
        const int Glamour = 0, Nerd = 3;

        static RaceConfig Cfg(int a, int b, int c, int d) => new RaceConfig(7, new[]
        {
            new SeatConfig(SeatKind.Human, a), new SeatConfig(SeatKind.Human, b),
            new SeatConfig(SeatKind.Human, c), new SeatConfig(SeatKind.Human, d),
        });

        static Fixed Fx(double v) => Fixed.FromRaw((long)(v * Fixed.OneRaw));
        static long V(int n) => Fixed.FromInt(n).Raw;
        static readonly TickInputs Push = TickInputs.Empty.With(0, new PlayerInput(0, 0, true));

        static void Place(Simulation sim, int seat, double s, double x, double speed = 7)
        {
            ref RunnerState r = ref sim.State.Runners[seat];
            r.S = Fx(s).Raw; r.X = Fx(x).Raw; r.Speed = Fx(speed).Raw; r.StunTicks = 0;
        }

        static double BaseS(Simulation sim) => (double)sim.Street.Blocks[0].Start + 30;

        /// <summary>Все четыре бегуна разведены далеко друг от друга и от быка: ни действий, ни «влез в кадр».</summary>
        static Simulation Quiet(RaceConfig cfg)
        {
            var sim = new Simulation(cfg, Data).Running();
            double s = BaseS(sim);
            for (int i = 0; i < 4; i++) Place(sim, i, s + 40 * i, 0);
            return sim;
        }

        /// <summary>Шаг сима с фактом и контрольной копии без него: разница зрителей/донатов — ровно скачок.</summary>
        static (Simulation sim, Simulation ctl) StepWithFact(Simulation sim, Action<RaceState> fact, TickInputs inp = default)
        {
            var ctl = sim.Clone();
            fact(sim.State);
            sim.Step(inp);
            ctl.Step(inp);
            return (sim, ctl);
        }

        static long DV(Simulation a, Simulation b, int seat) => a.State.Runners[seat].Viewing.Viewers - b.State.Runners[seat].Viewing.Viewers;
        static long DD(Simulation a, Simulation b, int seat) => a.State.Runners[seat].Viewing.Donations - b.State.Runners[seat].Viewing.Donations;

        [Fact]
        public void CollisionGivesOneEventAndPlus40()
        {
            var (sim, ctl) = StepWithFact(Quiet(Cfg(0, 1, 2, 3)), s =>
            {
                s.Runners[1].Collision.Count++; s.Runners[1].Collision.LastKind = (int)CollisionKind.Barrels;
            });
            Assert.Equal(1, sim.Events.Count);
            var e = sim.Events[0];
            Assert.Equal(RaceEventType.Collision, e.Type);
            Assert.Equal(1, e.Seat);
            Assert.Equal((int)CollisionKind.Barrels, e.Param1);
            Assert.Equal(0, e.Tick);
            Assert.Equal(V(40), DV(sim, ctl, 1));
            Assert.Equal(0, DD(sim, ctl, 1));
            Assert.Equal(0, ctl.Events.Count);
        }

        [Fact]
        public void WaterGivesCollisionPlus40AndDonations80()
        {
            var (sim, ctl) = StepWithFact(Quiet(Cfg(0, 1, 2, 3)), s =>
            {
                s.Runners[2].Collision.Count++; s.Runners[2].Collision.WaterCount++; s.Runners[2].Collision.LastKind = (int)CollisionKind.Water;
            });
            Assert.Equal(1, sim.Events.Count);
            Assert.Equal((int)CollisionKind.Water, sim.Events[0].Param1);
            Assert.Equal(V(40), DV(sim, ctl, 2));
            Assert.Equal(V(80), DD(sim, ctl, 2));
        }

        [Fact]
        public void TossGivesOneEventPlus120ViewersAnd50Donations()
        {
            var (sim, ctl) = StepWithFact(Quiet(Cfg(0, 1, 2, 3)), s => s.Runners[3].Toss.Tossed++);
            Assert.Equal(1, sim.Events.Count);
            Assert.Equal(RaceEventType.Tossed, sim.Events[0].Type);
            Assert.Equal(3, sim.Events[0].Seat);
            Assert.Equal(V(120), DV(sim, ctl, 3));
            Assert.Equal(V(50), DD(sim, ctl, 3));
        }

        [Fact]
        public void RoseGivesOneEventNoJump()
        {
            var (sim, ctl) = StepWithFact(Quiet(Cfg(0, 1, 2, 3)), s => s.Runners[0].Toss.Rose++);
            Assert.Equal(1, sim.Events.Count);
            Assert.Equal(RaceEventType.Rose, sim.Events[0].Type);
            Assert.Equal(0, DV(sim, ctl, 0));
        }

        [Fact]
        public void ShoveGivesTwoEventsPlus50AndMinus30()
        {
            var sim = new Simulation(Cfg(Nerd, Nerd, Nerd, Nerd), Data).Running();
            double s = BaseS(sim);
            Place(sim, 0, s, -2.4); Place(sim, 1, s, -1.2);
            Place(sim, 2, s + 300, 0); Place(sim, 3, s + 320, 0);
            var ctl = sim.Clone();
            sim.Step(Push); ctl.Step(TickInputs.Empty);
            Assert.Equal(2, sim.Events.Count);
            Assert.Equal(1, sim.Events.CountOf(RaceEventType.Shoved));
            Assert.Equal(1, sim.Events.CountOf(RaceEventType.GotShoved));
            for (int i = 0; i < sim.Events.Count; i++)
            {
                var e = sim.Events[i];
                if (e.Type == RaceEventType.Shoved) { Assert.Equal(0, e.Seat); Assert.Equal(1, e.Target); }
                else { Assert.Equal(1, e.Seat); Assert.Equal(0, e.Target); }
            }
            Assert.Equal(V(50), DV(sim, ctl, 0));
            Assert.Equal(-V(30), DV(sim, ctl, 1));
        }

        [Fact]
        public void TripGivesTripEventsAndPlus80()
        {
            var sim = new Simulation(Cfg(Nerd, Nerd, Nerd, Nerd), Data).Running();
            double s = BaseS(sim);
            Place(sim, 0, s, 0); Place(sim, 1, s + 1.1, 0);
            Place(sim, 2, s + 300, 0); Place(sim, 3, s + 320, 0);
            var ctl = sim.Clone();
            sim.Step(Push); ctl.Step(TickInputs.Empty);
            Assert.Equal(1, sim.Events.CountOf(RaceEventType.Tripped));
            Assert.Equal(1, sim.Events.CountOf(RaceEventType.GotTripped));
            Assert.Equal(1, sim.Events.CountOf(RaceEventType.LaidOut));
            Assert.Equal(3, sim.Events.Count);
            Assert.Equal(V(80), DV(sim, ctl, 0));
            Assert.Equal(0, DV(sim, ctl, 1));
        }

        [Fact]
        public void ShoveIntoObstacleLaysOutOnce()
        {
            var sim = Quiet(Cfg(Nerd, Nerd, Nerd, Nerd));
            // толчок 0 → 1 в тике 0, столкновение цели через 5 тиков, затем второе — «уложил» только один раз
            sim.State.Runners[1].Action.GotShoved++; sim.State.Runners[1].Action.LastGotShovedTick = 0; sim.State.Runners[1].Action.LastShovedBy = 0;
            sim.Step(TickInputs.Empty);
            int laid = 0;
            for (int t = 1; t <= 12; t++)
            {
                if (t == 5 || t == 9) { sim.State.Runners[1].Collision.Count++; sim.State.Runners[1].Collision.LastKind = (int)CollisionKind.Pole; }
                sim.Step(TickInputs.Empty);
                laid += sim.Events.CountOf(RaceEventType.LaidOut);
                if (t == 5) { Assert.Equal(1, sim.Events.CountOf(RaceEventType.LaidOut)); Assert.Equal(0, sim.Events[sim.Events.Count - 1].Seat); }
            }
            Assert.Equal(1, laid);
        }

        [Fact]
        public void ThresholdEveryTwoHundredFiftyOnce()
        {
            var sim = Quiet(Cfg(0, 1, 2, 3));
            sim.State.Runners[1].Viewing.Viewers = V(200); // +120 от подброса → 320 (порог 250)
            sim.State.Runners[1].Toss.Tossed++;
            sim.Step(TickInputs.Empty);
            Assert.Equal(1, sim.Events.CountOf(RaceEventType.ViewerThreshold));
            var e = sim.Events[sim.Events.Count - 1];
            Assert.Equal(RaceEventType.ViewerThreshold, e.Type);
            Assert.Equal(1, e.Seat);
            Assert.Equal(250, e.Param1);
            sim.Step(TickInputs.Empty);
            Assert.Equal(0, sim.Events.CountOf(RaceEventType.ViewerThreshold));
        }

        [Fact]
        public void ZoneChangeGivesOneEvent()
        {
            var sim = Quiet(Cfg(0, 1, 2, 3));
            // Соседи дальше «эфира пустого» (coolRange) и до, и после сдвига быка: смену зоны видит только место 0.
            for (int i = 1; i < 4; i++) Place(sim, i, BaseS(sim) + 100 * i, 0);
            sim.Step(TickInputs.Empty);
            Assert.Equal(0, sim.Events.CountOf(RaceEventType.ZoneChanged)); // первая зона — не смена
            sim.State.Bull.S = sim.State.Runners[0].S - Fx(3).Raw;
            sim.State.Bull.X = sim.State.Runners[0].X;
            sim.Step(TickInputs.Empty);
            Assert.Equal(1, sim.Events.CountOf(RaceEventType.ZoneChanged));
            for (int i = 0; i < sim.Events.Count; i++)
                if (sim.Events[i].Type == RaceEventType.ZoneChanged)
                {
                    Assert.Equal(0, sim.Events[i].Seat);
                    Assert.Equal(ViewerZone.Hot, sim.Events[i].Param2);
                    Assert.Equal(sim.State.Runners[0].Viewing.PrevZone, sim.Events[i].Param1);
                }
        }

        static (Simulation sim, Simulation ctl) RunFrame(int victimChar, int ticks)
        {
            var cfg = Cfg(Nerd, victimChar, Nerd, Nerd);
            var sim = new Simulation(cfg, Data).Running();
            var ctl = new Simulation(cfg, Data).Running();
            double s = BaseS(sim);
            for (int t = 0; t < ticks; t++)
            {
                foreach (var w in new[] { sim, ctl })
                {
                    Place(w, 1, s, 0);
                    Place(w, 0, w == sim ? s - 1.5 : s - 5, 0);
                    Place(w, 2, s + 300, 0); Place(w, 3, s + 320, 0);
                    w.State.Bull.S = Fx(s - 17).Raw; w.State.Bull.X = 0; // тёплая зона у всех, бык не двигается
                }
                sim.Step(TickInputs.Empty); ctl.Step(TickInputs.Empty);
                for (int i = 0; i < sim.Events.Count; i++)
                    Assert.NotEqual(RaceEventType.Tossed, sim.Events[i].Type);
                if (t != 30) Assert.Equal(0, sim.Events.CountOf(RaceEventType.FramedIn));
            }
            return (sim, ctl);
        }

        [Fact]
        public void FrameStealMovesViewersAfterOneSecond()
        {
            // за первую секунду ничего; на тике 30 — события; ещё 29 тиков тянет по 6/30
            var (sim, ctl) = RunFrame(Nerd, 60);
            long per = Fixed.FromInt(6).Raw / 30;
            Assert.Equal(30 * per, DV(sim, ctl, 0));
            Assert.Equal(-30 * per, DV(sim, ctl, 1));
        }

        [Fact]
        public void FrameStealOnGlamourIsHalf()
        {
            var (sim, ctl) = RunFrame(Glamour, 60);
            long per = (Fixed.FromInt(6) * Data.CharacterData.Get(Glamour).PerkValue).Raw / 30;
            Assert.Equal(30 * per, DV(sim, ctl, 0));
            Assert.Equal(-30 * per, DV(sim, ctl, 1));
            Assert.Equal(Fixed.FromInt(3).Raw / 30, per);
        }

        [Fact]
        public void FrameStealBothGetEventOnce()
        {
            var cfg = Cfg(Nerd, Glamour, Nerd, Nerd);
            var sim = new Simulation(cfg, Data).Running();
            double s = BaseS(sim);
            int framed = 0, inFrame = 0, firstTick = -1;
            for (int t = 0; t < 120; t++)
            {
                Place(sim, 1, s, 0); Place(sim, 0, s - 1.5, 0);
                Place(sim, 2, s + 300, 0); Place(sim, 3, s + 320, 0);
                sim.State.Bull.S = Fx(s - 17).Raw; sim.State.Bull.X = 0;
                sim.Step(TickInputs.Empty);
                for (int i = 0; i < sim.Events.Count; i++)
                {
                    var e = sim.Events[i];
                    if (e.Type == RaceEventType.FramedIn) { framed++; firstTick = e.Tick; Assert.Equal(0, e.Seat); Assert.Equal(1, e.Target); Assert.Equal(Fixed.FromInt(3).Raw, e.Param1); }
                    if (e.Type == RaceEventType.InYourFrame) { inFrame++; Assert.Equal(1, e.Seat); Assert.Equal(0, e.Target); Assert.Equal(e.Tick, firstTick); }
                }
            }
            Assert.Equal(1, framed);
            Assert.Equal(1, inFrame);
        }

        [Fact]
        public void NoFrameStealBeyondTwoMetersOrInFront()
        {
            var cfg = Cfg(Nerd, Nerd, Nerd, Nerd);
            var sim = new Simulation(cfg, Data).Running();
            double s = BaseS(sim);
            for (int t = 0; t < 90; t++)
            {
                Place(sim, 1, s, 0); Place(sim, 0, s - 2.5, 0); // дальше 2 м
                Place(sim, 2, s + 300, 0); Place(sim, 3, s + 320, 0);
                sim.State.Bull.S = Fx(s - 17).Raw; sim.State.Bull.X = 0;
                sim.Step(TickInputs.Empty);
                Assert.Equal(0, sim.Events.CountOf(RaceEventType.FramedIn));
            }
        }

        [Fact]
        public void BufferDoesNotAllocate()
        {
            var sim = new Simulation(Cfg(0, 1, 2, 3), Data);
            for (int i = 0; i < 100; i++) sim.Step(TickInputs.Empty);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 300; i++)
            {
                sim.State.Runners[i & 3].Collision.Count++;
                sim.State.Runners[i & 3].Toss.Tossed++;
                sim.Step(TickInputs.Empty);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void BufferCapacityDropsExtra()
        {
            var ev = new RaceEvents();
            for (int i = 0; i < RaceEvents.Capacity + 5; i++) ev.Add(RaceEventType.Rose, 0, 0);
            Assert.Equal(RaceEvents.Capacity, ev.Count);
            Assert.Equal(5, ev.Dropped);
            ev.Clear();
            Assert.Equal(0, ev.Count);
        }

        [Fact]
        public void FullRaceNoDropsDeterministicAndCloneMatches()
        {
            var cfg = new RaceConfig(11, new[]
            {
                new SeatConfig(SeatKind.Bot, 0), new SeatConfig(SeatKind.Bot, 1),
                new SeatConfig(SeatKind.Bot, 2), new SeatConfig(SeatKind.Bot, 3),
            });
            var a = new Simulation(cfg, Data);
            var b = new Simulation(cfg, Data);
            Simulation? snap = null;
            int total = 0;
            for (int t = 0; t < 3000; t++)
            {
                var inp = TickInputs.Empty.With(0, new PlayerInput((t / 40) % 3 - 1, 1, t % 50 == 0));
                a.Step(inp); b.Step(inp);
                Assert.Equal(a.StateHash(), b.StateHash());
                Assert.Equal(a.Events.Count, b.Events.Count);
                Assert.Equal(0, a.Events.Dropped);
                total += a.Events.Count;
                if (t == 1500) snap = a.Clone();
            }
            Assert.True(total > 0);
            // Clone/откат: пересчёт с 1500-го тика даёт тот же хэш
            for (int t = 1501; t < 3000; t++)
            {
                var inp = TickInputs.Empty.With(0, new PlayerInput((t / 40) % 3 - 1, 1, t % 50 == 0));
                snap!.Step(inp);
            }
            Assert.Equal(a.StateHash(), snap!.StateHash());
        }
    }
}
