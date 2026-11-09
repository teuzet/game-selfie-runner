using Game.Core;

namespace Game.Net
{
    /// <summary>
    /// Кольцевой буфер входов одного места: известные (настоящие) входы по тикам
    /// и предсказание «повтор последнего известного».
    /// </summary>
    public sealed class InputBuffer
    {
        public const int Capacity = 64;

        private readonly int[] _tag = new int[Capacity]; // tick+1; 0 — пусто
        private readonly byte[] _val = new byte[Capacity];

        /// <summary>Вход последнего подтверждённого (пройденного) тика — запасной вариант предсказания.</summary>
        public PlayerInput LastConfirmed;

        public bool Has(int tick) => tick >= 0 && _tag[tick & (Capacity - 1)] == tick + 1;

        public PlayerInput Get(int tick) => PlayerInput.Unpack(_val[tick & (Capacity - 1)]);

        /// <summary>Записывает настоящий вход, если его ещё нет. Возвращает true, если запись новая.</summary>
        public bool Set(int tick, PlayerInput input)
        {
            if (tick < 0 || Has(tick)) return false;
            _tag[tick & (Capacity - 1)] = tick + 1;
            _val[tick & (Capacity - 1)] = input.Pack();
            return true;
        }

        /// <summary>Вход на тик: настоящий, если известен, иначе повтор ближайшего известного слева (до floorTick).</summary>
        public PlayerInput Predict(int tick, int floorTick)
        {
            if (Has(tick)) return Get(tick);
            for (int t = tick - 1; t >= floorTick && t >= 0; t--)
                if (Has(t)) return Get(t);
            return LastConfirmed;
        }
    }
}
