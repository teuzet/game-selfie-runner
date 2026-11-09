using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Math;
using Game.Core.Street;
using Xunit;

namespace Game.Core.Tests
{
    public class StreetDataTests
    {
        static readonly StreetData D = StreetData.Load();

        static Fixed F(string s) => StreetData.ParseFixed(s);

        // id, tier, lenMin, lenMax, width "s:w,s:w", slope, rise, special, obstacles "type:weight:lanes", next
        static readonly (string id, string tier, int lmin, int lmax, string width, Slope slope, int rise, bool special, string obs, string next)[] ExpectedBlocks =
        {
            ("start", "core", 80, 80, "0:8,80:8", Slope.Flat, 0, false, "", "straight_8,descent"),
            ("straight_6", "core", 60, 120, "0:6,120:6", Slope.Flat, 0, false, "laundry:3:F,door:3:LR,pots:2:LR,pole:1:LR,barrels:1:LR", "straight_6,ascent"),
            ("straight_8", "core", 60, 120, "0:8,120:8", Slope.Flat, 0, false, "pole:2:LR,stall:2:LR,barrels:2:LRC,laundry:1:F,door:2:LR,pots:2:LRC,gawker:1:M", "straight_8,descent,market,arch,stairs,arena"),
            ("straight_10", "core", 60, 120, "0:10,120:10", Slope.Flat, 0, false, "pole:2:LRC,stall:3:LR,barrels:2:LRC,pots:1:LRC,gawker:2:M", "straight_10,plaza,pond"),
            ("descent", "core", 100, 100, "0:8,100:10", Slope.Down, -6, false, "barrels:3:LRC,pole:2:LR,pots:2:LRC,gawker:1:M", "straight_10,plaza,pond"),
            ("ascent", "core", 80, 80, "0:6,80:8", Slope.Up, 5, false, "laundry:2:F,door:2:LR,pots:2:LR,pole:1:LR", "straight_8,descent,market,arch,stairs,arena"),
            ("plaza", "core", 80, 80, "0:10,15:14,65:14,80:8", Slope.Flat, 0, true, "pole:1:LR,stall:2:LR,pots:2:LR,gawker:3:M", "straight_8,descent,arena"),
            ("market", "core", 100, 100, "0:8,20:10,80:10,100:6", Slope.Flat, 0, true, "stall:5:LR,barrels:2:LRC,gawker:2:M", "straight_6,ascent"),
            ("arena", "core", 40, 40, "0:8,10:14,40:14", Slope.Flat, 0, false, "", ""),
            ("pond", "later", 100, 100, "0:10,85:10,100:8", Slope.Flat, 0, true, "pole:2:D,pots:2:D,gawker:2:M", "straight_8,descent,arena"),
            ("arch", "later", 40, 40, "0:8,16:4,24:4,40:8", Slope.Flat, 0, true, "", "straight_8,descent,arena"),
            ("stairs", "later", 60, 60, "0:8,60:6", Slope.Flat, -4, true, "pots:2:LR,laundry:1:F", "straight_6,ascent"),
        };

        [Fact]
        public void LoadTwiceGivesSameData()
        {
            Assert.Equal(StreetData.Load().Dump(), StreetData.Load().Dump());
        }

        [Fact]
        public void TwelveBlocksEightObstaclesWithTiers()
        {
            Assert.Equal(12, D.Blocks.Length);
            Assert.Equal(8, D.Obstacles.Length);
            Assert.Equal(ExpectedBlocks.Select(b => b.id), D.Blocks.Select(b => b.Id));
            Assert.Equal(9, D.Blocks.Count(b => b.Tier == "core"));
            foreach (var id in new[] { "pond", "arch", "stairs" }) Assert.Equal("later", D.FindBlock(id)!.Tier);
        }

        [Fact]
        public void BlockNumbersMatchDesignTable()
        {
            foreach (var e in ExpectedBlocks)
            {
                var b = D.FindBlock(e.id)!;
                Assert.Equal(e.tier, b.Tier);
                Assert.Equal(Fixed.FromInt(e.lmin), b.LengthMin);
                Assert.Equal(Fixed.FromInt(e.lmax), b.LengthMax);
                Assert.Equal(e.lmin == e.lmax ? Fixed.Zero : Fixed.FromInt(10), b.LengthStep);
                Assert.Equal(e.slope, b.Slope);
                Assert.Equal(Fixed.FromInt(e.rise), b.Rise);
                Assert.Equal(e.special, b.Special);
                Assert.Equal(Fixed.FromInt(6), b.SlotStep);
                Assert.Equal(Fixed.FromInt(6), b.SlotMargin);
                Assert.Equal(e.id == "market" ? F("1.25") : Fixed.One, b.DensityMul);

                var w = string.Join(",", b.Width.Select(p => (int)p.S + ":" + (int)p.Width));
                Assert.Equal(e.width, w);

                var obs = string.Join(",", b.Obstacles.Select(o => o.Type + ":" + o.Weight + ":" + string.Join("", o.Lanes)));
                Assert.Equal(e.obs, obs);
                Assert.Equal(e.next, string.Join(",", b.Next));
            }
            var plaza = D.FindBlock("plaza")!.Fixed.Single();
            Assert.Equal("fountain", plaza.Kind);
            Assert.Equal(Fixed.FromInt(40), plaza.S);
            Assert.Equal(Fixed.FromInt(5), plaza.Size);
            var water = D.FindBlock("pond")!.Fixed.Single();
            Assert.Equal((Fixed.FromInt(15), Fixed.FromInt(85), Fixed.FromInt(3), "seed"), (water.S0, water.S1, water.Across, water.Side));
            var zone = D.FindBlock("stairs")!.Fixed.Single();
            Assert.Equal(("zone", "stairs_zone", Fixed.Zero, Fixed.FromInt(60)), (zone.Kind, zone.Type, zone.S0, zone.S1));
        }

        [Fact]
        public void ObstacleNumbersMatchDesignTable()
        {
            void Check(string id, string shape, string size, string along, string across, string effect, string stun, string slowMul, string slowTime, string motion)
            {
                var o = D.FindObstacle(id)!;
                Assert.Equal(shape, o.Shape);
                Assert.Equal(F(size), o.Size);
                Assert.Equal(F(along), o.SizeAlong);
                Assert.Equal(F(across), o.SizeAcross);
                Assert.Equal(effect, o.Effect);
                Assert.Equal(F(stun), o.Stun);
                Assert.Equal(F(slowMul), o.SlowMul);
                Assert.Equal(F(slowTime), o.SlowTime);
                Assert.Equal(motion, o.MotionKind);
            }
            Check("pole", "circle", "0.5", "0", "0", "stun", "0.6", "1", "0", "none");
            Check("stall", "rect", "0", "2.0", "1.2", "stun", "0.6", "1", "0", "none");
            Check("barrels", "rect", "0", "1.5", "1.5", "stun", "0.6", "1", "0", "none");
            Check("laundry", "rect", "0", "0.3", "0", "slow", "0", "0.6", "1", "none");
            Check("door", "rect", "0", "0.2", "0.8", "stun", "0.6", "1", "0", "door");
            Check("pots", "circle", "0.6", "0", "0", "stun", "0.3", "1", "0", "none");
            Check("gawker", "circle", "0.8", "0", "0", "stun", "0.6", "1", "0", "walk");
            Check("stairs_zone", "zone", "0", "0", "0", "zone", "0", "0.8", "0", "none");

            Assert.True(D.FindObstacle("laundry")!.FullWidth);
            var door = D.FindObstacle("door")!;
            Assert.Equal((F("1.5"), Fixed.FromInt(8), Fixed.FromInt(16)), (door.OpenTime, door.TriggerMin, door.TriggerMax));
            var g = D.FindObstacle("gawker")!;
            Assert.Equal((Fixed.One, F("0.5")), (g.Speed, g.WallGap));
            Assert.Equal(F("0.6"), D.FindObstacle("stairs_zone")!.PushBack);
            Assert.Equal("later", D.FindObstacle("stairs_zone")!.Tier);
        }

        [Fact]
        public void BlockObstacleTypesExist()
        {
            foreach (var b in D.Blocks)
                foreach (var o in b.Obstacles)
                {
                    var def = D.FindObstacle(o.Type);
                    Assert.NotNull(def);
                    Assert.True(o.Weight > 0);
                }
            foreach (var b in D.Blocks)
                foreach (var f in b.Fixed.Where(f => f.Kind == "zone"))
                    Assert.NotNull(D.FindObstacle(f.Type));
        }

        [Fact]
        public void JoinRules()
        {
            foreach (var b in D.Blocks)
                foreach (var nid in b.Next)
                {
                    var n = D.FindBlock(nid);
                    Assert.NotNull(n);
                    Assert.True(b.EndWidth == n!.StartWidth, $"{b.Id} end {b.EndWidth} != {n.Id} start {n.StartWidth}");
                    Assert.False(b.Special && n.Special, $"special->special {b.Id}->{n.Id}");
                    Assert.NotEqual("start", n.Id);
                    if (n.Id == "arena") Assert.Equal(Fixed.FromInt(8), b.EndWidth);
                }
        }

        [Fact]
        public void ParseFixedExact()
        {
            Assert.Equal(Fixed.FromInt(3), F("3"));
            Assert.Equal(Fixed.Half, F("0.5"));
            Assert.Equal(Fixed.FromRaw(-Fixed.OneRaw * 6), F("-6"));
            Assert.Equal(Fixed.FromFraction(5, 4), F("1.25"));
        }
    }
}
