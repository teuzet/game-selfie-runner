using Game.Core.Math;

namespace Game.Core.Economy
{
    /// <summary>Событийные скачки зрителей и донатов. Источник — design/overview.md, «Зрители и донаты»
    /// (отдельного JSON нет: GameData вне owned_files F-019, числа — константы).</summary>
    public static class EventBonuses
    {
        public const int CollisionViewers = 40;   // столкнулся с препятствием +40
        public const int TossViewers = 120;       // подброшен +120 зрителей
        public const int TossDonations = 50;      // и +50 донатов («держи на лечение»)
        public const int ShoveViewers = 50;       // толкнул соседа +50
        public const int TripViewers = 80;        // сосед упал от подножки +80
        public const int GotShovedViewers = -30;  // тебя толкнули −30
        public const int WaterDonations = 80;     // падение в пруд +80 донатов (плюс +40 как столкновение)
        public const int ThresholdStep = 250;     // порог зрителей каждые 250

        /// <summary>Скачок: зрители с зажимом [пол, потолок], донаты без ограничений.</summary>
        public static void Add(EconomyData d, ref ViewerState v, int viewers, int donations)
        {
            long nv = v.Viewers + Fixed.FromInt(viewers).Raw;
            if (nv > d.Cap.Raw) nv = d.Cap.Raw;
            if (nv < d.Floor.Raw) nv = d.Floor.Raw;
            v.Viewers = nv;
            v.Donations += Fixed.FromInt(donations).Raw;
        }
    }
}
