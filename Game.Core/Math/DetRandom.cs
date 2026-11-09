using System;

namespace Game.Core.Math
{
    /// <summary>PCG32 (XSH-RR). State is two ulongs; copy by assignment duplicates the stream.</summary>
    public struct DetRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong DefaultStream = 1442695040888963407UL;

        public ulong State;
        public ulong Inc;

        public DetRandom(ulong seed) : this(seed, DefaultStream) { }

        public DetRandom(ulong seed, ulong stream)
        {
            State = 0;
            Inc = (stream << 1) | 1UL;
            NextUInt();
            State = unchecked(State + seed);
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = State;
            State = unchecked(old * Multiplier + Inc);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        /// <summary>Uniform int in [min, maxExclusive), unbiased (rejection sampling).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) throw new ArgumentException("maxExclusive must be > min");
            ulong span = (ulong)((long)maxExclusive - min);
            if (span > uint.MaxValue)
            {
                // span up to 2^32: combine two draws
                ulong limit = ulong.MaxValue - (ulong.MaxValue % span);
                ulong r;
                do { r = ((ulong)NextUInt() << 32) | NextUInt(); } while (r >= limit);
                return (int)(min + (long)(r % span));
            }
            uint bound = (uint)span;
            uint threshold = unchecked((uint)(-(int)bound)) % bound;
            uint x;
            do { x = NextUInt(); } while (x < threshold);
            return (int)(min + (long)(x % bound));
        }

        /// <summary>Uniform Fixed in [0, 1).</summary>
        public Fixed NextFixed01() => Fixed.FromRaw(NextUInt() >> 16);

        /// <summary>True with probability p (p &lt;= 0 never, p &gt;= 1 always).</summary>
        public bool Chance(Fixed p)
        {
            if (p.Raw <= 0) return false;
            if (p.Raw >= Fixed.OneRaw) return true;
            return NextFixed01().Raw < p.Raw;
        }
    }
}
