namespace Game.Core
{
    /// <summary>
    /// Всё состояние места бегуна в одной структуре (F-063): движение, факты столкновений (F-013), устойчивость (F-014),
    /// толчки/подножки (F-015), подброс быком (F-017), зрители и донаты (F-018). Fixed хранится как raw (long).
    /// </summary>
    public struct RunnerState
    {
        public long S;       // метры вдоль улицы
        public long X;       // сдвиг поперёк оси (влево отрицательно)
        public long Speed;   // м/с вдоль улицы
        public long Stamina; // 0..staminaMax
        public int Exhausted;  // 1 — выносливость падала до 0, спринт заблокирован до порога восстановления
        public int StunTicks;  // оставшиеся тики без управления (скорость держится 0); ставят другие фичи
        /// <summary>Факты и эффекты столкновений (F-013); события F-019 читают Count/LastKind/LastS/LastX/LastTick.</summary>
        public Game.Core.Runners.CollisionState Collision;
        /// <summary>Устойчивость (Fixed raw), из персонажа места (CharacterId), F-014; её делят стан и сдвиг от толчка.</summary>
        public long Stability;
        /// <summary>Перезарядки и факты толчка/подножки (F-015); события F-019 читают Shoved/Tripped/GotShoved/GotTripped и Last*.</summary>
        public Game.Core.Runners.ActionState Action;
        /// <summary>Подброс (F-017): «нет сигнала», неуязвимость, факты «подброшен»/«встал» для F-019.</summary>
        public Game.Core.Bull.TossState Toss;
        /// <summary>Зрители и донаты (F-018); зона дистанции до быка — факт для F-019.</summary>
        public Game.Core.Economy.ViewerState Viewing;
    }
}
