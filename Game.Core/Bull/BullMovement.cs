using System;
using System.IO;
using System.Text;
using Game.Core.Data;
using Game.Core.Math;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Bull
{
    /// <summary>Числа быка из Data/bull.json.</summary>
    public sealed class BullData
    {
        public Fixed PenOffset, LineS, CountdownSec, ReleaseDelaySec, FinishSec, CatchUpSpeed, CruiseSpeed, CloseSpeed,
            TargetBehind, LagFar, LagNear, StairsMul, SlowedMul, CapsuleWidth, Accel, SteerSpeed;

        // Рывок быка (BullCharge): секунды — целые, дистанции и скорости — Fixed.
        public int ChargeMinIntervalSec, ChargeMaxIntervalSec, ChargeSec, ChargeInvulnSec, ChargeTossGapSec, ChargeBullSlowSec, TossCooldownSec;
        public Fixed ChargeSpeed, ChargeReach, ChargeTargetRange, ChargeTossDistance, ChargeSteer;
        public Fixed TossWaitGap, CatchUpClear, AnchorLag; // F-100 §3.1: отступ от защищённого, порог резинки; AnchorLag — порог снятия якоря, F-102 §3.1
        public Fixed ChargeMissSlowSec, ChargeMissNoSteerSec; // F-100 §4 «промах»: замедление быка и без доворота, секунды (дробные)

        public const string FileName = "bull.json";

        public static BullData Parse(string json)
        {
            var j = Json.Parse(FileName, json, truncate: true);
            return new BullData
            {
                PenOffset = j.Num("penOffset"), LineS = j.Num("lineS"), CountdownSec = j.Num("countdownSec"),
                ReleaseDelaySec = j.Num("releaseDelaySec"), FinishSec = j.Num("finishSec"),
                CatchUpSpeed = j.Num("catchUpSpeed"),
                CruiseSpeed = j.Num("cruiseSpeed"), CloseSpeed = j.Num("closeSpeed"),
                TargetBehind = j.Num("targetBehind"), LagFar = j.Num("lagFar"), LagNear = j.Num("lagNear"),
                StairsMul = j.Num("stairsMul"), SlowedMul = j.Num("slowedMul"),
                CapsuleWidth = j.Num("capsuleWidth"), Accel = j.Num("accel"), SteerSpeed = j.Num("steerSpeed"),
                ChargeMinIntervalSec = j.Int("chargeMinIntervalSec"), ChargeMaxIntervalSec = j.Int("chargeMaxIntervalSec"),
                ChargeSec = j.Int("chargeSec"), ChargeSpeed = j.Num("chargeSpeed"), ChargeReach = j.Num("chargeReach"),
                ChargeTargetRange = j.Num("chargeTargetRange"), ChargeTossDistance = j.Num("chargeTossDistance"),
                ChargeInvulnSec = j.Int("chargeInvulnSec"), ChargeTossGapSec = j.Int("chargeTossGapSec"),
                ChargeBullSlowSec = j.Int("chargeBullSlowSec"), ChargeSteer = j.Num("chargeSteer"),
                TossWaitGap = j.Num("tossWaitGap"), TossCooldownSec = j.Int("tossCooldownSec"), CatchUpClear = j.Num("catchUpClear"), AnchorLag = j.Num("anchorLag"),
                ChargeMissSlowSec = j.Num("chargeMissSlowSec"), ChargeMissNoSteerSec = j.Num("chargeMissNoSteerSec"),
            };
        }

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
    }

    /// <summary>Движение быка: загон, срыв, резинка к толпе, замедление, остановка на арене. Без аллокаций.</summary>
    public static class BullMovement
    {
        private static readonly Fixed Dt = Fixed.FromFraction(1, Simulation.TicksPerSecond);

        /// <summary>Линия старта бегунов: lineS метров от начала блока start улицы.</summary>
        public static Fixed StartLine(BullData d, StreetMap street) => street.Blocks[0].Start + d.LineS;

        /// <summary>Загон: 25 м позади линии старта (line); срыв через countdown + releaseDelay после старта забега.</summary>
        public static void Init(BullData d, Fixed line, ref BullState b)
        {
            b.S = (line - d.PenOffset).Raw;
            b.X = 0;
            b.Speed = 0;
            b.Phase = (int)BullPhase.Pen;
            b.Timer = ((d.CountdownSec + d.ReleaseDelaySec) * Fixed.FromInt(Simulation.TicksPerSecond)).ToInt();
            b.SlowTicks = 0;
            b.Mode = BullMode.None;
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

        /// <param name="tick">Номер тика (для «наелся»: сколько прошло с подброса бегуна).</param>
        public static void Step(BullData d, StreetMap street, ref BullState b, RunnerState[] runners, int tick)
        {
            if (b.Phase == (int)BullPhase.Arena) { b.Speed = 0; return; }
            if (b.Phase == (int)BullPhase.Pen)
            {
                if (--b.Timer > 0) return;
                b.Timer = 0;
                b.Phase = (int)BullPhase.Charge;
                b.Mode = BullMode.Charge; // срыв: рывок, пока не догонит targetBehind
            }

            Fixed s = Fixed.FromRaw(b.S), x = Fixed.FromRaw(b.X), speed = Fixed.FromRaw(b.Speed);
            Fixed lag = MedianS(runners) - s; // отставание от медианы толпы

            // Резинка с гистерезисом к цели targetBehind (решение F-016 в decisions.md):
            // lag > lagFar → рывок 9.5 до lag <= targetBehind; lag < lagNear → 5.0 до lag >= targetBehind; иначе крейсер.
            // F-102 §3.1: якорь — бегун впереди ближе catchUpClear; «наелся» — якорь, подброшенный меньше tossCooldownSec назад.
            bool anchor = false, fed = false;
            for (int i = 0; i < runners.Length; i++)
            {
                long ahead = runners[i].S - s.Raw;
                if (ahead < 0 || ahead >= d.CatchUpClear.Raw) continue;
                anchor = true;
                int lastToss = runners[i].Toss.LastTossTick;
                if (lastToss >= 0 && tick - lastToss < d.TossCooldownSec * Simulation.TicksPerSecond) fed = true;
            }
            bool running = b.Mode == BullMode.Charge || b.Mode == BullMode.Breakthrough;
            if (running && lag <= d.TargetBehind) b.Mode = BullMode.None;
            else if (b.Mode == BullMode.Close && lag >= d.TargetBehind) b.Mode = BullMode.None;
            if (b.Mode == BullMode.None)
            {
                if (lag > d.AnchorLag && anchor && !fed) b.Mode = BullMode.Breakthrough; // F-102 §3.1 п.3: прорыв с порога снятия якоря
                else if (lag > d.LagFar) b.Mode = BullMode.Charge;
                else if (lag < d.LagNear) b.Mode = BullMode.Close;
            }
            Fixed baseSpeed = b.Mode == BullMode.Charge || b.Mode == BullMode.Breakthrough ? d.CatchUpSpeed
                : b.Mode == BullMode.Close ? d.CloseSpeed : d.CruiseSpeed;
            // F-100 §3.1 п.5: резинка не давит бегуна — разгон 9.5 только по пустой улице (никого впереди ближе catchUpClear).
            // Принятое отступление: ограничивается целевая скорость (крейсер), а скорость выше крейсера (после рывка, подъёма)
            // сбрасывается не мгновенно, а через accel — поэтому тест допускает крейсер × 1.2.
            // F-102 §3.1 п.2–4: рывок резинки (Charge) якорь держит на крейсере, пока отставание ≤ anchorLag; прорыв (Breakthrough)
            // якорь не держит до targetBehind; «наелся» бык не продавливает ни в каком режиме — за ним остаётся крейсер.
            if (anchor && (b.Mode == BullMode.Breakthrough ? fed : (b.Mode == BullMode.Charge && (lag <= d.AnchorLag || fed))))
                baseSpeed = d.CruiseSpeed;
            b.Phase = b.Mode == BullMode.Charge ? (int)BullPhase.Charge : (int)BullPhase.Cruise;

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

            Fixed dv = d.Accel * Dt;
            if (speed < target) speed = Fixed.Min(speed + dv, target);
            else speed = Fixed.Max(speed - dv, target);
            s = s + speed * Dt;

            // x тянется к ближайшему бегуну по |ds|+|dx| среди тех, кто не позади быка (ds >= 0):
            // подброшенный/отставший за спину бегун не разворачивает быка.
            int best = -1; long bestD = long.MaxValue;
            for (int i = 0; i < runners.Length; i++)
            {
                long ds = runners[i].S - s.Raw; if (ds < 0) continue;
                long dx = runners[i].X - x.Raw; if (dx < 0) dx = -dx;
                long dist = ds + dx;
                if (dist < bestD) { bestD = dist; best = i; }
            }
            if (b.NoSteerTicks > 0) b.NoSteerTicks--;
            else if (best >= 0)
            {
                Fixed diff = Fixed.FromRaw(runners[best].X) - x;
                Fixed step = d.SteerSpeed * Dt;
                x = x + Fixed.Clamp(diff, -step, step);
            }
            Fixed lim = street.WidthAt(s) / Fixed.FromInt(2) - d.CapsuleWidth / Fixed.FromInt(2); // капсула 1.2 м — ширина (overview): центр не ближе половины ширины к краю
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
