using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Math;
using Game.Core.Street;
using Xunit;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Tests
{
    using Math = System.Math;

    public class StreetGeneratorTests
    {
        static readonly StreetData D = StreetData.Load();
        const int Seeds = 1000;

        public static IEnumerable<object[]> Pools()
        {
            yield return new object[] { StreetPool.Core };
            yield return new object[] { StreetPool.CorePlusLater };
        }

        static double Dbl(Fixed f) => (double)f;

        static StreetMap[] BuildAll(StreetPool pool)
        {
            var r = new StreetMap[Seeds];
            for (int i = 0; i < Seeds; i++) r[i] = StreetGenerator.Build((ulong)(i * 7919 + 1), D, pool);
            return r;
        }

        static void Each(StreetPool pool, Action<StreetMap, List<string>> check)
        {
            var bad = new List<string>();
            foreach (var st in BuildAll(pool))
            {
                var errs = new List<string>();
                check(st, errs);
                foreach (var e in errs) bad.Add("seed " + st.Seed + ": " + e);
            }
            Assert.True(bad.Count == 0, bad.Count + " нарушений, первые:\n" + string.Join("\n", bad.Take(10)));
        }

        [Theory, MemberData(nameof(Pools))]
        public void P4_ChainStartArenaNextAndAttempts(StreetPool pool)
        {
            int maxAttempts = 0;
            Each(pool, (st, e) =>
            {
                maxAttempts = Math.Max(maxAttempts, st.Attempts);
                if (st.Attempts > 16) e.Add("попыток " + st.Attempts);
                if (st.Blocks[0].Def.Id != "start") e.Add("первый не start");
                if (st.Blocks[^1].Def.Id != "arena") e.Add("последний не arena");
                for (int i = 1; i < st.Blocks.Length; i++)
                {
                    if (!st.Blocks[i - 1].Def.Next.Contains(st.Blocks[i].Def.Id)) e.Add("стык " + i + " не по next");
                    if (pool == StreetPool.Core && st.Blocks[i].Def.Tier != "core") e.Add("later в пуле core");
                    if (st.Blocks[i - 1].Def.EndWidth != st.Blocks[i].Def.StartWidth) e.Add("ширина на стыке " + i);
                    if (st.Blocks[i].Start != st.Blocks[i - 1].End) e.Add("щель между блоками " + i);
                }
            });
            Assert.True(maxAttempts <= 16);
        }

        [Theory, MemberData(nameof(Pools))]
        public void P5_LengthsAndCounts(StreetPool pool)
        {
            Each(pool, (st, e) =>
            {
                double sum = Dbl(st.Length);
                if (Math.Abs(sum - 1800) > 60) e.Add("сумма " + sum);
                if (st.Blocks.Length < 20 || st.Blocks.Length > 24) e.Add("блоков " + st.Blocks.Length);
                foreach (var b in st.Blocks)
                {
                    if (b.Def.LengthStep.Raw > 0)
                    {
                        double len = Dbl(b.Length);
                        if (len < 60 || len > 120 || len % 10 != 0) e.Add("длина прямой " + len);
                    }
                    else if (b.Length != b.Def.LengthMin) e.Add("длина фикс. блока " + b.Def.Id);
                }
            });
        }

        [Theory, MemberData(nameof(Pools))]
        public void P6_SpecialPlaces(StreetPool pool)
        {
            Each(pool, (st, e) =>
            {
                var sp = st.Blocks.Where(b => b.Def.Special).ToList();
                if (sp.Count < 3 || sp.Count > 4) e.Add("особых " + sp.Count);
                for (int i = 1; i < st.Blocks.Length; i++)
                    if (st.Blocks[i].Def.Special && st.Blocks[i - 1].Def.Special) e.Add("особые рядом " + i);
                for (int i = 0; i < sp.Count; i++)
                {
                    if (i == 0 && Dbl(sp[0].Start) < 300) e.Add("первое особое на " + Dbl(sp[0].Start));
                    if (i > 0)
                    {
                        if (sp[i].Def.Id == sp[i - 1].Def.Id) e.Add("тип особого повторён " + sp[i].Def.Id);
                        if (Dbl(sp[i].Start - sp[i - 1].End) < 120) e.Add("зазор " + Dbl(sp[i].Start - sp[i - 1].End));
                    }
                }
            });
        }

        static int ExpectedCount(StreetMap st, int bi)
        {
            var b = st.Blocks[bi];
            double arenaStart = Dbl(st.Blocks[^1].Start);
            double mid = Math.Min(arenaStart, Math.Max(30, Dbl(b.Start) + Dbl(b.Length) / 2));
            // точное рациональное округление (границы .5 бывают точными): длина × dm / (25 − 13p)
            long A = (long)arenaStart - 30, m = (long)mid - 30;
            long num = (long)Dbl(b.Length) * (long)(Dbl(b.Def.DensityMul) * 4) * A;
            long den = 4 * (25 * A - 13 * m);
            int byDensity = (int)((2 * num + den) / (2 * den));
            int rows = (int)Math.Floor((Dbl(b.Length) - 12) / 6) + 1;
            foreach (var f in b.Def.Fixed.Where(f => f.Kind == "fountain"))
                for (int i = 0; i < (int)Math.Floor((Dbl(b.Length) - 12) / 6) + 1; i++)
                    if (Math.Abs(6 + 6 * i - Dbl(f.S)) < 6.5) rows--;
            return Math.Min(rows, byDensity);
        }

        [Theory, MemberData(nameof(Pools))]
        public void P7_ObstacleCountsAndSpacing(StreetPool pool)
        {
            Each(pool, (st, e) =>
            {
                for (int bi = 0; bi < st.Blocks.Length; bi++)
                {
                    var b = st.Blocks[bi];
                    var list = st.Obstacles.Skip(b.FirstObstacle).Take(b.ObstacleCount).ToList();
                    if (b.Def.Id == "start" || b.Def.Id == "arena")
                    {
                        if (list.Count != 0) e.Add("препятствия в " + b.Def.Id);
                        continue;
                    }
                    if (b.Def.Obstacles.Length == 0) { if (list.Count != 0) e.Add("препятствия в пустом " + b.Def.Id); continue; }
                    int exp = ExpectedCount(st, bi);
                    if (list.Count != exp) e.Add(b.Def.Id + " n=" + list.Count + " ожидали " + exp + " (len " + Dbl(b.Length) + " start " + Dbl(b.Start) + " arena " + Dbl(st.Blocks[^1].Start) + ")");
                    var rowsDistinct = list.Select(o => o.S.Raw).Distinct().Count();
                    if (rowsDistinct != list.Count) e.Add("два препятствия в одном ряду " + b.Def.Id);
                    if (b.Def.Id == "plaza")
                        foreach (var o in list)
                            if (Math.Abs(Dbl(o.S - b.Start) - 40) < 6.5) e.Add("препятствие у фонтана " + Dbl(o.S - b.Start));
                    int rows = (int)Math.Floor((Dbl(b.Length) - 12) / 6) + 1;
                    if (b.Def.Id == "plaza") rows -= 2;
                    if (2 * list.Count <= rows)
                        for (int i = 1; i < list.Count; i++)
                            if (Dbl(list[i].S - list[i - 1].S) < 12) e.Add("ближе 12 м в " + b.Def.Id);
                    if (bi > 0 && list.Count > 0)
                    {
                        var pb = st.Blocks[bi - 1];
                        if (pb.ObstacleCount > 0)
                        {
                            var prevLast = st.Obstacles[pb.FirstObstacle + pb.ObstacleCount - 1];
                            if (Dbl(list[0].S - prevLast.S) < 12) e.Add("соседние блоки ближе 12 м на " + bi);
                        }
                    }
                }
            });
        }

        [Theory, MemberData(nameof(Pools))]
        public void P7_DensityGrowsTowardFinish(StreetPool pool)
        {
            Each(pool, (st, e) =>
            {
                double len = Dbl(st.Length);
                var xs = st.Obstacles.Select(o => Dbl(o.S)).ToList();
                double Mean(Func<double, bool> pick)
                {
                    var l = xs.Where(pick).ToList();
                    return l.Count < 2 ? double.NaN : (l[^1] - l[0]) / (l.Count - 1);
                }
                double first = Mean(s => s < len / 3), lastThird = Mean(s => s >= 2 * len / 3);
                if (!(lastThird < first)) e.Add("шаг в первой трети " + first + ", в последней " + lastThird);
            });
        }

        [Theory, MemberData(nameof(Pools))]
        public void P8_PlacementRules(StreetPool pool)
        {
            Each(pool, (st, e) =>
            {
                for (int bi = 0; bi < st.Blocks.Length; bi++)
                {
                    var b = st.Blocks[bi];
                    for (int i = 0; i < b.ObstacleCount; i++)
                    {
                        var o = st.Obstacles[b.FirstObstacle + i];
                        var be = b.Def.Obstacles.FirstOrDefault(x => x.Type == o.Type);
                        if (be == null) { e.Add(o.Type + " не разрешён в " + b.Def.Id); continue; }
                        string laneCh = o.Lane.ToString();
                        bool laneOk = be.Lanes.Contains(laneCh) || (be.Lanes.Contains("D") && (laneCh == "L" || laneCh == "R"));
                        if (!laneOk) e.Add("полоса " + laneCh + " у " + o.Type + " в " + b.Def.Id);
                        double half = Dbl(o.SizeAlong) / 2;
                        double w = Dbl(st.MinWidthOn(o.S - o.SizeAlong / Fixed.FromInt(2), o.S + o.SizeAlong / Fixed.FromInt(2)));
                        double across = Dbl(o.SizeAcross);
                        if (Math.Abs(Dbl(o.X)) + across / 2 > w / 2 + 1e-3) e.Add("за стеной " + o.Type + " в " + b.Def.Id);
                        double free = w - across;
                        double lo = Dbl(o.X) - across / 2, hi = Dbl(o.X) + across / 2;
                        if (b.WaterSide != 0)
                        {
                            var water = b.Def.Fixed.First(f => f.Kind == "water");
                            double loc = Dbl(o.S - b.Start);
                            bool inRange = loc + half > Dbl(water.S0) && loc - half < Dbl(water.S1);
                            if (inRange)
                            {
                                double wl = b.WaterSide > 0 ? w / 2 - Dbl(water.Across) : -w / 2;
                                double wh = b.WaterSide > 0 ? w / 2 : -w / 2 + Dbl(water.Across);
                                if (hi > wl && lo < wh) e.Add("в воде " + o.Type);
                                free -= Dbl(water.Across);
                            }
                            if (be.Lanes.Contains("D") && o.Lane != (b.WaterSide > 0 ? Lane.L : Lane.R)) e.Add("D не у сухой стены");
                        }
                        if (o.Lane != Lane.F && free < 2) e.Add("проход " + free + " у " + o.Type);
                    }
                }
            });
        }

        [Theory, MemberData(nameof(Pools))]
        public void P9_DeterministicAndDistinct(StreetPool pool)
        {
            var a = BuildAll(pool);
            var b = BuildAll(pool);
            for (int i = 0; i < Seeds; i++)
            {
                Assert.Equal(a[i].Hash(), b[i].Hash());
                Assert.Equal(a[i].Obstacles.Length, b[i].Obstacles.Length);
                for (int k = 0; k < a[i].Obstacles.Length; k++)
                {
                    Assert.Equal(a[i].Obstacles[k].S, b[i].Obstacles[k].S);
                    Assert.Equal(a[i].Obstacles[k].X, b[i].Obstacles[k].X);
                    Assert.Equal(a[i].Obstacles[k].Type, b[i].Obstacles[k].Type);
                }
            }
            int distinct = a.Select(s => s.Hash()).Distinct().Count();
            Assert.True(distinct > Seeds * 0.99, "различных улиц " + distinct + " из " + Seeds);
        }

        [Fact]
        public void Queries_WidthSlopeObstaclesIn()
        {
            var st = StreetGenerator.Build(5, D, StreetPool.CorePlusLater);
            foreach (var b in st.Blocks)
            {
                Assert.Equal(b.Def.Width[0].Width, st.WidthAt(b.Start));
                var expected = b.Def.Slope == Slope.Down ? Fixed.FromFraction(11, 10) : b.Def.Slope == Slope.Up ? Fixed.FromFraction(9, 10) : Fixed.One;
                Assert.Equal(expected, st.SlopeAt(b.Start + Fixed.One));
            }
            Assert.Equal(st.Obstacles.Length, st.ObstaclesIn(Fixed.Zero, st.Length).Length);
            Assert.Equal(0, st.ObstaclesIn(Fixed.Zero, Fixed.FromInt(80)).Length);
            var mid = st.ObstaclesIn(Fixed.FromInt(300), Fixed.FromInt(600));
            Assert.All(mid.ToArray(), o => Assert.InRange(Dbl(o.S), 300, 600));
            Assert.Equal(st.Obstacles.Count(o => Dbl(o.S) >= 300 && Dbl(o.S) < 600), mid.Length);
        }

        [Fact]
        public void SimulationHasStreetFromSeedSharedByClone()
        {
            var seats = new SeatConfig[4];
            var sim = new Simulation(new RaceConfig(42, seats));
            Assert.Equal(StreetGenerator.Build(42, D, StreetPool.Core).Hash(), sim.Street.Hash());
            Assert.Same(sim.Street, sim.Clone().Street);
        }
    }
}
