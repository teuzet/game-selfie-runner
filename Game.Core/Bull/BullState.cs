namespace Game.Core
{
    public enum BullPhase { Pen = 0, Charge = 1, Cruise = 2, Slowed = 3, Arena = 4 }

    /// <summary>Плоское состояние быка. Fixed хранится как raw (long).</summary>
    public struct BullState
    {
        public long S;        // метры вдоль улицы (центр капсулы)
        public long X;        // сдвиг поперёк оси
        public long Speed;    // м/с вдоль улицы
        public int Phase;     // BullPhase
        public int Timer;     // Pen: тиков до срыва из загона
        public int SlowTicks; // оставшиеся тики замедления (ставят другие фичи через BullMovement.Slow)
    }
}
