using Game.Core.Math;

namespace Game.Core.Economy
{
    /// <summary>Событийные скачки зрителей и донатов. Числа — в economy.json (EconomyData, F-074;
    /// источник — design/overview.md, «Зрители и донаты», design/F-101.md §3.3).</summary>
    public static class EventBonuses
    {
        /// <summary>Скачок: зрители с зажимом [пол, потолок], донаты без ограничений.</summary>
        public static void Add(EconomyData d, ref ViewerState v, int viewers, int donations)
        {
            if (v.FinishTick >= 0) return; // финишировал: зрители и донаты заморожены
            long nv = v.Viewers + Fixed.FromInt(viewers).Raw;
            if (nv > d.Cap.Raw) nv = d.Cap.Raw;
            if (nv < d.Floor.Raw) nv = d.Floor.Raw;
            v.Viewers = nv;
            v.Donations += Fixed.FromInt(donations).Raw;
        }
    }
}
