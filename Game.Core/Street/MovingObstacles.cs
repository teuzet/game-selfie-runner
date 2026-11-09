using Game.Core.Math;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Street
{
    /// <summary>Динамика подвижного препятствия (индекс = индекс в Street.Obstacles). Входит в RaceState и хэш.</summary>
    public struct ObstacleDyn
    {
        public long X;        // зевака: текущий x
        public int Dir;       // зевака: ±1
        public int OpenTick;  // дверь: тик начала открытия, -1 — закрыта
    }

    /// <summary>Подвижные препятствия: дверь (открывается по порогу), зевака (ходит поперёк). design/F-003.md §2. Без аллокаций.</summary>
    public static class MovingObstacles
    {
        static readonly Fixed Dt = Fixed.FromFraction(1, Simulation.TicksPerSecond);

        /// <summary>Хэш состояния модуля Street: динамика подвижных препятствий и флаги «уже действовало» (Hit).</summary>
        public static ulong Hash(ulong h, ObstacleDyn[] dyn, byte[] hit)
        {
            for (int i = 0; i < dyn.Length; i++)
            {
                h = Hashing.Mix(h, (ulong)dyn[i].X);
                h = Hashing.Mix(h, (ulong)(uint)dyn[i].Dir);
                h = Hashing.Mix(h, (ulong)(uint)dyn[i].OpenTick);
            }
            for (int i = 0; i < hit.Length; i++) h = Hashing.Mix(h, hit[i]);
            return h;
        }

        public static void Init(StreetMap street, ObstacleDyn[] dyn)
        {
            for (int i = 0; i < street.Obstacles.Length; i++)
            {
                ref readonly StreetObstacle o = ref street.Obstacles[i];
                dyn[i].OpenTick = -1;
                dyn[i].X = o.X.Raw;
                dyn[i].Dir = o.Def.MotionKind == "walk" ? (o.Param.Raw < 0 ? -1 : 1) : 0;
            }
        }

        /// <summary>Один тик: двери (триггер по первому подошедшему бегуну), зеваки (шаг и разворот).</summary>
        public static void Step(StreetMap street, ObstacleDyn[] dyn, RunnerState[] runners, int tick)
        {
            var obs = street.Obstacles;
            for (int i = 0; i < obs.Length; i++)
            {
                ref readonly StreetObstacle o = ref obs[i];
                if (o.Def.MotionKind == "door")
                {
                    if (dyn[i].OpenTick >= 0) continue;
                    for (int k = 0; k < runners.Length; k++)
                    {
                        Fixed d = o.S - Fixed.FromRaw(runners[k].S); // до двери вдоль s
                        if (d >= Fixed.Zero && d <= o.Param) { dyn[i].OpenTick = tick; break; }
                    }
                }
                else if (o.Def.MotionKind == "walk")
                {
                    GawkerLimits(street, o, out Fixed lo, out Fixed hi);
                    Fixed x = Fixed.FromRaw(dyn[i].X) + Fixed.FromInt(dyn[i].Dir) * o.Def.Speed * Dt;
                    if (x >= hi) { x = hi; dyn[i].Dir = -1; }
                    else if (x <= lo) { x = lo; dyn[i].Dir = 1; }
                    dyn[i].X = x.Raw;
                }
            }
        }

        /// <summary>Крайние x центра зеваки: 0.5 м от стены (в пруду — от кромки воды). Та же формула, что в генераторе.</summary>
        public static void GawkerLimits(StreetMap street, in StreetObstacle o, out Fixed lo, out Fixed hi)
        {
            Fixed half = Fixed.Half;
            Fixed w = street.MinWidthOn(o.S - o.SizeAlong * half, o.S + o.SizeAlong * half);
            Fixed lim = w * half - o.Def.WallGap - o.Def.Size * half;
            lo = -lim; hi = lim;
            ref readonly PlacedBlock pb = ref street.Blocks[street.BlockIndexAt(o.S)];
            if (pb.WaterSide == 0) return;
            for (int f = 0; f < pb.Def.Fixed.Length; f++)
            {
                var fe = pb.Def.Fixed[f];
                if (fe.Kind != "water") continue;
                Fixed loc = o.S - pb.Start;
                if (loc < fe.S0 - o.Def.Size || loc > fe.S1 + o.Def.Size) continue;
                Fixed edge = w * half - fe.Across - o.Def.WallGap - o.Def.Size * half;
                if (pb.WaterSide > 0) hi = Fixed.Min(hi, edge); else lo = Fixed.Max(lo, -edge);
            }
            if (lo > hi) { lo = Fixed.Zero; hi = Fixed.Zero; }
        }

        /// <summary>Открытая ширина двери в тик <paramref name="tick"/>: 0 → SizeAcross за OpenTime. 0 — закрыта/только открывается.</summary>
        public static Fixed DoorOpenWidth(in StreetObstacle o, in ObstacleDyn dyn, int tick)
        {
            if (dyn.OpenTick < 0) return Fixed.Zero;
            int total = (o.Def.OpenTime * Fixed.FromInt(Simulation.TicksPerSecond)).ToInt();
            if (total < 1) total = 1;
            int el = tick - dyn.OpenTick;
            if (el >= total) return o.SizeAcross;
            if (el <= 0) return Fixed.Zero;
            return o.SizeAcross * Fixed.FromInt(el) / Fixed.FromInt(total);
        }
    }
}
