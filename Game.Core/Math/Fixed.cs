using System;
using System.Globalization;

namespace Game.Core.Math
{
    /// <summary>
    /// Q16.16 fixed-point number stored in a long. Deterministic; no implicit conversions from float.
    /// Add/Sub/Mul/Div use unchecked wrap-around on overflow of the 64-bit raw value
    /// (Div requires |a| &lt; 2^47 raw, Mul requires |a*b| &lt; 2^63 raw before the shift).
    /// </summary>
    public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
    {
        public const int FractionBits = 16;
        public const long OneRaw = 1L << FractionBits;

        public readonly long Raw;

        private Fixed(long raw) { Raw = raw; }

        public static readonly Fixed Zero = new Fixed(0);
        public static readonly Fixed One = new Fixed(OneRaw);
        public static readonly Fixed Half = new Fixed(OneRaw / 2);
        public static readonly Fixed MaxValue = new Fixed(long.MaxValue);
        public static readonly Fixed MinValue = new Fixed(long.MinValue);

        public static Fixed FromRaw(long raw) => new Fixed(raw);
        public static Fixed FromInt(int v) => new Fixed((long)v << FractionBits);

        /// <summary>numerator/denominator, truncated toward zero.</summary>
        public static Fixed FromFraction(int numerator, int denominator)
        {
            if (denominator == 0) throw new DivideByZeroException();
            return new Fixed(((long)numerator << FractionBits) / denominator);
        }

        public int ToInt() => (int)(Raw >> FractionBits); // floor
        public int ToIntTruncated() => (int)(Raw / OneRaw);

        public static explicit operator Fixed(int v) => FromInt(v);
        public static explicit operator int(Fixed f) => f.ToIntTruncated();
        /// <summary>Explicit only; for tooling/tests, not for simulation logic.</summary>
        public static explicit operator double(Fixed f) => f.Raw / (double)OneRaw;

        public static Fixed operator +(Fixed a, Fixed b) => new Fixed(unchecked(a.Raw + b.Raw));
        public static Fixed operator -(Fixed a, Fixed b) => new Fixed(unchecked(a.Raw - b.Raw));
        public static Fixed operator -(Fixed a) => new Fixed(unchecked(-a.Raw));
        public static Fixed operator *(Fixed a, Fixed b) => new Fixed(unchecked(a.Raw * b.Raw) >> FractionBits);

        public static Fixed operator /(Fixed a, Fixed b)
        {
            if (b.Raw == 0) throw new DivideByZeroException();
            return new Fixed(unchecked(a.Raw << FractionBits) / b.Raw);
        }

        public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
        public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
        public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
        public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
        public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
        public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

        /// <summary>Abs; Abs(MinValue) wraps to MinValue.</summary>
        public static Fixed Abs(Fixed a) => a.Raw < 0 ? new Fixed(unchecked(-a.Raw)) : a;
        public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;
        public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;

        public static Fixed Clamp(Fixed v, Fixed min, Fixed max)
        {
            if (min.Raw > max.Raw) throw new ArgumentException("min > max");
            return v.Raw < min.Raw ? min : (v.Raw > max.Raw ? max : v);
        }

        /// <summary>Square root, truncated. Throws for negative input.</summary>
        public static Fixed Sqrt(Fixed a)
        {
            if (a.Raw < 0) throw new ArgumentOutOfRangeException(nameof(a), "Sqrt of negative");
            if (a.Raw == 0) return Zero;
            ulong raw = (ulong)a.Raw;
            // want isqrt(raw << 16); for raw >= 2^48 the shift overflows, so use isqrt(raw) << 8.
            if (raw >= (1UL << 48)) return new Fixed((long)ISqrt(raw) << 8);
            return new Fixed((long)ISqrt(raw << FractionBits));
        }

        private static ulong ISqrt(ulong n)
        {
            ulong res = 0;
            ulong bit = 1UL << 62;
            while (bit > n) bit >>= 2;
            while (bit != 0)
            {
                if (n >= res + bit)
                {
                    n -= res + bit;
                    res = (res >> 1) + bit;
                }
                else res >>= 1;
                bit >>= 2;
            }
            return res;
        }

        public bool Equals(Fixed other) => Raw == other.Raw;
        public override bool Equals(object? obj) => obj is Fixed f && f.Raw == Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

        /// <summary>Invariant decimal text with 5 fractional digits, truncated toward zero (e.g. "-1.50000").</summary>
        public override string ToString()
        {
            bool neg = Raw < 0;
            ulong mag = neg ? unchecked((ulong)(-Raw)) : (ulong)Raw; // MinValue: -Raw wraps to itself, cast gives 2^63
            ulong ip = mag >> FractionBits;
            ulong frac = ((mag & (OneRaw - 1)) * 100000UL) >> FractionBits;
            return (neg ? "-" : "") + ip.ToString(CultureInfo.InvariantCulture) + "." +
                   frac.ToString("D5", CultureInfo.InvariantCulture);
        }
    }
}
