using System;
using Game.Core.Data;
using Game.Core.Math;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Runners
{
    /// <summary>Data/actions.json: дальности, перезарядки и стан действия «Мешает соседу».</summary>
    public sealed class ActionData
    {
        public Fixed ShoveRange, ShoveCooldown, TripRange, TripCooldown, TripStun, StairsPushBack;

        public static ActionData Default() => new ActionData
        {
            ShoveRange = Fixed.FromFraction(3, 2), ShoveCooldown = Fixed.FromInt(2),
            TripRange = Fixed.FromFraction(6, 5), TripCooldown = Fixed.FromInt(4),
            TripStun = Fixed.FromFraction(3, 5), StairsPushBack = Fixed.FromFraction(3, 5),
        };

        public static ActionData Parse(string json)
        {
            var j = Json.Parse("actions.json", json, truncate: true);
            return new ActionData
            {
                ShoveRange = j.Num("shoveRange"), ShoveCooldown = j.Num("shoveCooldown"),
                TripRange = j.Num("tripRange"), TripCooldown = j.Num("tripCooldown"),
                TripStun = j.Num("tripStun"), StairsPushBack = j.Num("stairsPushBack"),
            };
        }
    }

    /// <summary>Перезарядки и факты действий места (для событий F-019). Входит в RaceState и хэш.</summary>
    public struct ActionState
    {
        public int ShoveCd, TripCd;           // тиков до следующего толчка / подножки
        public int Shoved, Tripped;           // «толкнул», «сбил подножкой» (я — действующий)
        public int GotShoved, GotTripped;     // «тебя толкнули», «упал от подножки» (я — цель)
        public int LastShoveTick, LastTripTick, LastGotShovedTick, LastGotTrippedTick; // -1 — не было
        public int LastShoveTarget, LastTripTarget, LastShovedBy, LastTrippedBy;       // место, -1 — нет
    }

    /// <summary>Контекстное действие (PlayerInput.action): подножка впереди приоритетнее толчка сбоку. Без аллокаций.</summary>
    public static class Actions
    {
        static readonly Fixed TicksFx = Fixed.FromInt(Simulation.TicksPerSecond);

        public static void InitState(ref ActionState a)
        {
            a = default;
            a.LastShoveTick = a.LastTripTick = a.LastGotShovedTick = a.LastGotTrippedTick = -1;
            a.LastShoveTarget = a.LastTripTarget = a.LastShovedBy = a.LastTrippedBy = -1;
        }

        /// <summary>Дальность толчка места: shoveRange, у бабули — её perkValue.</summary>
        public static Fixed ShoveRangeOf(ActionData ad, CharacterDef c) =>
            c.Perk == Perk.GrandmaBag && c.PerkValue > ad.ShoveRange ? c.PerkValue : ad.ShoveRange;

        public static void Step(ActionData ad, CharacterData cd, RunnerData[] seatData, StreetMap street, RaceState st,
            RaceConfig config, TickInputs inputs)
        {
            int n = st.Runners.Length;
            for (int i = 0; i < n; i++)
            {
                if (st.Runners[i].Action.ShoveCd > 0) st.Runners[i].Action.ShoveCd--;
                if (st.Runners[i].Action.TripCd > 0) st.Runners[i].Action.TripCd--;
            }
            for (int me = 0; me < n; me++)
            {
                var seat = config.GetSeat(me);
                if (!inputs.Get(me).Action) continue;
                if (st.Runners[me].StunTicks > 0) continue;
                TryAct(ad, cd, seatData, street, st, me, cd.Get(seat.CharacterId));
            }
        }

        /// <summary>«Впереди» для подножки: сосед дальше по s и больше впереди, чем сбоку. Тем же правилом пользуются боты.</summary>
        public static bool IsAhead(Fixed ds, Fixed dx) => ds > Fixed.Zero && ds >= Fixed.Abs(dx);

        static void TryAct(ActionData ad, CharacterData cd, RunnerData[] seatData, StreetMap street, RaceState st, int me, CharacterDef pusher)
        {
            ref RunnerState mr = ref st.Runners[me];
            Fixed ms = Fixed.FromRaw(mr.S), mx = Fixed.FromRaw(mr.X);
            Fixed shoveRange = ShoveRangeOf(ad, pusher);
            Fixed tripSq = ad.TripRange * ad.TripRange, shoveSq = shoveRange * shoveRange;
            int trip = -1, shove = -1;
            Fixed tripBest = Fixed.Zero, shoveBest = Fixed.Zero;
            for (int t = 0; t < st.Runners.Length; t++)
            {
                if (t == me) continue;
                Fixed ds = Fixed.FromRaw(st.Runners[t].S) - ms, dx = Fixed.FromRaw(st.Runners[t].X) - mx;
                Fixed d2 = ds * ds + dx * dx;
                // Впереди: сосед дальше по s и «больше впереди, чем сбоку»; иначе — сбоку.
                if (IsAhead(ds, dx))
                {
                    if (d2 <= tripSq && (trip < 0 || d2 < tripBest)) { trip = t; tripBest = d2; }
                }
                else if (d2 <= shoveSq && (shove < 0 || d2 < shoveBest)) { shove = t; shoveBest = d2; }
            }
            if (trip >= 0 && mr.Action.TripCd == 0) { Trip(ad, street, st, me, trip); return; }
            if (shove >= 0 && mr.Action.ShoveCd == 0) Shove(ad, cd, seatData, street, st, me, shove, pusher);
        }

        static void Shove(ActionData ad, CharacterData cd, RunnerData[] seatData, StreetMap street, RaceState st, int me, int target, CharacterDef pusher)
        {
            ref RunnerState tr = ref st.Runners[target];
            int dir = tr.X >= st.Runners[me].X ? 1 : -1;
            Characters.ApplyShove(cd, seatData[target], street, st, pusher, target, dir);
            ref CollisionState c = ref tr.Collision;
            if (c.ZoneActive != 0) c.ZoneRaw = (Fixed.FromRaw(c.ZoneRaw) * (Fixed.One - cd.ShoveSpeedLoss)).Raw;
            PushBackOnStairs(ad, street, ref tr);
            ref ActionState ma = ref st.Runners[me].Action;
            ma.ShoveCd = (ad.ShoveCooldown * TicksFx).ToInt();
            ma.Shoved++;
            ma.LastShoveTick = st.Tick; ma.LastShoveTarget = target;
            tr.Action.GotShoved++;
            tr.Action.LastGotShovedTick = st.Tick; tr.Action.LastShovedBy = me;
        }

        static void Trip(ActionData ad, StreetMap street, RaceState st, int me, int target)
        {
            ref RunnerState tr = ref st.Runners[target];
            tr.Speed = 0;
            tr.Collision.ZoneRaw = 0;
            tr.StunTicks = Collisions.StunTicks(ad.TripStun, Fixed.FromRaw(tr.Stability));
            PushBackOnStairs(ad, street, ref tr);
            ref ActionState ma = ref st.Runners[me].Action;
            ma.TripCd = (ad.TripCooldown * TicksFx).ToInt();
            ma.Tripped++;
            ma.LastTripTick = st.Tick; ma.LastTripTarget = target;
            tr.Action.GotTripped++;
            tr.Action.LastGotTrippedTick = st.Tick; tr.Action.LastTrippedBy = me;
        }

        static void PushBackOnStairs(ActionData ad, StreetMap street, ref RunnerState r)
        {
            Fixed s = Fixed.FromRaw(r.S);
            if (street.Blocks[street.BlockIndexAt(s)].Def.Id != "stairs") return;
            s = s - ad.StairsPushBack;
            if (s < Fixed.Zero) s = Fixed.Zero;
            r.S = s.Raw;
        }
    }
}
