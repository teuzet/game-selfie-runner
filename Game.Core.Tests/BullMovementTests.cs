using System;
using Game.Core.Bull;
using Game.Core.Math;
using Game.Core.Street;
using Xunit;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Tests
{
    public class BullMovementTests
    {
        static readonly BullData D = BullData.Load();
        static readonly StreetMap Street = StreetGenerator.Build(1, StreetData.Load(), StreetPool.Core);
        static readonly Fixed Dt = Fixed.FromFraction(1, 30);
        const int PenTicks = 150;

        static double Dbl(long raw) => raw / (double)Fixed.OneRaw;

        static BullState NewBull() { var b = new BullState(); BullMovement.Init(D, ref b); return b; }

        static RunnerState[] Dummies(double s)
        {
            var r = new RunnerState[4];
            for (int i = 0; i < 4; i++) { r[i].S = Fixed.FromFraction((int)(s * 100), 100).Raw; r[i].X = Fixed.FromFraction(i * 100 - 150, 100).Raw; }
            return r;
        }

        static void MoveDummies(RunnerState[] r, Fixed speed)
        {
            for (int i = 0; i < r.Length; i++) r[i].S = (Fixed.FromRaw(r[i].S) + speed * Dt).Raw;
        }

        [Fact]
        public void DataMatchesDesign()
        {
            Assert.Equal(25, (double)D.PenOffset);
            Assert.Equal(9.5, (double)D.ChargeSpeed);
            Assert.Equal(6.0, (double)D.CruiseSpeed);
            Assert.Equal(5.0, (double)D.CloseSpeed);
            Assert.Equal(12, (double)D.TargetBehind);
            Assert.Equal(25, (double)D.LagFar);
            Assert.Equal(6, (double)D.LagNear);
            Assert.Equal(0.8, (double)D.StairsMul, 3);
            Assert.Equal(1.2, (double)D.CapsuleWidth, 3);
        }

        [Fact]
        public void StartsInPen25mBehindLine_AndDoesNotMoveBeforeRelease()
        {
            var b = NewBull();
            Assert.Equal(5.0, Dbl(b.S));
            Assert.Equal((int)BullPhase.Pen, b.Phase);
            var r = Dummies(45); // к срыву толпа уйдёт на 14+ м от линии
            long s0 = b.S;
            for (int t = 0; t < PenTicks - 1; t++)
            {
                BullMovement.Step(D, Street, ref b, r);
                Assert.Equal(s0, b.S);
                Assert.Equal(0, b.Speed);
                Assert.Equal((int)BullPhase.Pen, b.Phase);
            }
            BullMovement.Step(D, Street, ref b, r); // срыв: тик 150 = 3 с отсчёта + 2 с
            Assert.Equal((int)BullPhase.Charge, b.Phase);
            Assert.True(b.Speed > 0);
            for (int t = 0; t < 60; t++) BullMovement.Step(D, Street, ref b, r);
            Assert.Equal(9.5, Dbl(b.Speed), 3); // разгон до 9.5 м/с
        }

        [Fact]
        public void KeepsDistanceToMedianBetween6And25()
        {
            var b = NewBull();
            var r = Dummies(30);
            Fixed seven = Fixed.FromInt(7);
            double arenaStart = (double)Street.Blocks[Street.Blocks.Length - 1].Start;
            double min = 1e9, max = -1e9;
            bool caught = false;
            int guard = 0;
            while (b.Phase != (int)BullPhase.Arena && guard++ < 20000)
            {
                MoveDummies(r, seven);
                BullMovement.Step(D, Street, ref b, r);
                if (b.Phase == (int)BullPhase.Arena) break;
                double lag = (double)BullMovement.MedianS(r) - Dbl(b.S);
                if (b.Phase == (int)BullPhase.Cruise) caught = true;
                if (!caught) continue;
                min = System.Math.Min(min, lag); max = System.Math.Max(max, lag);
            }
            Assert.True(caught);
            Assert.Equal((int)BullPhase.Arena, b.Phase);
            Assert.True(min >= 6 && max <= 25.0, $"дистанция до медианы {min:F2}..{max:F2}, ожидалось 6..25");
            Assert.True(min <= 13, $"бык не вернулся к цели 12 м после рывка: минимум {min:F2}");
        }

        [Fact]
        public void CatchUpReturnsToTargetBehind()
        {
            var b = NewBull();
            var r = Dummies(60); // толпа далеко: лаг >25 → рывок до 12 м
            b.Phase = (int)BullPhase.Cruise; b.Timer = 0; b.S = Fixed.FromInt(10).Raw; b.Speed = Fixed.FromInt(6).Raw;
            double min = 1e9;
            for (int t = 0; t < 300; t++)
            {
                MoveDummies(r, Fixed.FromInt(7));
                BullMovement.Step(D, Street, ref b, r);
                min = System.Math.Min(min, (double)BullMovement.MedianS(r) - Dbl(b.S));
                if (min < 20) break;
            }
            for (int t = 0; t < 30; t++)
            {
                MoveDummies(r, Fixed.FromInt(7));
                BullMovement.Step(D, Street, ref b, r);
            }
            // после рывка бык держится у цели, а не у границы 25
            double lag = (double)BullMovement.MedianS(r) - Dbl(b.S);
            Assert.InRange(lag, 6, 20);
        }

        [Fact]
        public void SlopeScalesCruise()
        {
            foreach (var (id, mul) in new[] { ("descent", 1.1), ("ascent", 0.9) })
            {
                StreetMap? st = null; PlacedBlock blk = default;
                for (ulong seed = 1; seed < 400 && st == null; seed++)
                {
                    var c = StreetGenerator.Build(seed, StreetData.Load(), StreetPool.Core);
                    foreach (var pb in c.Blocks) if (pb.Def.Id == id) { st = c; blk = pb; break; }
                }
                Assert.NotNull(st);
                var b = NewBull();
                b.Phase = (int)BullPhase.Cruise; b.Timer = 0;
                double s0 = (double)blk.Start + 5;
                b.S = Fixed.FromFraction((int)(s0 * 100), 100).Raw;
                var r = Dummies(s0 + 15); // лаг 15 — крейсер
                b.Speed = Fixed.FromInt(6).Raw;
                for (int t = 0; t < 10; t++) BullMovement.Step(D, st!, ref b, r);
                Assert.Equal(6.0 * mul, Dbl(b.Speed), 2);
            }
        }

        [Fact]
        public void StopsOnArena()
        {
            var b = NewBull();
            var r = Dummies(30);
            Fixed arena = Street.Blocks[Street.Blocks.Length - 1].Start;
            b.Phase = (int)BullPhase.Cruise; b.Timer = 0;
            b.S = (arena - Fixed.One).Raw; b.Speed = Fixed.FromInt(6).Raw;
            r[0].S = r[1].S = r[2].S = r[3].S = (arena + Fixed.FromInt(10)).Raw;
            for (int t = 0; t < 60; t++) BullMovement.Step(D, Street, ref b, r);
            Assert.Equal((int)BullPhase.Arena, b.Phase);
            Assert.Equal(0, b.Speed);
            long s = b.S;
            for (int t = 0; t < 30; t++) BullMovement.Step(D, Street, ref b, r);
            Assert.Equal(s, b.S);
            Assert.Equal(0, b.Speed);
        }

        [Fact]
        public void SlowDownAndSlopeApplyToCruise()
        {
            // Замедление ×0.7: цель крейсера 6.0 → 4.2.
            var b = NewBull();
            var r = Dummies(40);
            for (int i = 0; i < 4; i++) r[i].S = Fixed.FromInt(40).Raw; // толпа в 15 м впереди: крейсер 6.0
            b.Phase = (int)BullPhase.Cruise; b.Timer = 0; b.S = Fixed.FromInt(25).Raw; b.Speed = 0;
            BullMovement.Slow(ref b, 300);
            for (int t = 0; t < 40; t++) BullMovement.Step(D, Street, ref b, r);
            Assert.Equal((int)BullPhase.Slowed, b.Phase);
            Assert.Equal(4.2, Dbl(b.Speed), 2);
        }

        [Fact]
        public void StairsZoneScalesAllBullSpeeds()
        {
            StreetMap? st = null;
            for (ulong seed = 1; seed < 400 && st == null; seed++)
            {
                var c = StreetGenerator.Build(seed, StreetData.Load(), StreetPool.CorePlusLater);
                foreach (var pb in c.Blocks) if (pb.Def.Id == "stairs") { st = c; break; }
            }
            Assert.NotNull(st);
            PlacedBlock stairs = default;
            foreach (var pb in st!.Blocks) if (pb.Def.Id == "stairs") stairs = pb;
            // Крейсер, рывок и сближение внутри лестницы (толпа далеко/в районе быка).
            double[] crowdLag = { 15, 40, 2 };
            double[] want = { 6.0, 9.5, 5.0 };
            for (int k = 0; k < 3; k++)
            {
                var b = NewBull();
                b.Phase = (int)BullPhase.Cruise; b.Timer = 0;
                double s0 = (double)stairs.Start + 20;
                b.S = Fixed.FromFraction((int)(s0 * 100), 100).Raw;
                var r = Dummies(s0 + crowdLag[k]);
                for (int i = 0; i < 4; i++) r[i].S = (Fixed.FromRaw(r[i].S)).Raw;
                b.Speed = Fixed.FromFraction((int)(want[k] * 80), 100).Raw;
                for (int t = 0; t < 5; t++) BullMovement.Step(D, st, ref b, r);
                Assert.Equal(want[k] * 0.8, Dbl(b.Speed), 2);
            }
        }

        [Fact]
        public void IgnoresObstaclesAndSteersTowardNearestRunner()
        {
            // Ближайший бегун справа (x=+2, впереди), остальные далеко слева (x=-2): бык уходит вправо.
            var b = NewBull();
            var r = Dummies(100);
            r[0].S = Fixed.FromInt(44).Raw; r[0].X = Fixed.FromInt(2).Raw;
            for (int i = 1; i < 4; i++) r[i].X = Fixed.FromInt(-2).Raw;
            b.Phase = (int)BullPhase.Cruise; b.Timer = 0; b.S = Fixed.FromInt(40).Raw; b.Speed = Fixed.FromInt(6).Raw;
            for (int t = 0; t < 10; t++) BullMovement.Step(D, Street, ref b, r);
            Assert.True(Dbl(b.X) > 0.5, $"x={Dbl(b.X):F2}, ожидался сдвиг к ближайшему (+2)");
            Assert.True(b.Speed > 0);
        }

        [Fact]
        public void XIsClampedToStreetEdgesMinusHalfCapsule()
        {
            var b = NewBull();
            var r = Dummies(60);
            b.Phase = (int)BullPhase.Cruise; b.Timer = 0; b.S = Fixed.FromInt(40).Raw; b.Speed = Fixed.FromInt(6).Raw;
            for (int t = 0; t < 120; t++)
            {
                for (int i = 0; i < 4; i++) { r[i].S = b.S + Fixed.FromInt(14).Raw; r[i].X = Fixed.FromInt(20).Raw; }
                BullMovement.Step(D, Street, ref b, r);
                double lim = (double)Street.WidthAt(Fixed.FromRaw(b.S)) / 2 - 0.6;
                Assert.True(Dbl(b.X) <= lim + 1e-3, $"x={Dbl(b.X):F2} > {lim:F2}");
            }
            Assert.True(Dbl(b.X) > 1.0);
        }

        [Fact]
        public void SimulationIsDeterministicAndBullMoves()
        {
            var cfg = new RaceConfig(7, new[]
            {
                new SeatConfig(SeatKind.Human, 0), new SeatConfig(SeatKind.Human, 1),
                new SeatConfig(SeatKind.Human, 2), new SeatConfig(SeatKind.Bot, 3, 1),
            });
            var a = new Simulation(cfg); var c = new Simulation(cfg);
            var inp = new TickInputs();
            for (int t = 0; t < 600; t++)
            {
                a.Step(inp); c.Step(inp);
                Assert.Equal(a.StateHash(), c.StateHash());
            }
            Assert.True(a.State.Bull.S > Fixed.FromInt(5).Raw);
            var cl = a.Clone();
            a.Step(inp); cl.Step(inp);
            Assert.Equal(a.StateHash(), cl.StateHash());
        }
    }
}
