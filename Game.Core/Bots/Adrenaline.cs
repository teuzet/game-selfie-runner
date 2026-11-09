using Game.Core.Math;

namespace Game.Core.Bots
{
    /// <summary>«Адреналинщик» (design/F-005.md §2.4): держит d в коридоре 4–8 м (трусца / бег / внутри — прежний темп), спринт в последний момент
    /// или от рывка, запас выносливости; толкает при помехе; квесты — все, кроме «Чистого пробега». Числа — из BotDef/BotData.</summary>
    public sealed class Adrenaline : BotBrain
    {
        public Adrenaline(BotData data, BotDef def) : base(data, def) { }

        protected override int HuntVictim(in BotMemory m) => m.HuntVictim;

        protected override bool PreHint(in BotView v, ref BotMemory m, out Fixed tx)
        {
            tx = Fixed.Zero;
            if (Quest(v, "floor_neighbor"))
            {
                HuntStep(v, ref m, Data.QuestHuntRadius, Data.QuestHuntPeriod);
                tx = Fixed.FromRaw(m.HuntTx);
                return m.HuntKind == 1;
            }
            m.HuntVictim = -1; m.HuntKind = 0;
            // «Врежься в лоток»: подсказку о лотке выполняет наоборот — цель x в сторону препятствия
            ref BotPlan near = ref NearPlan(ref m);
            if (Quest(v, "hit_stall") && near.Used != 0 && near.Stall != 0 && near.Full == 0 && near.Side != 0 && v.Tick >= near.ReactAt)
            {
                tx = Fixed.FromInt(near.Side) * (v.Width / Data.DodgeDiv);
                return true;
            }
            return false;
        }

        protected override bool PostHint(in BotView v, ref BotMemory m, out Fixed tx)
        {
            tx = Fixed.Zero;
            if (m.HuntKind == 2) { tx = Fixed.FromRaw(m.HuntTx); return true; }
            // без подсказки в рынке держится у стены
            if (Quest(v, "hit_stall") && v.Block == BotBlock.Market)
            {
                tx = Fixed.FromInt(m.LaneSide) * (v.Width / Fixed.FromInt(2) - Data.WallMargin);
                return true;
            }
            return false;
        }

        protected override int ChoosePace(in BotView v, ref BotMemory m, bool rushOnMe)
        {
            if (rushOnMe && v.Stamina > Def.SprintUntilStamina) return 1;
            if (v.Arena) return 0;
            if (v.Tick < m.TempoNext) return m.Pace;
            m.TempoNext = v.Tick + Def.TempoPeriod;
            Fixed lo = Def.BandMin, hi = Def.BandMax;
            bool chase = Quest(v, "overtake") && QuestTargetAhead(v, ref m);
            if (Quest(v, "stay_hot")) { lo = Data.StayHotMin; hi = Data.StayHotMax; }
            if (chase) hi = Data.OvertakeBand;
            m.Sprinting = v.InvulnTicks == 0 && Below(m.Sprinting != 0, v.BullDist, Def.SprintBelow, Def.Hysteresis) ? 1 : 0;
            int pace = m.Pace > 0 ? 0 : m.Pace; // спринт — не состояние коридора
            if (m.Sprinting != 0 && v.Stamina > Def.SprintUntilStamina) pace = 1;
            else if (chase && v.Stamina >= Def.OvertakeSprint && v.Stamina > Def.ReserveStamina) pace = 1; // без угрозы — только из запаса
            else if (chase) pace = 0;
            else if (pace == 0 && v.BullDist > hi + Def.Hysteresis) pace = -1;
            else if (pace < 0 && v.BullDist < lo - Def.Hysteresis) pace = 0;
            m.Pace = pace;
            return pace;
        }

        protected override bool ChooseAction(in BotView v, ref BotMemory m)
        {
            if (v.Arena) return false;
            if (Quest(v, "floor_neighbor")) return HuntPress(v, ref m); // агрессия 1.0, охота как у тролля
            if (v.Tick < m.ActionNext) return false;
            m.ActionNext = v.Tick + Def.ActionPeriod;
            if (v.ShoveCd > 0) return false;
            Fixed tx = Fixed.FromRaw(m.LastTx);
            int path = Fixed.Abs(tx - v.X) > Data.DeadZone ? Sign(tx - v.X) : 0;
            for (int o = 0; o < RaceConfig.SeatCount; o++)
            {
                if (o == v.Seat) continue;
                BotNeighbor n = v.Neighbor(o);
                if (!n.Valid || Fixed.Abs(n.DS) > Data.SideFrame || n.DS * n.DS + n.DX * n.DX > Def.HuntRadius * Def.HuntRadius) continue;
                bool onPath = path != 0 && Sign(n.DX) == path; // стоит на пути его уворота
                bool inFrame = n.DS <= Fixed.Zero;            // влез в кадр: сзади не дальше 1.5 м
                if (onPath || inFrame) return m.Rng.Chance(Def.Aggression);
            }
            return false;
        }
    }
}
