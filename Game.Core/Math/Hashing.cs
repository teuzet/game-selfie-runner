namespace Game.Core.Math
{
    /// <summary>FNV-1a (64 бита): общий строительный блок хэшей состояния. Каждая структура модуля кладёт свои поля через Mix в своём Hash(h).</summary>
    public static class Hashing
    {
        public const ulong Seed = 14695981039346656037UL;

        public static ulong Mix(ulong h, ulong v)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (v >> (i * 8)) & 0xFF;
                    h *= 1099511628211UL;
                }
            }
            return h;
        }
    }
}
