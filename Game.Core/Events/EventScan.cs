using Game.Core.Economy;
using Game.Core.Math;
using Game.Core.Runners;

namespace Game.Core.Events
{
    /// <summary>В конце Step: факты тика (счётчики состояния) → события и событийные скачки. Без аллокаций.</summary>
    public static class EventScan
    {
        /// <summary>Толчок в препятствие засчитывается как «уложил», если столкновение не позже чем через 1 с после толчка.</summary>
        public const int LayoutWindow = Simulation.TicksPerSecond;

        public static void Run(Simulation sim)
        {
            var st = sim.State;
            var ev = sim.Events;
            var d = sim.EconomyData;
            int tick = st.Tick;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                ref EventState e = ref st.Ev[i];
                ref ViewerState v = ref st.Runners[i].Viewing;
                ref CollisionState c = ref st.Runners[i].Collision;
                ref ActionState a = ref st.Runners[i].Action;
                ref var t = ref st.Runners[i].Toss;

                int water = c.WaterCount - e.SeenWater;
                int other = (c.Count - e.SeenCollisions) - water;
                e.SeenWater = c.WaterCount; e.SeenCollisions = c.Count;
                for (int k = 0; k < water + other; k++)
                {
                    bool isWater = k < water;
                    int kind = isWater ? (int)CollisionKind.Water : (c.LastKind == (int)CollisionKind.Water ? (int)CollisionKind.Other : c.LastKind);
                    ev.Add(RaceEventType.Collision, tick, i, -1, kind);
                    EventBonuses.Add(d, ref v, d.CollisionViewers, isWater ? d.WaterDonations : 0);
                    // толчок в препятствие: цель столкнулась вскоре после толчка соседа
                    int stamp = a.LastGotShovedTick + 1;
                    if (a.LastShovedBy >= 0 && a.LastGotShovedTick >= 0 && stamp != e.LaidStamp && tick - a.LastGotShovedTick <= LayoutWindow)
                    {
                        e.LaidStamp = stamp;
                        ev.Add(RaceEventType.LaidOut, tick, a.LastShovedBy, i);
                    }
                }

                for (int n = a.GotShoved - e.SeenGotShoved; n > 0; n--)
                {
                    ev.Add(RaceEventType.GotShoved, tick, i, a.LastShovedBy);
                    EventBonuses.Add(d, ref v, d.GotShovedViewers, 0);
                }
                e.SeenGotShoved = a.GotShoved;
                for (int n = a.GotTripped - e.SeenGotTripped; n > 0; n--)
                    ev.Add(RaceEventType.GotTripped, tick, i, a.LastTrippedBy);
                e.SeenGotTripped = a.GotTripped;
                for (int n = a.Shoved - e.SeenShoved; n > 0; n--)
                {
                    ev.Add(RaceEventType.Shoved, tick, i, a.LastShoveTarget);
                    EventBonuses.Add(d, ref v, d.ShoveViewers, 0);
                }
                e.SeenShoved = a.Shoved;
                for (int n = a.Tripped - e.SeenTripped; n > 0; n--)
                {
                    ev.Add(RaceEventType.Tripped, tick, i, a.LastTripTarget);
                    EventBonuses.Add(d, ref v, d.TripViewers, 0);
                    ev.Add(RaceEventType.LaidOut, tick, i, a.LastTripTarget); // подножка тоже «уложил соседа»
                }
                e.SeenTripped = a.Tripped;

                for (int n = t.Tossed - e.SeenTossed; n > 0; n--)
                {
                    ev.Add(RaceEventType.Tossed, tick, i, -1, t.LastCause);
                    EventBonuses.Add(d, ref v, d.TossViewers, d.TossDonations);
                }
                e.SeenTossed = t.Tossed;
                for (int n = t.Rose - e.SeenRose; n > 0; n--) ev.Add(RaceEventType.Rose, tick, i);
                e.SeenRose = t.Rose;

                if (v.ZoneChanges != e.SeenZoneChanges)
                {
                    ev.Add(RaceEventType.ZoneChanged, tick, i, -1, v.PrevZone, v.Zone);
                    e.SeenZoneChanges = v.ZoneChanges;
                }
            }
            // Пороги зрителей — последними, по итоговому значению тика (после скачков и «влез в кадр»).
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                ref EventState e = ref st.Ev[i];
                int level = (int)(st.Runners[i].Viewing.Viewers >> Fixed.FractionBits) / d.RecordStep;
                while (e.Level < level)
                {
                    e.Level++;
                    ev.Add(RaceEventType.ViewerThreshold, tick, i, -1, e.Level * d.RecordStep);
                }
            }
        }
    }
}
