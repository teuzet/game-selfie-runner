using Game.Core.Math;

namespace Game.Core.Bots
{
    /// <summary>«Трус» (design/F-005.md §2.4): d &lt; порога → спринт (гистерезис, пока выносливость &gt; порога), иначе бег; дальше jogAbove — трусца с гистерезисом jogHysteresis (F-102 §3.3);
    /// при рывке на себя спринтует при любой выносливости &gt; 0; действие никогда. Все числа — из BotDef.</summary>
    public sealed class Coward : BotBrain
    {
        public Coward(BotData data, BotDef def) : base(data, def) { }

        protected override int ChoosePace(in BotView v, ref BotMemory m, bool rushOnMe)
        {
            if (rushOnMe && v.Stamina > Fixed.Zero) return 1;
            if (v.Tick < m.TempoNext) return m.Pace;
            m.TempoNext = v.Tick + Def.TempoPeriod;
            if (v.InvulnTicks > 0) m.Sprinting = 1; // подброшен (§2.5): неуязвимость — спринт, пока выносливость > порога
            else if (m.Sprinting == 0 && v.BullDist < Def.SprintBelow - Def.Hysteresis) m.Sprinting = 1;
            else if (m.Sprinting != 0 && v.BullDist > Def.SprintBelow + Def.Hysteresis) m.Sprinting = 0;
            if (m.Sprinting != 0 && v.Stamina > Def.SprintUntilStamina) m.Pace = 1;
            else // F-102 §3.3 п.1: дальше jogAbove — трусца (включается при d > jogAbove + jogHysteresis, выключается при d < jogAbove − jogHysteresis), иначе бег
                m.Pace = (m.Pace < 0 ? v.BullDist > Def.JogAbove - Def.JogHysteresis : v.BullDist > Def.JogAbove + Def.JogHysteresis) ? -1 : 0;
            return m.Pace;
        }
    }
}
