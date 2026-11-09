using Game.Core.Math;
using Game.Core.Street;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Runners
{
    public enum CollisionKind : byte
    {
        None = 0, Pole, Stall, Barrels, Laundry, Door, Pots, Gawker, Fountain, Water, Other,
    }

    /// <summary>Столкновения бегуна: последний факт (для событий F-019) и текущие эффекты. Входит в RaceState и хэш.</summary>
    public struct CollisionState
    {
        public int Count;      // всего столкновений (чистый пробег = 0)
        public int WaterCount; // из них — вода
        public int LastKind;   // CollisionKind последнего
        public long LastS, LastX;
        public int LastTick;   // тик последнего (-1 нет)
        public int SlowTicks;  // замедление бельём
        public long SlowCap;   // потолок скорости на время замедления
        public int WaterPending; // 1 — после стана вынести к кромке воды
        public int ZoneActive; // 1 — в прошлый тик скорость масштабировалась зоной лестницы
        public long ZoneRaw;   // скорость без множителя зоны

        public ulong Hash(ulong h)
        {
            h = Hashing.Mix(h, (ulong)Count); h = Hashing.Mix(h, (ulong)WaterCount); h = Hashing.Mix(h, (ulong)LastKind);
            h = Hashing.Mix(h, (ulong)LastS); h = Hashing.Mix(h, (ulong)LastX); h = Hashing.Mix(h, (ulong)LastTick);
            h = Hashing.Mix(h, (ulong)SlowTicks); h = Hashing.Mix(h, (ulong)SlowCap); h = Hashing.Mix(h, (ulong)WaterPending);
            h = Hashing.Mix(h, (ulong)ZoneActive);
            return Hashing.Mix(h, (ulong)ZoneRaw);
        }
    }

    /// <summary>Пересечение круга бегуна с препятствиями и эффекты. design/F-003.md §2. Без аллокаций.</summary>
    public static class Collisions
    {
        static readonly Fixed Window = Fixed.FromInt(3);
        /// <summary>Вода: стан 1.5 с, устойчивость не уменьшает (design/F-003.md §2).</summary>
        public static readonly Fixed WaterStun = Fixed.FromFraction(3, 2);
        /// <summary>Зона лестницы ×0.8 (design/F-003.md §3).</summary>
        public static readonly Fixed StairsMul = Fixed.FromFraction(4, 5);
        static readonly Fixed StepDt = Fixed.FromFraction(1, Simulation.TicksPerSecond);
        static readonly Fixed TicksFx = Fixed.FromInt(Simulation.TicksPerSecond);

        /// <summary>Размер массива флагов «уже действовало»: препятствия + по одному фонтану на блок, на каждое место.</summary>
        public static int HitStride(StreetMap street) => street.Obstacles.Length + street.Blocks.Length;

        public static void InitState(ref CollisionState c) { c = default; c.LastTick = -1; }

        /// <summary>До движения: вернуть скорость без множителя зоны лестницы.</summary>
        public static void BeforeMove(RaceState st)
        {
            for (int i = 0; i < st.Runners.Length; i++)
                if (st.Runners[i].Collision.ZoneActive != 0) st.Runners[i].Speed = st.Runners[i].Collision.ZoneRaw;
        }

        /// <summary>После движения и расталкивания: столкновения, замедления, зона, вынос из воды.</summary>
        public static void After(RunnerData d, StreetMap street, RaceState st)
        {
            int stride = HitStride(street);
            for (int seat = 0; seat < st.Runners.Length; seat++)
                StepSeat(d, street, st, seat, stride);
        }

        static void StepSeat(RunnerData d, StreetMap street, RaceState st, int seat, int stride)
        {
            ref RunnerState r = ref st.Runners[seat];
            ref CollisionState c = ref r.Collision;
            Fixed s = Fixed.FromRaw(r.S);
            int bi = street.BlockIndexAt(s);
            ref readonly PlacedBlock pb = ref street.Blocks[bi];

            // StepRunner прошёл тик на «сырой» скорости: возвращаем s так, будто шли с потолком
            // замедления бельём и множителем зоны лестницы.
            {
                Fixed raw = Fixed.FromRaw(r.Speed);
                Fixed eff = raw;
                if (c.SlowTicks > 0 && eff > Fixed.FromRaw(c.SlowCap)) eff = Fixed.FromRaw(c.SlowCap);
                if (InZone(pb, s)) eff = eff * StairsMul;
                if (eff < raw && raw > Fixed.Zero)
                {
                    s = s - (raw - eff) * StepDt;
                    r.S = s.Raw;
                    bi = street.BlockIndexAt(s);
                    pb = ref street.Blocks[bi];
                }
            }

            // Вынос к кромке воды после стана.
            if (c.WaterPending != 0 && r.StunTicks == 0)
            {
                c.WaterPending = 0;
                Fixed w = street.WidthAt(s);
                Fixed across = WaterAcross(pb);
                Fixed dry = w / Fixed.FromInt(2) - across - d.Radius;
                if (dry < Fixed.Zero) dry = Fixed.Zero;
                r.X = (pb.WaterSide > 0 ? dry : -dry).Raw;
            }
            else if (r.StunTicks == 0 && c.WaterPending == 0)
            {
                Detect(d, street, st, seat, stride, ref r, ref c, s, bi);
            }

            // Замедление бельём.
            if (c.SlowTicks > 0)
            {
                if (r.Speed > c.SlowCap) r.Speed = c.SlowCap;
                c.SlowTicks--;
            }

            // Зона лестницы ×0.8.
            if (InZone(pb, s))
            {
                c.ZoneRaw = r.Speed;
                r.Speed = (Fixed.FromRaw(r.Speed) * StairsMul).Raw;
                c.ZoneActive = 1;
            }
            else c.ZoneActive = 0;
        }

        static bool InZone(in PlacedBlock pb, Fixed s)
        {
            var fx = pb.Def.Fixed;
            for (int f = 0; f < fx.Length; f++)
            {
                if (fx[f].Kind != "zone") continue;
                Fixed loc = s - pb.Start;
                if (loc >= fx[f].S0 && loc <= fx[f].S1) return true;
            }
            return false;
        }

        static Fixed WaterAcross(in PlacedBlock pb)
        {
            var fx = pb.Def.Fixed;
            for (int f = 0; f < fx.Length; f++) if (fx[f].Kind == "water") return fx[f].Across;
            return Fixed.Zero;
        }

        static void Detect(RunnerData d, StreetMap street, RaceState st, int seat, int stride,
            ref RunnerState r, ref CollisionState c, Fixed s, int bi)
        {
            Fixed x = Fixed.FromRaw(r.X);
            Fixed rad = d.Radius;
            int hitBase = seat * stride;
            var obs = street.Obstacles;

            // Вода и фонтан — фиксированные элементы блока.
            ref readonly PlacedBlock pb = ref street.Blocks[bi];
            var fx = pb.Def.Fixed;
            for (int f = 0; f < fx.Length; f++)
            {
                if (fx[f].Kind == "water" && pb.WaterSide != 0)
                {
                    Fixed loc = s - pb.Start;
                    if (loc < fx[f].S0 || loc > fx[f].S1) continue;
                    Fixed w = street.WidthAt(s);
                    Fixed edge = w / Fixed.FromInt(2) - fx[f].Across; // кромка воды по модулю x
                    Fixed depth = pb.WaterSide > 0 ? x + rad - edge : -x + rad - edge;
                    if (depth <= Fixed.Zero) continue;
                    int idx = obs.Length + bi;
                    if (st.Hit[hitBase + idx] != 0) continue;
                    st.Hit[hitBase + idx] = 1;
                    Record(st, ref r, ref c, CollisionKind.Water);
                    r.Speed = 0;
                    r.StunTicks = (WaterStun * TicksFx).ToInt();
                    c.WaterPending = 1;
                    c.WaterCount++;
                    return;
                }
                else if (fx[f].Kind == "fountain")
                {
                    Fixed fs = pb.Start + fx[f].S;
                    Fixed dx = x - fx[f].X, ds = s - fs;
                    Fixed rr = rad + fx[f].Size / Fixed.FromInt(2);
                    if (Fixed.Abs(dx) >= rr || Fixed.Abs(ds) >= rr || dx * dx + ds * ds >= rr * rr) continue;
                    int idx = obs.Length + bi;
                    if (st.Hit[hitBase + idx] != 0) continue;
                    st.Hit[hitBase + idx] = 1;
                    Stun(st, seat, ref r, ref c, CollisionKind.Fountain, PoleStun(street));
                    return;
                }
            }

            // Препятствия в окне по s (массив отсортирован по s).
            int lo = 0, hi = obs.Length;
            Fixed from = s - Window;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (obs[mid].S < from) lo = mid + 1; else hi = mid; }
            Fixed to = s + Window;
            for (int i = lo; i < obs.Length && obs[i].S <= to; i++)
            {
                if (st.Hit[hitBase + i] != 0) continue;
                ref readonly StreetObstacle o = ref obs[i];
                ObstacleDef od = o.Def;
                if (od.Effect != "stun" && od.Effect != "slow") continue;
                if (!Overlaps(st, i, o, s, x, rad)) continue;

                st.Hit[hitBase + i] = 1;
                CollisionKind kind = KindOf(od.Id);
                if (od.Effect == "slow")
                {
                    Record(st, ref r, ref c, kind);
                    Fixed v = Fixed.FromRaw(r.Speed) * od.SlowMul;
                    r.Speed = v.Raw;
                    c.SlowCap = v.Raw;
                    c.SlowTicks = (od.SlowTime * TicksFx).ToInt();
                    continue;
                }
                Stun(st, seat, ref r, ref c, kind, od.Stun);
                return;
            }
        }

        static Fixed PoleStun(StreetMap street)
        {
            // Фонтан «как со столбом»: стан столба из данных улицы, при отсутствии — 0.6 с.
            for (int i = 0; i < street.Obstacles.Length; i++)
                if (street.Obstacles[i].Def.Id == "pole") return street.Obstacles[i].Def.Stun;
            return Fixed.FromFraction(3, 5);
        }

        static void Stun(RaceState st, int seat, ref RunnerState r, ref CollisionState c, CollisionKind kind, Fixed stunSec)
        {
            Record(st, ref r, ref c, kind);
            r.Speed = 0;
            r.StunTicks = StunTicks(stunSec, Fixed.FromRaw(st.Runners[seat].Stability));
        }

        /// <summary>Тики стана: стан / устойчивость, округление к ближайшему, минимум 1 (0.6 с при 1.0 → 18; при 1.6 → 11).</summary>
        public static int StunTicks(Fixed stunSec, Fixed stability)
        {
            if (stability <= Fixed.Zero) stability = Fixed.One;
            int t = ((stunSec * TicksFx / stability) + Fixed.Half).ToInt();
            return t < 1 ? 1 : t;
        }

        static void Record(RaceState st, ref RunnerState r, ref CollisionState c, CollisionKind kind)
        {
            c.Count++;
            c.LastKind = (int)kind;
            c.LastS = r.S; c.LastX = r.X;
            c.LastTick = st.Tick;
        }

        static CollisionKind KindOf(string id)
        {
            switch (id)
            {
                case "pole": return CollisionKind.Pole;
                case "stall": return CollisionKind.Stall;
                case "barrels": return CollisionKind.Barrels;
                case "laundry": return CollisionKind.Laundry;
                case "door": return CollisionKind.Door;
                case "pots": return CollisionKind.Pots;
                case "gawker": return CollisionKind.Gawker;
                default: return CollisionKind.Other;
            }
        }

        static bool Overlaps(RaceState st, int i, in StreetObstacle o, Fixed s, Fixed x, Fixed rad)
        {
            Fixed half = Fixed.Half;
            ObstacleDef od = o.Def;
            if (od.MotionKind == "walk")
            {
                Fixed ox = Fixed.FromRaw(st.Dyn[i].X);
                return CircleCircle(s, x, rad, o.S, ox, od.Size * half);
            }
            if (od.MotionKind == "door")
            {
                Fixed ow = MovingObstacles.DoorOpenWidth(o, st.Dyn[i], st.Tick);
                if (ow <= Fixed.Zero) return false;
                Fixed cx = o.X >= Fixed.Zero
                    ? o.X + o.SizeAcross * half - ow * half   // правая стена: открытая часть у стены
                    : o.X - o.SizeAcross * half + ow * half;
                return CircleRect(s, x, rad, o.S, cx, o.SizeAlong * half, ow * half);
            }
            if (od.Shape == "circle") return CircleCircle(s, x, rad, o.S, o.X, od.Size * half);
            return CircleRect(s, x, rad, o.S, o.X, o.SizeAlong * half, o.SizeAcross * half);
        }

        static bool CircleCircle(Fixed s, Fixed x, Fixed rad, Fixed os, Fixed ox, Fixed orad)
        {
            Fixed rr = rad + orad;
            Fixed dx = x - ox, ds = s - os;
            if (Fixed.Abs(dx) >= rr || Fixed.Abs(ds) >= rr) return false;
            return dx * dx + ds * ds < rr * rr;
        }

        static bool CircleRect(Fixed s, Fixed x, Fixed rad, Fixed os, Fixed ox, Fixed hs, Fixed hx)
        {
            Fixed px = Fixed.Clamp(x, ox - hx, ox + hx);
            Fixed ps = Fixed.Clamp(s, os - hs, os + hs);
            Fixed dx = x - px, ds = s - ps;
            if (Fixed.Abs(dx) >= rad || Fixed.Abs(ds) >= rad) return false;
            return dx * dx + ds * ds < rad * rad;
        }
    }
}
