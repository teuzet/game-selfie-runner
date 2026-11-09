using System;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core.Math;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Bull
{
    /// <summary>Числа быка из Data/bull.json.</summary>
    public sealed class BullData
    {
        public Fixed PenOffset, LineS, CountdownSec, ReleaseDelaySec, ChargeSpeed, CruiseSpeed, CloseSpeed,
            TargetBehind, LagFar, LagNear, StairsMul, SlowedMul, CapsuleRadius, Accel, SteerSpeed;

        public static BullData Parse(string json) => new BullData
        {
            PenOffset = Num(json, "penOffset"), LineS = Num(json, "lineS"), CountdownSec = Num(json, "countdownSec"),
            ReleaseDelaySec = Num(json, "releaseDelaySec"), ChargeSpeed = Num(json, "chargeSpeed"),
            CruiseSpeed = Num(json, "cruiseSpeed"), CloseSpeed = Num(json, "closeSpeed"),
            TargetBehind = Num(json, "targetBehind"), LagFar = Num(json, "lagFar"), LagNear = Num(json, "lagNear"),
            StairsMul = Num(json, "stairsMul"), SlowedMul = Num(json, "slowedMul"),
            CapsuleRadius = Num(json, "capsuleRadius"), Accel = Num(json, "accel"), SteerSpeed = Num(json, "steerSpeed"),
        };

        public static BullData Load(string? path = null)
        {
            if (path == null)
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    string c = Path.Combine(dir, "Game.Core", "Data", "bull.json");
                    if (File.Exists(c)) { path = c; break; }
                    c = Path.Combine(dir, "Data", "bull.json");
                    if (File.Exists(c)) { path = c; break; }
                    dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
                if (path == null) throw new FileNotFoundException("Game.Core/Data/bull.json not found");
            }
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        private static Fixed Num(string json, string key)
        {
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) throw new FormatException("bull.json: нет ключа " + key);
            int p = json.IndexOf(':', k) + 1;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            int e = p;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '.' || json[e] == '-')) e++;
            string t = json.Substring(p, e - p);
            if (t.Length == 0) throw new FormatException("bull.json: ключ " + key + " не число");
            bool neg = t[0] == '-';
            if (neg) t = t.Substring(1);
            int dot = t.IndexOf('.');
            string ip = dot < 0 ? t : t.Substring(0, dot);
            string fp = dot < 0 ? "" : t.Substring(dot + 1);
            Fixed v = Fixed.FromInt(int.Parse(ip, CultureInfo.InvariantCulture));
            if (fp.Length > 0)
            {
                long den = 1;
                for (int i = 0; i < fp.Length; i++) den *= 10;
                long num = long.Parse(fp, CultureInfo.InvariantCulture);
                v = v + Fixed.FromRaw((num * Fixed.OneRaw) / den);
            }
            return neg ? -v : v;
        }
    }

    /// <summary>Движение быка: загон, срыв, резинка к толпе, замедление, остановка на арене. Без аллокаций.</summary>
    public static class BullMovement
    {
        private static readonly Fixed Dt = Fixed.FromFraction(1, Simulation.TicksPerSecond);

        /// <summary>Загон: 25 м позади линии; срыв через countdown + releaseDelay после старта забега.</summary>
        public static void Init(BullData d, ref BullState b)
        {
            b.S = (d.LineS - d.PenOffset).Raw;
            b.X = 0;
            b.Speed = 0;
            b.Phase = (int)BullPhase.Pen;
            b.Timer = ((d.CountdownSec + d.ReleaseDelaySec) * Fixed.FromInt(Simulation.TicksPerSecond)).ToInt();
            b.SlowTicks = 0;
        }

        /// <summary>Внешнее замедление (после подброса бегуна): скорость ×slowedMul на ticks тиков.</summary>
        public static void Slow(ref BullState b, int ticks)
        {
            if (b.Phase == (int)BullPhase.Pen || b.Phase == (int)BullPhase.Arena) return;
            if (ticks > b.SlowTicks) b.SlowTicks = ticks;
        }

        /// <summary>Медиана S четырёх бегунов (среднее двух средних), без аллокаций.</summary>
        public static Fixed MedianS(RunnerState[] r)
        {
            long a = r[0].S, b = r[1].S, c = r[2].S, e = r[3].S, t;
            if (a > b) { t = a; a = b; b = t; }
            if (c > e) { t = c; c = e; e = t; }
            if (a > c) { t = a; a = c; c = t; }
            if (b > e) { t = b; b = e; e = t; }
            // a — min, e — max; средние b и c
            long m1 = b < c ? b : c, m2 = b < c ? c : b;
            return Fixed.FromRaw(m1 + (m2 - m1) / 2);
        }

        public static void Step(BullData d, StreetMap street, ref BullState b, RunnerState[] runners)
        {
            if (b.Phase == (int)BullPhase.Arena) { b.Speed = 0; return; }
            if (b.Phase == (int)BullPhase.Pen)
            {
                if (--b.Timer > 0) return;
                b.Timer = 0;
                b.Phase = (int)BullPhase.Charge;
            }

            Fixed s = Fixed.FromRaw(b.S), x = Fixed.FromRaw(b.X), speed = Fixed.FromRaw(b.Speed);
            Fixed lag = MedianS(runners) - s; // отставание от медианы толпы

            // Резинка: пороги отставания 25 / сближения 6 считаются от медианы, цель — 12 м позади неё.
            Fixed baseSpeed;
            if (lag > d.LagFar) baseSpeed = d.ChargeSpeed;
            else if (lag < d.LagNear) baseSpeed = d.CloseSpeed;
            else baseSpeed = d.CruiseSpeed;

            if (b.Phase == (int)BullPhase.Charge && lag <= d.LagFar) b.Phase = (int)BullPhase.Cruise;

            // Уклон блока и лестница действуют на все скорости быка.
            Fixed mul = street.SlopeAt(s);
            if (street.Blocks[street.BlockIndexAt(s)].Def.Id == "stairs") mul = mul * d.StairsMul;
            Fixed target = baseSpeed * mul;

            if (b.SlowTicks > 0)
            {
                b.SlowTicks--;
                target = target * d.SlowedMul;
                b.Phase = (int)BullPhase.Slowed;
            }
            else if (b.Phase == (int)BullPhase.Slowed) b.Phase = (int)BullPhase.Cruise;

            Fixed dv = d.Accel * Dt;
            if (speed < target) speed = Fixed.Min(speed + dv, target);
            else speed = Fixed.Max(speed - dv, target);
            s = s + speed * Dt;

            // x тянется к ближайшему бегуну; те, что позади быка, считаются вдвое дальше.
            int best = -1; long bestD = long.MaxValue;
            for (int i = 0; i < runners.Length; i++)
            {
                long ds = runners[i].S - s.Raw; if (ds < 0) ds = -ds * 2;
                long dx = runners[i].X - x.Raw; if (dx < 0) dx = -dx;
                long dist = ds + dx;
                if (dist < bestD) { bestD = dist; best = i; }
            }
            if (best >= 0)
            {
                Fixed diff = Fixed.FromRaw(runners[best].X) - x;
                Fixed step = d.SteerSpeed * Dt;
                x = x + Fixed.Clamp(diff, -step, step);
            }
            Fixed lim = street.WidthAt(s) / Fixed.FromInt(2) - d.CapsuleRadius;
            if (lim < Fixed.Zero) lim = Fixed.Zero;
            x = Fixed.Clamp(x, -lim, lim);

            // Арена: вход в блок arena — остановка.
            if (street.Blocks[street.BlockIndexAt(s)].Def.Id == "arena")
            {
                b.Phase = (int)BullPhase.Arena;
                speed = Fixed.Zero;
                b.SlowTicks = 0;
            }

            b.S = s.Raw; b.X = x.Raw; b.Speed = speed.Raw;
        }
    }
}
