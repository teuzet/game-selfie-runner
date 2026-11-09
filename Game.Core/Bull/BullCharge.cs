using Game.Core.Math;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Bull
{
    /// <summary>Факты подброса бегуна (по месту) для событий F-019: «подброшен» и «встал». Входит в RaceState и хэш.</summary>
    public struct TossState
    {
        public int NoSignalTicks;   // >0 — «нет сигнала»: вход игнорируется
        public int InvulnTicks;     // неуязвимость после «нет сигнала» (тикает, когда NoSignalTicks == 0)
        public int Tossed;          // сколько раз подбрасывали (факт «подброшен»)
        public int Rose;            // сколько раз «встал» (кончилось «нет сигнала»)
        public int LastTossTick;    // -1 — не было
        public int LastRoseTick;    // -1 — не было
        public long LastTossFromS;  // s бегуна до подброса (raw)
    }

    /// <summary>Состояние рывков быка. Входит в RaceState и хэш.</summary>
    public struct BullChargeState
    {
        public DetRandom Rng;     // собственный поток быка: интервалы и выбор цели
        public int Cooldown;      // тиков до следующего рывка (-1 — ещё не назначен, бык в загоне)
        public int ChargeLeft;    // >0 — идёт рывок
        public int Target;        // место цели, -1 — нет
        public int Count;         // начато рывков
        public int Tosses;        // всего подбросов
        public int LastChargeTick, LastChargeTarget; // -1 — не было
    }

    /// <summary>Рывок быка: цель по вкусности, подброс, «нет сигнала» и неуязвимость. Числа — design/overview.md (таблица быка; «Нет сигнала»). Без аллокаций.</summary>
    public static class BullCharge
    {
        // Отдельного JSON нет (GameData вне owned_files F-017), числа — константы с источником.
        public const int MinIntervalSec = 8, MaxIntervalSec = 15; // рывок каждые 8–15 с (старт к старту)
        public const int ChargeSec = 3;                           // рывок длится 3 с
        public static readonly Fixed ChargeSpeed = Fixed.FromInt(11);   // 11 м/с
        public static readonly Fixed Reach = Fixed.One;                 // достал: ≤1 м
        public static readonly Fixed TargetRange = Fixed.FromInt(30);   // цель — впереди в 30 м
        public static readonly Fixed TossDistance = Fixed.FromInt(8);   // подброс на 8 м вперёд
        public const int InvulnSec = 3;                                 // неуязвимость 3 с после «нет сигнала»
        public const int MinTossGapSec = 6;                             // между подбросами одного бегуна ≥ 6 с
        public const int BullSlowSec = 2;                               // бык 2 с на 30% медленнее
        public static readonly Fixed ChargeSteer = Fixed.FromInt(6);    // м/с поперёк в рывке (решение исполнителя F-017)

        const int Tps = Simulation.TicksPerSecond;
        static readonly Fixed Dt = Fixed.FromFraction(1, Tps);

        public static void InitState(ulong seed, ref BullChargeState c)
        {
            c = default;
            c.Rng = new DetRandom(seed, 0xB0117UL);
            c.Cooldown = -1; c.Target = -1;
            c.LastChargeTick = -1; c.LastChargeTarget = -1;
        }

        public static void InitToss(ref TossState t)
        {
            t = default;
            t.LastTossTick = -1; t.LastRoseTick = -1;
        }

        public static bool Protected(in TossState t) => t.NoSignalTicks > 0 || t.InvulnTicks > 0;

        /// <summary>После BullMovement.Step; prevBullS — s быка до этого шага (рывок переопределяет движение).</summary>
        public static void Step(Simulation sim, long prevBullS)
        {
            var st = sim.State;
            var street = sim.Street;
            ref BullState b = ref st.Bull;
            ref BullChargeState c = ref st.Charge;

            for (int i = 0; i < st.Tosses.Length; i++)
            {
                ref TossState t = ref st.Tosses[i];
                if (t.NoSignalTicks > 0)
                {
                    if (--t.NoSignalTicks == 0) { t.Rose++; t.LastRoseTick = st.Tick; }
                }
                else if (t.InvulnTicks > 0) t.InvulnTicks--;
            }

            if (b.Phase == (int)BullPhase.Pen || b.Phase == (int)BullPhase.Arena) return;

            if (c.Cooldown < 0) c.Cooldown = NextInterval(ref c.Rng);
            else if (c.Cooldown > 0) c.Cooldown--;

            if (c.ChargeLeft == 0 && c.Cooldown == 0) TryStartCharge(sim, ref c, b);

            if (c.ChargeLeft > 0)
            {
                if (c.Target < 0 || Protected(st.Tosses[c.Target])) { c.ChargeLeft = 0; c.Target = -1; }
                else MoveCharging(sim, street, ref b, ref c, prevBullS);
            }

            if (b.Phase != (int)BullPhase.Arena) Contacts(sim, street, ref b, ref c);
        }

        static int NextInterval(ref DetRandom rng) => rng.Range(MinIntervalSec * Tps, MaxIntervalSec * Tps + 1);

        static void TryStartCharge(Simulation sim, ref BullChargeState c, in BullState b)
        {
            var st = sim.State;
            Fixed bs = Fixed.FromRaw(b.S), bx = Fixed.FromRaw(b.X);
            Fixed total = Fixed.Zero;
            Fixed w0 = Fixed.Zero, w1 = Fixed.Zero, w2 = Fixed.Zero, w3 = Fixed.Zero;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                Fixed w = Weight(sim, st, i, bs, bx);
                total = total + w;
                switch (i) { case 0: w0 = w; break; case 1: w1 = w; break; case 2: w2 = w; break; default: w3 = w; break; }
            }
            if (total <= Fixed.Zero) return; // целей нет — ждём, пробуем снова на следующем тике
            Fixed pick = c.Rng.NextFixed01() * total;
            int target;
            if (pick < w0) target = 0;
            else if (pick < w0 + w1) target = 1;
            else if (pick < w0 + w1 + w2) target = 2;
            else target = w3 > Fixed.Zero ? 3 : w2 > Fixed.Zero ? 2 : w1 > Fixed.Zero ? 1 : 0; // округление: последний с весом
            c.Target = target;
            c.ChargeLeft = ChargeSec * Tps;
            c.Count++;
            c.LastChargeTick = st.Tick; c.LastChargeTarget = target;
            c.Cooldown = NextInterval(ref c.Rng);
        }

        /// <summary>Вес цели: вкусность / (1 + d/10); 0 — не цель (позади, дальше 30 м, неуязвим).</summary>
        static Fixed Weight(Simulation sim, RaceState st, int seat, Fixed bs, Fixed bx)
        {
            if (Protected(st.Tosses[seat])) return Fixed.Zero;
            Fixed ds = Fixed.FromRaw(st.Runners[seat].S) - bs;
            if (ds < Fixed.Zero || ds > TargetRange) return Fixed.Zero;
            Fixed dx = Fixed.FromRaw(st.Runners[seat].X) - bx;
            Fixed d = Fixed.Sqrt(ds * ds + dx * dx);
            return sim.CharacterOf(seat).Tastiness / (Fixed.One + d / Fixed.FromInt(10));
        }

        static void MoveCharging(Simulation sim, StreetMap street, ref BullState b, ref BullChargeState c, long prevBullS)
        {
            var st = sim.State;
            var bd = sim.BullData;
            Fixed s = Fixed.FromRaw(prevBullS), x = Fixed.FromRaw(b.X), speed = Fixed.FromRaw(b.Speed);
            Fixed mul = street.SlopeAt(s);
            if (street.Blocks[street.BlockIndexAt(s)].Def.Id == "stairs") mul = mul * bd.StairsMul;
            Fixed target = ChargeSpeed * mul;
            Fixed dv = bd.Accel * Dt;
            speed = speed < target ? Fixed.Min(speed + dv, target) : Fixed.Max(speed - dv, target);
            s = s + speed * Dt;

            Fixed diff = Fixed.FromRaw(st.Runners[c.Target].X) - x;
            Fixed step = ChargeSteer * Dt;
            x = x + Fixed.Clamp(diff, -step, step);
            Fixed lim = street.WidthAt(s) / Fixed.FromInt(2) - bd.CapsuleWidth / Fixed.FromInt(2);
            if (lim < Fixed.Zero) lim = Fixed.Zero;
            x = Fixed.Clamp(x, -lim, lim);

            b.Phase = (int)BullPhase.Charge;
            if (street.Blocks[street.BlockIndexAt(s)].Def.Id == "arena")
            {
                b.Phase = (int)BullPhase.Arena; speed = Fixed.Zero; b.SlowTicks = 0; c.ChargeLeft = 0; c.Target = -1;
            }
            else if (--c.ChargeLeft == 0) c.Target = -1;
            b.S = s.Raw; b.X = x.Raw; b.Speed = speed.Raw;
        }

        /// <summary>Достал (≤1 м) — подброс; позади быка — подброс (или бык стоит у неуязвимого): никого позади быка.</summary>
        static void Contacts(Simulation sim, StreetMap street, ref BullState b, ref BullChargeState c)
        {
            var st = sim.State;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                ref RunnerState r = ref st.Runners[i];
                long dsRaw = r.S - b.S;
                if (Protected(st.Tosses[i]))
                {
                    if (dsRaw < 0) { b.S = r.S; if (b.Speed > 0) b.Speed = 0; } // неуязвимого не обгоняем и не подбрасываем
                    continue;
                }
                bool hit = dsRaw < 0;
                if (!hit)
                {
                    Fixed ds = Fixed.FromRaw(dsRaw), dx = Fixed.FromRaw(r.X - b.X);
                    hit = ds * ds + dx * dx <= Reach * Reach;
                }
                if (hit) Toss(sim, street, ref b, ref c, i);
            }
        }

        static void Toss(Simulation sim, StreetMap street, ref BullState b, ref BullChargeState c, int seat)
        {
            var st = sim.State;
            ref RunnerState r = ref st.Runners[seat];
            ref TossState t = ref st.Tosses[seat];
            t.LastTossFromS = r.S;
            long baseS = r.S > b.S ? r.S : b.S;
            long ns = baseS + TossDistance.Raw;
            if (ns > street.Length.Raw) ns = street.Length.Raw;
            r.S = ns;
            int noSignal = (sim.CharacterOf(seat).NoSignal * Fixed.FromInt(Tps)).ToInt();
            int invuln = InvulnSec * Tps;
            if (noSignal + invuln < MinTossGapSec * Tps) invuln = MinTossGapSec * Tps - noSignal; // у атлета noSignal 2.5 с — добираем до 6 с
            t.NoSignalTicks = noSignal;
            t.InvulnTicks = invuln;
            t.Tossed++;
            t.LastTossTick = st.Tick;
            c.Tosses++;
            c.ChargeLeft = 0; c.Target = -1;
            BullMovement.Slow(ref b, BullSlowSec * Tps);
        }
    }
}
