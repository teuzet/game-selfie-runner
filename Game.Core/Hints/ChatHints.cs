using System;
using Game.Core.Bots;
using Game.Core.Events;
using Game.Core.Math;
using Game.Core.Runners;
using Game.Core.Street;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Hints
{
    /// <summary>
    /// Подсказки чата (F-021, design/overview.md → «Подсказки чата»): бегуну приходит подсказка о препятствии на его трети улицы,
    /// если впереди него в 10–45 м бежит тот, у кого препятствие уже «сзади» (в кадре), за LeadSec до препятствия; с шумом.
    /// Событие Hint: Param1 = тип препятствия (CollisionKind) | тиков до &lt;&lt; 8, Param2 = сторона (−1/0/+1) со знаком шума.
    /// </summary>
    public static class ChatHints
    {
        public static void InitState(ulong seed, int seat, ref HintState h)
        {
            h = default;
            h.FreshTick = -1;
            h.Rng = new DetRandom(seed ^ 0xC4A7C4A7C4A7UL, (ulong)(seat + 1));
        }

        public static int TypeOf(int param1) => param1 & 0xFF;
        public static int TicksOf(int param1) => param1 >> 8;

        /// <summary>Шум одной подсказки: true — подсказка не пришла; side меняется на противоположную при вранье (центр — случайно).</summary>
        public static bool Noise(HintData hd, ref DetRandom rng, bool noiseless, bool full, ref int side)
        {
            if (noiseless) return false;
            bool wrong = rng.Chance(hd.WrongSideChance), drop = rng.Chance(hd.DropChance); // независимые доли от всех подсказок
            if (drop) return true;
            if (wrong && !full)
                side = side != 0 ? -side : ((rng.NextUInt() & 1) == 0 ? -1 : 1);
            return false;
        }

        /// <summary>Шаг модуля: решения по препятствиям, вошедшим в горизонт опережения, события Hint и подсказки тика для ботов.</summary>
        public static void Step(Simulation sim)
        {
            var st = sim.State;
            var obs = sim.Street.Obstacles;
            HintData hd = sim.HintData;
            for (int seat = 0; seat < RaceConfig.SeatCount; seat++)
            {
                ref RunnerState r = ref st.Runners[seat];
                ref HintState h = ref r.Hints;
                while (h.Next < obs.Length && obs[h.Next].S.Raw <= r.S) h.Next++; // позади (в т.ч. после подброса вперёд)
                if (r.Speed <= 0) continue;
                bool nerd = sim.CharacterOf(seat).Perk == Perk.NerdModerator;
                while (h.Next < obs.Length)
                {
                    int i = h.Next;
                    long ticks = TicksUntil(obs[i].S.Raw - r.S, r.Speed);
                    if (ticks > hd.LeadTicks) break;
                    h.Next++;
                    Consider(sim, hd, seat, i, (int)ticks, nerd);
                }
            }
        }

        // тиков до препятствия при текущей скорости, округление вверх: при постоянной скорости ровно LeadTicks на первом тике горизонта
        static long TicksUntil(long distRaw, long speedRaw) =>
            (distRaw * Simulation.TicksPerSecond + speedRaw - 1) / speedRaw;

        static void Consider(Simulation sim, HintData hd, int seat, int i, int ticks, bool nerd)
        {
            var st = sim.State;
            StreetMap street = sim.Street;
            ref readonly StreetObstacle o = ref street.Obstacles[i];
            ObstacleDef od = o.Def;
            if (od.Effect != "stun" && od.Effect != "slow") return;
            Fixed s = Fixed.FromRaw(st.Runners[seat].S);
            // источник: бегун впереди в [NearMin, NearMax]; препятствие должно быть позади него (в его кадре)
            Fixed leaderS = Fixed.Zero; bool found = false;
            for (int k = 0; k < RaceConfig.SeatCount; k++)
            {
                if (k == seat) continue;
                Fixed ls = Fixed.FromRaw(st.Runners[k].S);
                Fixed d = ls - s;
                if (d < hd.NearMin || d > hd.NearMax) continue;
                if (!found || ls > leaderS) { leaderS = ls; found = true; }
            }
            if (!found || o.S >= leaderS) return;
            // твоя треть улицы
            Fixed w = street.WidthAt(o.S);
            Fixed third = w / Fixed.FromInt(hd.Thirds);
            Fixed half = w / Fixed.FromInt(2);
            int mine = ThirdIndex(Fixed.FromRaw(st.Runners[seat].X), half, third, hd.Thirds);
            Fixed ox = Fixed.FromRaw(st.Dyn[i].X);
            int side;
            if (od.FullWidth) side = 0;
            else
            {
                Fixed lo = -half + third * Fixed.FromInt(mine), hi = lo + third;
                Fixed a = o.SizeAcross / Fixed.FromInt(2);
                if (ox + a <= lo || ox - a >= hi) return;
                side = ThirdIndex(ox, half, third, hd.Thirds) - hd.Thirds / 2;
            }
            ref HintState h = ref st.Runners[seat].Hints;
            if (Noise(hd, ref h.Rng, nerd, od.FullWidth, ref side)) return;
            int type = (int)Collisions.KindOf(od.Id);
            sim.Events.Add(RaceEventType.Hint, st.Tick, seat, -1, type | (ticks << 8), side);
            if (h.FreshTick != st.Tick) { h.FreshTick = st.Tick; h.FreshCount = 0; }
            if (h.FreshCount == 0) { h.Type0 = type; h.Side0 = side; h.Ticks0 = ticks; h.FreshCount = 1; }
            else if (h.FreshCount == 1) { h.Type1 = type; h.Side1 = side; h.Ticks1 = ticks; h.FreshCount = 2; }
        }

        static int ThirdIndex(Fixed x, Fixed half, Fixed third, int thirds)
        {
            Fixed p = x + half;
            int t = p < Fixed.Zero ? 0 : (p / third).ToInt();
            return t >= thirds ? thirds - 1 : t;
        }
    }

    /// <summary>Подсказки бота = подсказки его чата тика (IBotHintSource): читает то, что ChatHints.Step записал в состояние места.</summary>
    public sealed class ChatHintSource : IBotHintSource
    {
        public int Collect(RaceState st, StreetMap street, int seat, Span<BotHint> dst)
        {
            ref readonly HintState h = ref st.Runners[seat].Hints;
            if (h.FreshTick != st.Tick) return 0;
            int n = 0;
            if (h.FreshCount > 0 && n < dst.Length) dst[n++] = Make(h.Type0, h.Side0, h.Ticks0);
            if (h.FreshCount > 1 && n < dst.Length) dst[n++] = Make(h.Type1, h.Side1, h.Ticks1);
            return n;
        }

        static BotHint Make(int type, int side, int ticks) => new BotHint
        {
            Side = side, TicksUntil = ticks,
            Full = type == (int)CollisionKind.Laundry, Stall = type == (int)CollisionKind.Stall,
        };
    }
}
