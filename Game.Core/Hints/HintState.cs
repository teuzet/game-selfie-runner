using Game.Core.Math;

namespace Game.Core.Hints
{
    /// <summary>Состояние подсказок места (RunnerState.Hints): какое препятствие обработано, ГСЧ шума и подсказки текущего тика для бота.</summary>
    public struct HintState
    {
        public int Next;        // индекс первого препятствия, по которому решение ещё не принято (одна подсказка на препятствие)
        public DetRandom Rng;   // собственный поток шума
        public int FreshTick;   // тик, в котором выданы Fresh* (-1 — ещё не было)
        public int FreshCount;  // 0..2
        public int Type0, Side0, Ticks0, Type1, Side1, Ticks1;

        public ulong Hash(ulong h)
        {
            h = Hashing.Mix(h, (ulong)(uint)Next); h = Hashing.Mix(h, Rng.State); h = Hashing.Mix(h, Rng.Inc);
            h = Hashing.Mix(h, (ulong)(uint)FreshTick); h = Hashing.Mix(h, (ulong)(uint)FreshCount);
            h = Hashing.Mix(h, (ulong)(uint)Type0); h = Hashing.Mix(h, (ulong)(uint)Side0); h = Hashing.Mix(h, (ulong)(uint)Ticks0);
            h = Hashing.Mix(h, (ulong)(uint)Type1); h = Hashing.Mix(h, (ulong)(uint)Side1);
            return Hashing.Mix(h, (ulong)(uint)Ticks1);
        }
    }
}
