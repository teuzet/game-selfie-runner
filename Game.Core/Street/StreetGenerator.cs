using System;
using System.Collections.Generic;
using Game.Core.Math;

namespace Game.Core.Street
{
    /// <summary>
    /// Сборка улицы по сиду: design/F-003.md §2 («Сборка по сиду», «Процедура одной попытки», «Расстановка препятствий»).
    /// Результат зависит только от сида, пула и данных. Только Fixed и DetRandom.
    /// </summary>
    public static class StreetGenerator
    {
        /// <summary>Страховочный предел попыток. Тест требует ≤ 16 (F-003 §2 п. 6).</summary>
        public const int MaxAttempts = 64;

        static readonly Fixed MinTotal = Fixed.FromInt(1740);
        static readonly Fixed MaxTotal = Fixed.FromInt(1860);
        static readonly Fixed LimitEnd = Fixed.FromInt(1820);
        static readonly Fixed ArenaThreshold = Fixed.FromInt(1700);
        static readonly Fixed FirstSpecialFrom = Fixed.FromInt(300);
        static readonly Fixed SpecialGap = Fixed.FromInt(120);
        static readonly Fixed StartLine = Fixed.FromInt(30);
        static readonly Fixed Ten = Fixed.FromInt(10);

        struct Placed { public BlockDef Def; public Fixed Length; }

        public static Street Build(ulong seed, StreetData data, StreetPool pool)
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                // под-сид (сид, номер попытки): отдельный поток PCG на попытку
                var rng = new DetRandom(seed, (ulong)attempt);
                var blocks = TryBlocks(ref rng, data, pool);
                if (blocks == null) continue;
                return Finish(seed, pool, attempt, blocks, data, ref rng);
            }
            throw new InvalidOperationException("Street: не собралась за " + MaxAttempts + " попыток, сид " + seed);
        }

        static bool InPool(BlockDef b, StreetPool pool) => b.Tier == "core" || (pool == StreetPool.CorePlusLater && b.Tier == "later");

        static List<Placed>? TryBlocks(ref DetRandom rng, StreetData data, StreetPool pool)
        {
            int k = 3 + rng.Range(0, 2);
            var start = data.FindBlock("start") ?? throw new InvalidOperationException("no start block");
            var list = new List<Placed>();
            list.Add(new Placed { Def = start, Length = start.LengthMin });
            Fixed l = start.LengthMin;
            int specials = 0;
            bool anySpecial = false;
            Fixed lastSpecialEnd = Fixed.Zero;
            string lastSpecialId = "";
            var cands = new List<BlockDef>();

            while (true)
            {
                BlockDef last = list[list.Count - 1].Def;
                cands.Clear();
                BlockDef? arena = null;
                for (int i = 0; i < last.Next.Length; i++)
                {
                    var c = data.FindBlock(last.Next[i]);
                    if (c == null || !InPool(c, pool)) continue;
                    if (c.Id == "arena") { arena = c; continue; }
                    cands.Add(c);
                }
                if (arena != null && specials == k && l >= ArenaThreshold)
                {
                    list.Add(new Placed { Def = arena, Length = arena.LengthMin });
                    l += arena.LengthMin;
                    break;
                }

                bool wide10Special = false;
                for (int i = 0; i < data.Blocks.Length; i++)
                {
                    var b = data.Blocks[i];
                    if (InPool(b, pool) && b.Special && b.StartWidth == Ten && b.Id != lastSpecialId) wide10Special = true;
                }

                int w = 0;
                for (int i = 0; i < cands.Count; i++)
                {
                    var c = cands[i];
                    bool ok = true;
                    if (c.Special)
                    {
                        if (last.Special || specials >= k || l < FirstSpecialFrom || c.Id == lastSpecialId) ok = false;
                        else if (anySpecial && l - lastSpecialEnd < SpecialGap) ok = false;
                    }
                    if (ok && (c.Id == "descent" || c.Id == "straight_10") && (specials >= k || !wide10Special)) ok = false;
                    if (ok && l + c.LengthMin > LimitEnd) ok = false;
                    if (ok) cands[w++] = c;
                }
                cands.RemoveRange(w, cands.Count - w);
                if (cands.Count == 0) return null;

                var pick = cands[rng.Range(0, cands.Count)];
                Fixed len = pick.LengthMin;
                if (pick.LengthStep.Raw > 0)
                {
                    Fixed max = Fixed.Min(pick.LengthMax, LimitEnd - l);
                    int steps = (int)((max - pick.LengthMin) / pick.LengthStep).ToInt();
                    len = pick.LengthMin + pick.LengthStep * Fixed.FromInt(rng.Range(0, steps + 1));
                }
                list.Add(new Placed { Def = pick, Length = len });
                if (pick.Special) { specials++; anySpecial = true; lastSpecialId = pick.Id; lastSpecialEnd = l + len; }
                l += len;
            }

            if (list.Count < 20 || list.Count > 24) return null;
            if (l < MinTotal || l > MaxTotal) return null;
            return list;
        }

        // ---------- препятствия ----------

        static Street Finish(ulong seed, StreetPool pool, int attempt, List<Placed> placed, StreetData data, ref DetRandom rng)
        {
            var blocks = new PlacedBlock[placed.Count];
            Fixed s = Fixed.Zero;
            for (int i = 0; i < blocks.Length; i++)
            {
                blocks[i] = new PlacedBlock(placed[i].Def, s, placed[i].Length, 0, 0, 0);
                s += placed[i].Length;
            }
            Fixed arenaStart = blocks[blocks.Length - 1].Start;
            var obs = new List<StreetObstacle>();
            // Street нужен для ширины — собираем временную улицу и потом пересоздаём с препятствиями
            var tmp = new Street(seed, pool, attempt, blocks, new StreetObstacle[0]);

            for (int bi = 0; bi < blocks.Length; bi++)
            {
                BlockDef def = blocks[bi].Def;
                int water = 0;
                for (int i = 0; i < def.Fixed.Length; i++)
                    if (def.Fixed[i].Kind == "water") water = rng.Range(0, 2) == 0 ? -1 : 1;
                if (water != 0) blocks[bi] = blocks[bi].WithWater(water);
                int first = obs.Count;
                PlaceBlock(tmp, bi, blocks[bi], arenaStart, data, ref rng, obs);
                blocks[bi] = blocks[bi].WithObstacles(first, obs.Count - first);
            }
            return new Street(seed, pool, attempt, blocks, obs.ToArray());
        }

        static void PlaceBlock(Street tmp, int bi, PlacedBlock pb, Fixed arenaStart, StreetData data, ref DetRandom rng, List<StreetObstacle> outObs)
        {
            BlockDef def = pb.Def;
            if (def.Obstacles.Length == 0) return;

            // ряды слотов
            Fixed step = def.SlotStep, margin = def.SlotMargin;
            int total = ((pb.Length - margin - margin) / step).ToInt() + 1;
            if (total <= 0) return;
            var rows = new Fixed[total];
            int r = 0;
            for (int i = 0; i < total; i++)
            {
                Fixed rs = margin + step * Fixed.FromInt(i);
                bool excluded = false;
                for (int f = 0; f < def.Fixed.Length; f++)
                    if (def.Fixed[f].Kind == "fountain" && Fixed.Abs(rs - def.Fixed[f].S) < Fixed.FromFraction(13, 2)) excluded = true;
                if (!excluded) rows[r++] = rs;
            }
            int R = r;

            // сколько препятствий
            Fixed mid = pb.Start + pb.Length / Fixed.FromInt(2);
            // n = round-half-up(длина × densityMul / (25 − 13p)), p = (mid − 30)/(A − 30) — точно, в целых
            // (деление Fixed на границе .5 теряет младший бит и округляет вниз)
            Fixed span = arenaStart - StartLine;
            Fixed midC = Fixed.Clamp(mid, StartLine, arenaStart);
            long a = (pb.Length * def.DensityMul).Raw;
            long c = (Fixed.FromInt(25) * span - Fixed.FromInt(13) * (midC - StartLine)).Raw;
            long one = Fixed.OneRaw;
            int n = (int)((2 * a * span.Raw + c * one) / (2 * c * one));
            if (n > R) n = R;
            if (n <= 0) return;

            // ряды
            var chosen = new int[n];
            bool sparse = 2 * n <= R;
            for (int k = 0; k < n; k++)
            {
                int ideal = (int)((2L * k + 1) * R / (2L * n));
                int shift = rng.Range(-1, 2);
                int row = ideal;
                if (shift != 0)
                {
                    int cand = ideal + shift;
                    bool ok = cand >= 0 && cand < R;
                    // плотный режим (n > R/2): только порядок и разные ряды; разрежённый — зазор ≥ 2 ряда
                    int minGap = sparse ? 2 : 1;
                    if (ok)
                    {
                        if (k > 0 && cand - chosen[k - 1] < minGap) ok = false;
                        if (k + 1 < n)
                        {
                            int nextIdeal = (int)((2L * (k + 1) + 1) * R / (2L * n));
                            if (nextIdeal - cand < minGap) ok = false;
                        }
                    }
                    if (ok) row = cand;
                }
                chosen[k] = row;
            }

            int totalWeight = 0;
            for (int i = 0; i < def.Obstacles.Length; i++) totalWeight += def.Obstacles[i].Weight;

            for (int k = 0; k < n; k++)
            {
                Fixed rs = pb.Start + rows[chosen[k]];
                int w = rng.Range(0, totalWeight);
                BlockObstacleDef be = def.Obstacles[0];
                for (int i = 0; i < def.Obstacles.Length; i++)
                {
                    if (w < def.Obstacles[i].Weight) { be = def.Obstacles[i]; break; }
                    w -= def.Obstacles[i].Weight;
                }
                ObstacleDef od = data.FindObstacle(be.Type) ?? throw new InvalidOperationException("no obstacle " + be.Type);

                // допустимые полосы
                string lanes = "";
                for (int i = 0; i < be.Lanes.Length; i++)
                {
                    string ln = be.Lanes[i];
                    bool allowed = ln == "D" || Array.IndexOf(od.Lanes, ln) >= 0;
                    if (allowed) lanes += ln;
                }
                if (lanes.Length == 0) throw new InvalidOperationException("no lanes for " + be.Type + " in " + def.Id);
                char lane = lanes[rng.Range(0, lanes.Length)];
                if (lane == 'D') lane = pb.WaterSide > 0 ? 'L' : 'R';

                outObs.Add(MakeObstacle(tmp, bi, pb, od, rs, lane, ref rng));
            }
        }

        static StreetObstacle MakeObstacle(Street tmp, int bi, PlacedBlock pb, ObstacleDef od, Fixed rs, char lane, ref DetRandom rng)
        {
            Fixed half = Fixed.Half;
            Fixed along, across;
            if (od.Shape == "circle") { along = od.Size; across = od.Size; }
            else { along = od.SizeAlong; across = od.SizeAcross; }

            Fixed w = tmp.MinWidthOn(rs - along * half, rs + along * half);
            Fixed x = Fixed.Zero, param = Fixed.Zero;
            Lane ln;
            switch (lane)
            {
                case 'L': ln = Lane.L; x = -(w * half - across * half); break;
                case 'R': ln = Lane.R; x = w * half - across * half; break;
                case 'C': ln = Lane.C; x = Fixed.FromFraction(rng.Range(0, 9) - 4, 4); break;
                case 'F':
                    ln = Lane.F;
                    across = w;
                    break;
                case 'M':
                {
                    ln = Lane.M;
                    Fixed lim = w * half - od.WallGap - od.Size * half;
                    Fixed lo = -lim, hi = lim;
                    for (int f = 0; f < pb.Def.Fixed.Length; f++)
                    {
                        var fe = pb.Def.Fixed[f];
                        if (fe.Kind != "water" || pb.WaterSide == 0) continue;
                        Fixed loc = rs - pb.Start;
                        if (loc < fe.S0 - od.Size || loc > fe.S1 + od.Size) continue;
                        Fixed edge = w * half - fe.Across - od.WallGap - od.Size * half;
                        if (pb.WaterSide > 0) hi = Fixed.Min(hi, edge); else lo = Fixed.Max(lo, -edge);
                    }
                    int steps = ((hi - lo) * Fixed.FromInt(4)).ToInt();
                    x = lo + Fixed.FromFraction(rng.Range(0, steps + 1), 4);
                    param = rng.Range(0, 2) == 0 ? -Fixed.One : Fixed.One;
                    break;
                }
                default: throw new InvalidOperationException("lane " + lane);
            }
            if (od.MotionKind == "door")
                param = Fixed.FromInt(rng.Range((int)od.TriggerMin.ToInt(), (int)od.TriggerMax.ToInt() + 1));
            return new StreetObstacle(od, bi, rs, x, along, across, ln, param);
        }
    }
}
