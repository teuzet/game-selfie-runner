using Game.Core.Events;
using Game.Core.Math;
using Game.Core.Runners;

namespace Game.Core.Economy
{
    /// <summary>«Влез в кадр» по месту-вору. Входит в RaceState и хэш.</summary>
    public struct FrameState
    {
        public int Victim;  // сосед, перед которым вор в кадре; -1 — никого
        public int Ticks;   // сколько тиков подряд
    }

    /// <summary>«Влез в кадр»: ≤2 м позади соседа дольше 1 с тянет 6 зрителей/с (design/overview.md, «Зрители и донаты»).
    /// У жертвы с перком GlamourLoyalViewers × perkValue. Зрители переливаются от жертвы вору. Без аллокаций.</summary>
    public static class FrameSteal
    {
        public static readonly Fixed Range = Fixed.FromInt(2);   // ≤2 м (евклидово расстояние, вор позади жертвы по s)
        public const int HoldTicks = Simulation.TicksPerSecond;  // дольше 1 с
        public static readonly Fixed Rate = Fixed.FromInt(6);    // 6 зрителей/с (тюнить)

        public static void InitState(ref FrameState f) { f.Victim = -1; f.Ticks = 0; }

        /// <summary>Зрителей/с, которые вор тянет у жертвы.</summary>
        public static Fixed RateFor(Simulation sim, int victim)
        {
            var c = sim.CharacterOf(victim);
            return c.Perk == Perk.GlamourLoyalViewers ? Rate * c.PerkValue : Rate;
        }

        public static void Step(Simulation sim)
        {
            var st = sim.State;
            var ev = sim.Events;
            var d = sim.EconomyData;
            for (int thief = 0; thief < RaceConfig.SeatCount; thief++)
            {
                ref FrameState f = ref st.Frame[thief];
                int victim = Find(st, thief);
                if (victim < 0) { f.Victim = -1; f.Ticks = 0; continue; }
                if (victim != f.Victim) { f.Victim = victim; f.Ticks = 0; }
                f.Ticks++;
                if (f.Ticks <= HoldTicks) continue;
                Fixed rate = RateFor(sim, victim);
                if (f.Ticks == HoldTicks + 1)
                {
                    ev.Add(RaceEventType.FramedIn, st.Tick, thief, victim, (int)rate.Raw);
                    ev.Add(RaceEventType.InYourFrame, st.Tick, victim, thief, (int)rate.Raw);
                }
                long amount = rate.Raw / Simulation.TicksPerSecond;
                ref ViewerState vv = ref st.Viewing[victim];
                ref ViewerState tv = ref st.Viewing[thief];
                if (amount > vv.Viewers) amount = vv.Viewers;
                if (amount > d.Cap.Raw - tv.Viewers) amount = d.Cap.Raw - tv.Viewers;
                if (amount < 0) amount = 0;
                vv.Viewers -= amount;
                tv.Viewers += amount;
            }
        }

        /// <summary>Ближайший сосед впереди вора в пределах Range; -1 если нет. Ничья — меньшее место.</summary>
        static int Find(RaceState st, int thief)
        {
            int best = -1; Fixed bestD = Fixed.Zero;
            Fixed ts = Fixed.FromRaw(st.Runners[thief].S), tx = Fixed.FromRaw(st.Runners[thief].X);
            Fixed rr = Range * Range;
            for (int k = 0; k < RaceConfig.SeatCount; k++)
            {
                if (k == thief) continue;
                Fixed ds = Fixed.FromRaw(st.Runners[k].S) - ts;
                if (ds <= Fixed.Zero) continue;
                Fixed dx = Fixed.FromRaw(st.Runners[k].X) - tx;
                Fixed d2 = ds * ds + dx * dx;
                if (d2 > rr) continue;
                if (best < 0 || d2 < bestD) { best = k; bestD = d2; }
            }
            return best;
        }
    }
}
