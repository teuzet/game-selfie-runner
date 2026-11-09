namespace Game.Core
{
    public enum BullPhase { Pen = 0, Charge = 1, Cruise = 2, Slowed = 3, Arena = 4 }

    /// <summary>Режим резинки (гистерезис): 0 — крейсер, 1 — рывок до цели, 2 — сближение до цели.</summary>
    public static class BullMode { public const int None = 0, Charge = 1, Close = 2; }

    /// <summary>Плоское состояние быка. Fixed хранится как raw (long).</summary>
    public struct BullState
    {
        public long S;        // метры вдоль улицы (центр капсулы)
        public long X;        // сдвиг поперёк оси
        public long Speed;    // м/с вдоль улицы
        public int Phase;     // BullPhase
        public int Timer;     // Pen: тиков до срыва из загона
        public int Mode;      // BullMode
        public int SlowTicks; // оставшиеся тики замедления (ставят другие фичи через BullMovement.Slow)

        public ulong Hash(ulong h)
        {
            h = Math.Hashing.Mix(h, (ulong)S); h = Math.Hashing.Mix(h, (ulong)X); h = Math.Hashing.Mix(h, (ulong)Speed);
            h = Math.Hashing.Mix(h, (ulong)Phase); h = Math.Hashing.Mix(h, (ulong)Timer);
            h = Math.Hashing.Mix(h, (ulong)Mode);
            return Math.Hashing.Mix(h, (ulong)SlowTicks);
        }
    }
}
