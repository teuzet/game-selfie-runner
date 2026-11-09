namespace SelfieRunner.Sim
{
    /// <summary>Копит долг в тиках (dt × 30), отдаёт число шагов за кадр (не больше MaxStepsPerFrame); остаток доигрывается позже.</summary>
    public sealed class TickAccumulator
    {
        public const int TicksPerSecond = 30;
        public const int MaxStepsPerFrame = 8;

        private double _debt;

        public double Debt => _debt;

        public int Advance(double dt)
        {
            if (dt > 0) _debt += dt * TicksPerSecond;
            int steps = (int)(_debt + 1e-9);
            if (steps > MaxStepsPerFrame) steps = MaxStepsPerFrame;
            _debt -= steps;
            if (_debt < 0) _debt = 0;
            return steps;
        }
    }
}
