using Game.Core.Math;

namespace Game.Core.Bots
{
    /// <summary>«Трус» (design/F-005.md §2.4): d &lt; порога → спринт (гистерезис, пока выносливость &gt; порога), иначе бег; трусцу не включает;
    /// при рывке на себя спринтует при любой выносливости &gt; 0; действие никогда. Все числа — из BotDef.</summary>
    public sealed class Coward : BotBrain
    {
        public Coward(BotData data, BotDef def) : base(data, def) { }

        protected override int ChoosePace(in BotView v, ref BotMemory m, bool rushOnMe)
        {
            if (rushOnMe && v.Stamina > Fixed.Zero) return 1;
            if (v.Tick < m.TempoNext) return m.Pace;
            m.TempoNext = v.Tick + Def.TempoPeriod;
            if (v.InvulnTicks > 0) m.Sprinting = 0; // после «нет сигнала» бык не страшен, бот это знает
            else if (m.Sprinting == 0 && v.BullDist < Def.SprintBelow - Def.Hysteresis) m.Sprinting = 1;
            else if (m.Sprinting != 0 && v.BullDist > Def.SprintBelow + Def.Hysteresis) m.Sprinting = 0;
            m.Pace = m.Sprinting != 0 && v.Stamina > Def.SprintUntilStamina ? 1 : 0;
            return m.Pace;
        }
    }
}
