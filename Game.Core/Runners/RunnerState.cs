namespace Game.Core
{
    /// <summary>Плоское состояние бегуна. Fixed хранится как raw (long).</summary>
    public struct RunnerState
    {
        public long S;       // метры вдоль улицы
        public long X;       // сдвиг поперёк оси (влево отрицательно)
        public long Speed;   // м/с вдоль улицы
        public long Stamina; // 0..staminaMax
        public int Exhausted;  // 1 — выносливость падала до 0, спринт заблокирован до порога восстановления
        public int StunTicks;  // оставшиеся тики без управления (скорость держится 0); ставят другие фичи
    }
}
