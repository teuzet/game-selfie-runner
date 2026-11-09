using Game.Core.Math;

namespace Game.Core.Bots
{
    /// <summary>«Тролль» (design/F-005.md §2.4): коридор 12–18 м, охота на соседа (поиск раз в период действия, до 3 с, толчок в препятствие,
    /// «бьёт туда» подножкой, толчок в конце), квесты «Уложи соседа» и «Обгони X». Числа — из BotDef/BotData.</summary>
    public sealed class Troll : BotBrain
    {
        public Troll(BotData data, BotDef def) : base(data, def) { }

        protected override int HuntVictim(in BotMemory m) => m.HuntVictim;

        protected override bool PreHint(in BotView v, ref BotMemory m, out Fixed tx)
        {
            HuntStep(v, ref m, Def.HuntRadius, Def.ActionPeriod);
            tx = Fixed.FromRaw(m.HuntTx);
            return m.HuntKind == 1;
        }

        protected override bool PostHint(in BotView v, ref BotMemory m, out Fixed tx)
        {
            tx = Fixed.FromRaw(m.HuntTx);
            return m.HuntKind == 2;
        }

        protected override bool ChooseAction(in BotView v, ref BotMemory m) => HuntPress(v, ref m);

        protected override int ChoosePace(in BotView v, ref BotMemory m, bool rushOnMe)
        {
            if (rushOnMe && v.Stamina > Def.SprintUntilStamina) return 1;
            if (v.Arena) return 0; // арена: бег до финиша
            if (v.Tick < m.TempoNext) return m.Pace;
            m.TempoNext = v.Tick + Def.TempoPeriod;
            m.Sprinting = v.InvulnTicks == 0 && Below(m.Sprinting != 0, v.BullDist, Def.SprintBelow, Def.Hysteresis) ? 1 : 0;
            if (m.Sprinting != 0 && v.Stamina > Def.SprintUntilStamina) m.Pace = 1;
            else if (Quest(v, "overtake") && QuestTargetAhead(v, ref m)) m.Pace = v.Stamina >= Def.OvertakeSprint ? 1 : 0;
            else if (HuntPace(v, ref m, out int hunt)) m.Pace = hunt;
            else m.Pace = (m.Pace < 0 ? v.BullDist > Def.BandMax - Def.Hysteresis : v.BullDist > Def.BandMax + Def.Hysteresis) ? -1 : 0;
            return m.Pace;
        }
    }
}
