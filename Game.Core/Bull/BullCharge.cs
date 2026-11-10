using Game.Core.Math;
using Game.Core.Runners;
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
        public int LastCause;       // TossCause последнего подброса (0 — не было)

        public ulong Hash(ulong h)
        {
            h = Hashing.Mix(h, (ulong)(uint)LastCause);
            h = Hashing.Mix(h, (ulong)(uint)NoSignalTicks); h = Hashing.Mix(h, (ulong)(uint)InvulnTicks);
            h = Hashing.Mix(h, (ulong)(uint)Tossed); h = Hashing.Mix(h, (ulong)(uint)Rose);
            h = Hashing.Mix(h, (ulong)(uint)LastTossTick); h = Hashing.Mix(h, (ulong)(uint)LastRoseTick);
            return Hashing.Mix(h, (ulong)LastTossFromS);
        }
    }

    /// <summary>Причина подброса (F-100 §3.1 п.1; Param1 события Tossed): бык был в рывке / контакт вне рывка.</summary>
    public static class TossCause { public const int Charge = 1, Contact = 2; }

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

        public ulong Hash(ulong h)
        {
            h = Hashing.Mix(h, Rng.State); h = Hashing.Mix(h, Rng.Inc);
            h = Hashing.Mix(h, (ulong)(uint)Cooldown); h = Hashing.Mix(h, (ulong)(uint)ChargeLeft);
            h = Hashing.Mix(h, (ulong)(uint)Target); h = Hashing.Mix(h, (ulong)(uint)Count);
            h = Hashing.Mix(h, (ulong)(uint)Tosses); h = Hashing.Mix(h, (ulong)(uint)LastChargeTick);
            return Hashing.Mix(h, (ulong)(uint)LastChargeTarget);
        }
    }

    /// <summary>Рывок быка: цель по вкусности, подброс, «нет сигнала» и неуязвимость. Числа — Data/bull.json (BullData.Charge*). Без аллокаций.</summary>
    public static class BullCharge
    {
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

        /// <summary>F-100 §3.1 п.2: защищённый — лежащий/неуязвимый, на пороге арены (последние chargeTossDistance м перед ней) и на арене, финишировавший.</summary>
        public static bool Shielded(Simulation sim, int seat)
        {
            ref RunnerState r = ref sim.State.Runners[seat];
            return Protected(r.Toss) || r.Viewing.FinishTick >= 0
                || r.S >= sim.ArenaStart.Raw - sim.BullData.ChargeTossDistance.Raw;
        }

        /// <summary>Бык за тик (шаг 6 порядка): движение с резинкой, затем рывок и подброс.</summary>
        public static void StepBull(Simulation sim)
        {
            long prevBullS = sim.State.Bull.S, prevSpeed = sim.State.Bull.Speed, prevX = sim.State.Bull.X;
            BullMovement.Step(sim.BullData, sim.Street, ref sim.State.Bull, sim.State.Runners, sim.State.Tick);
            Step(sim, prevBullS, prevSpeed, prevX);
        }

        /// <summary>
        /// После BullMovement.Step; prevBullS, prevSpeed и prevX — s, скорость и x быка до этого шага: рывок переопределяет движение
        /// и стартует от них, а не от скорости и x, уже сдвинутых резинкой (крейсер, доворот 3 м/с) — F-081.
        /// </summary>
        public static void Step(Simulation sim, long prevBullS, long prevSpeed, long prevX)
        {
            var st = sim.State;
            var street = sim.Street;
            ref BullState b = ref st.Bull;
            ref BullChargeState c = ref st.Charge;

            for (int i = 0; i < st.Runners.Length; i++)
            {
                ref TossState t = ref st.Runners[i].Toss;
                if (t.NoSignalTicks > 0)
                {
                    if (--t.NoSignalTicks == 0)
                    {
                        t.Rose++; t.LastRoseTick = st.Tick;
                        RunnerMovement.Rise(sim.SeatData(i), street, ref st.Runners[i]); // встаёт сразу в бег
                    }
                }
                else if (t.InvulnTicks > 0) t.InvulnTicks--;
            }

            if (b.Phase == (int)BullPhase.Pen || b.Phase == (int)BullPhase.Arena) return;

            if (c.Cooldown < 0) c.Cooldown = NextInterval(sim.BullData, ref c.Rng);
            else if (c.Cooldown > 0) c.Cooldown--;

            if (c.ChargeLeft == 0 && c.Cooldown == 0) TryStartCharge(sim, ref c, b);

            if (c.ChargeLeft > 0)
            {
                if (c.Target < 0 || Shielded(sim, c.Target)) { c.ChargeLeft = 0; c.Target = -1; } // рывок на ставшего защищённым прекращается
            }
            // цель рывка в этом тике (даже если рывок кончится в этом же тике): только её подброс имеет причину «рывок»
            int chargeTarget = c.ChargeLeft > 0 ? c.Target : -1;
            if (chargeTarget >= 0)
            {
                MoveCharging(sim, street, ref b, ref c, prevBullS, prevSpeed, prevX);
                if (b.Phase == (int)BullPhase.Charge) Miss(sim, ref b, ref c, prevBullS);
            }

            if (b.Phase != (int)BullPhase.Arena) Contacts(sim, street, ref b, ref c, prevBullS, chargeTarget);
        }

        static int NextInterval(BullData bd, ref DetRandom rng) => rng.Range(bd.ChargeMinIntervalSec * Tps, bd.ChargeMaxIntervalSec * Tps + 1);

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
            if (total <= Fixed.Zero) { c.Cooldown = NextInterval(sim.BullData, ref c.Rng); return; } // F-100 §3.1 п.6: целей нет — рывок пропущен, следующий через новый интервал
            Fixed pick = c.Rng.NextFixed01() * total;
            int target;
            if (pick < w0) target = 0;
            else if (pick < w0 + w1) target = 1;
            else if (pick < w0 + w1 + w2) target = 2;
            else target = w3 > Fixed.Zero ? 3 : w2 > Fixed.Zero ? 2 : w1 > Fixed.Zero ? 1 : 0; // округление: последний с весом
            c.Target = target;
            c.ChargeLeft = sim.BullData.ChargeSec * Tps;
            c.Count++;
            c.LastChargeTick = st.Tick; c.LastChargeTarget = target;
            c.Cooldown = NextInterval(sim.BullData, ref c.Rng);
        }

        /// <summary>Вес цели: вкусность / (1 + d/10); 0 — не цель (позади, дальше 30 м, защищён, «бык наелся»: охлаждение после подброса, F-100 §3.1 п.6).</summary>
        static Fixed Weight(Simulation sim, RaceState st, int seat, Fixed bs, Fixed bx)
        {
            if (Shielded(sim, seat)) return Fixed.Zero;
            int lastToss = st.Runners[seat].Toss.LastTossTick;
            if (lastToss >= 0 && st.Tick - lastToss < sim.BullData.TossCooldownSec * Tps) return Fixed.Zero;
            Fixed ds = Fixed.FromRaw(st.Runners[seat].S) - bs;
            if (ds < Fixed.Zero || ds > sim.BullData.ChargeTargetRange) return Fixed.Zero;
            // F-102 §3.2 п.1: вес 0, если цель дальше эффективной досягаемости (chargeSpeed − скорость цели) × chargeSec + chargeReach.
            Fixed reach = (sim.BullData.ChargeSpeed - Fixed.FromRaw(st.Runners[seat].Speed)) * Fixed.FromInt(sim.BullData.ChargeSec) + sim.BullData.ChargeReach;
            if (ds > reach) return Fixed.Zero;
            Fixed dx = Fixed.FromRaw(st.Runners[seat].X) - bx;
            Fixed d = Fixed.Sqrt(ds * ds + dx * dx);
            return sim.CharacterOf(seat).Tastiness / (Fixed.One + d / Fixed.FromInt(10));
        }

        static void MoveCharging(Simulation sim, StreetMap street, ref BullState b, ref BullChargeState c, long prevBullS, long prevSpeed, long prevX)
        {
            var st = sim.State;
            var bd = sim.BullData;
            Fixed s = Fixed.FromRaw(prevBullS), x = Fixed.FromRaw(prevX), speed = Fixed.FromRaw(prevSpeed);
            Fixed mul = street.SlopeAt(s);
            if (street.Blocks[street.BlockIndexAt(s)].Def.Id == "stairs") mul = mul * bd.StairsMul;
            Fixed target = bd.ChargeSpeed * mul;
            Fixed dv = bd.Accel * Dt;
            speed = speed < target ? Fixed.Min(speed + dv, target) : Fixed.Max(speed - dv, target);
            s = s + speed * Dt;

            Fixed diff = Fixed.FromRaw(st.Runners[c.Target].X) - x;
            Fixed step = bd.ChargeSteer * Dt;
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

        /// <summary>
        /// F-100 §3.2 п.3 «Промах»: бык в рывке поравнялся по s с бегуном, который дальше chargeReach поперёк, — рывок кончается:
        /// бык встаёт вровень (не обгоняет, идёт не быстрее него), chargeMissSlowSec идёт медленнее и chargeMissNoSteerSec не доворачивает.
        /// Достанувшего (≤ chargeReach) подбросит Contacts этого же тика.
        /// </summary>
        static void Miss(Simulation sim, ref BullState b, ref BullChargeState c, long prevBullS)
        {
            var st = sim.State;
            var bd = sim.BullData;
            Fixed reach = bd.ChargeReach;
            // с кем бык поравнялся в этом тике: бегун с минимальным s в [prevBullS, b.S], дальше reach поперёк
            int hit = -1;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                ref RunnerState r = ref st.Runners[i];
                if (Shielded(sim, i) || r.S < prevBullS || r.S > b.S) continue;
                Fixed dx = Fixed.FromRaw(r.X - b.X);
                if (dx * dx <= reach * reach) continue;
                if (hit < 0 || r.S < st.Runners[hit].S) hit = i;
            }
            if (hit < 0) return;
            ref RunnerState m = ref st.Runners[hit];
            b.S = m.S;
            if (b.Speed > m.Speed) b.Speed = m.Speed > 0 ? m.Speed : 0;
            c.ChargeLeft = 0; c.Target = -1;
            BullMovement.Slow(ref b, (bd.ChargeMissSlowSec * Fixed.FromInt(Tps)).ToInt());
            b.NoSteerTicks = (bd.ChargeMissNoSteerSec * Fixed.FromInt(Tps)).ToInt();
        }

        /// <summary>
        /// F-100 §3.1: защищённого бык не подбрасывает и держится в tossWaitGap м позади него (не паркуется на его s и не обходит);
        /// достал (≤1 м) или бегун позади быка — подброс: никого позади быка. Защита раньше контактов.
        /// </summary>
        static void Contacts(Simulation sim, StreetMap street, ref BullState b, ref BullChargeState c, long prevBullS, int chargeTarget)
        {
            var st = sim.State;
            Fixed reach = sim.BullData.ChargeReach;
            long gap = sim.BullData.TossWaitGap.Raw;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                ref RunnerState r = ref st.Runners[i];
                long dsRaw = r.S - b.S;
                if (Shielded(sim, i))
                {
                    // впереди и ближе отступа (финишировавший/арена — сами не держим: бык там останавливается по BullMovement)
                    if (r.Viewing.FinishTick < 0 && r.S < sim.ArenaStart.Raw && dsRaw >= 0 && dsRaw < gap)
                    {
                        long hold = r.S - gap;
                        if (hold < prevBullS) hold = prevBullS;
                        if (b.S > hold) b.S = hold;
                        if (b.Speed > r.Speed) b.Speed = r.Speed > 0 ? r.Speed : 0;
                    }
                    continue;
                }
                bool hit = dsRaw < 0;
                if (!hit)
                {
                    Fixed ds = Fixed.FromRaw(dsRaw), dx = Fixed.FromRaw(r.X - b.X);
                    hit = ds * ds + dx * dx <= reach * reach;
                }
                if (hit) Toss(sim, street, ref b, ref c, i, i == chargeTarget ? TossCause.Charge : TossCause.Contact);
            }
        }

        static void Toss(Simulation sim, StreetMap street, ref BullState b, ref BullChargeState c, int seat, int cause)
        {
            var st = sim.State;
            var bd = sim.BullData;
            ref RunnerState r = ref st.Runners[seat];
            ref TossState t = ref r.Toss;
            t.LastTossFromS = r.S;
            long baseS = r.S > b.S ? r.S : b.S;
            long ns = baseS + bd.ChargeTossDistance.Raw;
            if (ns > street.Length.Raw) ns = street.Length.Raw;
            r.S = ns;
            int noSignal = (sim.CharacterOf(seat).NoSignal * Fixed.FromInt(Tps)).ToInt();
            int invuln = bd.ChargeInvulnSec * Tps;
            int gap = bd.ChargeTossGapSec * Tps;
            if (noSignal + invuln < gap) invuln = gap - noSignal; // у атлета noSignal 2.5 с — добираем до 6 с
            t.NoSignalTicks = noSignal;
            t.InvulnTicks = invuln;
            t.Tossed++;
            t.LastTossTick = st.Tick;
            t.LastCause = cause;
            c.Tosses++;
            c.ChargeLeft = 0; c.Target = -1;
            BullMovement.Slow(ref b, bd.ChargeBullSlowSec * Tps);
        }
    }
}
