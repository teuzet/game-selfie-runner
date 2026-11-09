using System;
using Game.Core.Math;
using Xunit;

namespace Game.Core.Tests
{
    public class MathTests
    {
        [Fact]
        public void Constants()
        {
            Assert.Equal(0, Fixed.Zero.Raw);
            Assert.Equal(65536, Fixed.One.Raw);
            Assert.Equal(32768, Fixed.Half.Raw);
        }

        [Fact]
        public void Arithmetic()
        {
            var a = Fixed.FromInt(3);
            var b = Fixed.FromFraction(1, 2);
            Assert.Equal(Fixed.FromFraction(7, 2), a + b);
            Assert.Equal(Fixed.FromFraction(5, 2), a - b);
            Assert.Equal(Fixed.FromFraction(3, 2), a * b);
            Assert.Equal(Fixed.FromInt(6), a / b);
            Assert.Equal(Fixed.FromInt(-3), -a);
            Assert.Equal(Fixed.FromInt(-2), Fixed.FromInt(-6) / a);
            Assert.Equal(Fixed.FromInt(-6), Fixed.FromInt(-3) * Fixed.FromInt(2));
        }

        [Fact]
        public void FromFractionTruncates()
        {
            Assert.Equal(21845, Fixed.FromFraction(1, 3).Raw);
            Assert.Throws<DivideByZeroException>(() => Fixed.FromFraction(1, 0));
            Assert.Throws<DivideByZeroException>(() => Fixed.One / Fixed.Zero);
        }

        [Fact]
        public void Boundaries()
        {
            Assert.Equal(int.MaxValue, Fixed.FromInt(int.MaxValue).ToInt());
            Assert.Equal(int.MinValue, Fixed.FromInt(int.MinValue).ToInt());
            Assert.Equal(Fixed.MinValue, Fixed.Abs(Fixed.MinValue)); // wraps
            Assert.Equal(Fixed.MaxValue, Fixed.Abs(Fixed.MinValue + Fixed.FromRaw(1)));
            Assert.Equal(Fixed.One, Fixed.Min(Fixed.One, Fixed.FromInt(2)));
            Assert.Equal(Fixed.FromInt(2), Fixed.Max(Fixed.One, Fixed.FromInt(2)));
            Assert.Equal(Fixed.One, Fixed.Clamp(Fixed.FromInt(5), Fixed.Zero, Fixed.One));
            Assert.Equal(Fixed.Zero, Fixed.Clamp(Fixed.FromInt(-5), Fixed.Zero, Fixed.One));
            Assert.Equal(Fixed.Half, Fixed.Clamp(Fixed.Half, Fixed.Zero, Fixed.One));
        }

        [Fact]
        public void OverflowWrapsDeterministically()
        {
            Assert.Equal(Fixed.MinValue, Fixed.MaxValue + Fixed.FromRaw(1));
            Assert.Equal(Fixed.MaxValue, Fixed.MinValue - Fixed.FromRaw(1));
            var big = Fixed.FromInt(100000);
            var p = big * big; // 1e10 > 2^31 range but within long raw: 1e10*65536 fits? 6.5e14 raw*65536 wraps
            Assert.Equal(unchecked(big.Raw * big.Raw) >> 16, p.Raw);
        }

        [Fact]
        public void Comparisons()
        {
            Assert.True(Fixed.Zero < Fixed.One);
            Assert.True(Fixed.One > Fixed.Half);
            Assert.True(Fixed.One <= Fixed.One);
            Assert.True(Fixed.One >= Fixed.One);
            Assert.True(Fixed.One != Fixed.Half);
            Assert.True(Fixed.One == Fixed.FromInt(1));
        }

        [Fact]
        public void SqrtOfIntegerSquares()
        {
            for (int i = 0; i <= 3000; i++)
                Assert.Equal(Fixed.FromInt(i), Fixed.Sqrt(Fixed.FromInt(i * i)));
            Assert.Equal(Fixed.Zero, Fixed.Sqrt(Fixed.Zero));
            Assert.Equal(Fixed.FromRaw(92681), Fixed.Sqrt(Fixed.FromInt(2))); // 1.41421 truncated
            Assert.Throws<ArgumentOutOfRangeException>(() => Fixed.Sqrt(Fixed.FromInt(-1)));
        }

        [Fact]
        public void SqrtLargeValues()
        {
            Assert.Equal(Fixed.FromInt(1 << 20), Fixed.Sqrt(Fixed.FromRaw(1L << 56)));
            var s = Fixed.Sqrt(Fixed.MaxValue);
            Assert.True(s.Raw > 0);
        }

        [Fact]
        public void ToStringFormat()
        {
            Assert.Equal("0.00000", Fixed.Zero.ToString());
            Assert.Equal("1.50000", Fixed.FromFraction(3, 2).ToString());
            Assert.Equal("-1.50000", Fixed.FromFraction(-3, 2).ToString());
            Assert.Equal("-0.50000", Fixed.FromFraction(-1, 2).ToString());
            Assert.Equal("0.33332", Fixed.FromFraction(1, 3).ToString());
            Assert.Equal("2147483647.00000", Fixed.FromInt(int.MaxValue).ToString());
            Assert.StartsWith("-", Fixed.MinValue.ToString());
        }

        [Fact]
        public void NoImplicitFloatConversion()
        {
            Assert.Null(typeof(Fixed).GetMethod("op_Implicit"));
        }

        [Fact]
        public void RandomSameSequence()
        {
            var a = new DetRandom(12345);
            var b = new DetRandom(12345);
            for (int i = 0; i < 1000; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
            var c = new DetRandom(12346);
            var d = new DetRandom(12345);
            bool differs = false;
            for (int i = 0; i < 10; i++) differs |= c.NextUInt() != d.NextUInt();
            Assert.True(differs);
        }

        [Fact]
        public void RandomKnownVector()
        {
            // PCG32 reference demo: seed 42, stream 54
            var r = new DetRandom(42UL, 54UL);
            uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
            foreach (var e in expected) Assert.Equal(e, r.NextUInt());
        }

        [Fact]
        public void RandomCopyByAssignment()
        {
            var a = new DetRandom(7);
            a.NextUInt();
            var copy = a;
            Assert.Equal(a.NextUInt(), copy.NextUInt());
            Assert.Equal(a.State, copy.State);
        }

        [Fact]
        public void RangeBoundsAndUniform()
        {
            var r = new DetRandom(99);
            const int n = 100000, buckets = 10;
            var counts = new int[buckets];
            for (int i = 0; i < n; i++)
            {
                int v = r.Range(-5, 5);
                Assert.InRange(v, -5, 4);
                counts[v + 5]++;
            }
            double expected = n / (double)buckets;
            foreach (var c in counts) Assert.InRange(c, expected * 0.98, expected * 1.02);
            Assert.Equal(3, r.Range(3, 4));
            Assert.Throws<ArgumentException>(() => r.Range(1, 1));
            int full = r.Range(int.MinValue, int.MaxValue);
            Assert.True(full < int.MaxValue);
        }

        [Fact]
        public void NextFixed01AndChance()
        {
            var r = new DetRandom(5);
            for (int i = 0; i < 10000; i++)
            {
                var f = r.NextFixed01();
                Assert.True(f >= Fixed.Zero && f < Fixed.One);
            }
            Assert.False(r.Chance(Fixed.Zero));
            Assert.True(r.Chance(Fixed.One));
            int hits = 0;
            for (int i = 0; i < 100000; i++) if (r.Chance(Fixed.Half)) hits++;
            Assert.InRange(hits, 49000, 51000);
        }
    }
}
