using System;
using Game.Core.Math;

namespace Game.Core.Street
{
    /// <summary>Пул блоков сборки: `core` или `core + later` (design/F-003.md §2, п. 7).</summary>
    public enum StreetPool : byte { Core = 0, CorePlusLater = 1 }

    /// <summary>Полоса препятствия. `D` при расстановке превращается в L или R.</summary>
    public enum Lane : byte { L = 0, R = 1, C = 2, M = 3, F = 4 }

    /// <summary>Блок, поставленный на улицу: s-начало, длина и диапазон его препятствий.</summary>
    public readonly struct PlacedBlock
    {
        public readonly BlockDef Def;
        public readonly Fixed Start, Length;
        /// <summary>Индекс первого препятствия блока в <see cref="Street"/> и их число.</summary>
        public readonly int FirstObstacle, ObstacleCount;
        /// <summary>Сторона воды в пруду: -1 слева, +1 справа, 0 — воды нет.</summary>
        public readonly int WaterSide;

        public PlacedBlock(BlockDef def, Fixed start, Fixed length, int firstObstacle, int obstacleCount, int waterSide)
        {
            Def = def; Start = start; Length = length;
            FirstObstacle = firstObstacle; ObstacleCount = obstacleCount; WaterSide = waterSide;
        }

        public Fixed End => Start + Length;

        internal PlacedBlock WithObstacles(int first, int count) => new PlacedBlock(Def, Start, Length, first, count, WaterSide);
        internal PlacedBlock WithWater(int side) => new PlacedBlock(Def, Start, Length, FirstObstacle, ObstacleCount, side);
    }

    /// <summary>Препятствие на улице. Размеры — вдоль s и поперёк; у белья SizeAcross = ширина улицы в точке.</summary>
    public readonly struct StreetObstacle
    {
        public readonly ObstacleDef Def;
        public readonly int Block;
        public readonly Fixed S, X, SizeAlong, SizeAcross;
        public readonly Lane Lane;
        /// <summary>Дверь: порог открытия, м (8–16). Зевака: направление старта, ±1. Иначе 0.</summary>
        public readonly Fixed Param;

        public StreetObstacle(ObstacleDef def, int block, Fixed s, Fixed x, Fixed sizeAlong, Fixed sizeAcross, Lane lane, Fixed param)
        {
            Def = def; Block = block; S = s; X = x; SizeAlong = sizeAlong; SizeAcross = sizeAcross; Lane = lane; Param = param;
        }

        public string Type => Def.Id;
    }

    /// <summary>Собранная улица. Неизменяема после сборки; запросы без аллокаций.</summary>
    public sealed class Street
    {
        public readonly ulong Seed;
        public readonly StreetPool Pool;
        /// <summary>Сколько попыток понадобилось (1 = с первой).</summary>
        public readonly int Attempts;
        public readonly PlacedBlock[] Blocks;
        public readonly StreetObstacle[] Obstacles;
        public readonly Fixed Length;

        internal Street(ulong seed, StreetPool pool, int attempts, PlacedBlock[] blocks, StreetObstacle[] obstacles)
        {
            Seed = seed; Pool = pool; Attempts = attempts; Blocks = blocks; Obstacles = obstacles;
            Length = blocks[blocks.Length - 1].End;
        }

        /// <summary>Индекс блока, содержащего s (вне улицы — крайний блок).</summary>
        public int BlockIndexAt(Fixed s)
        {
            int lo = 0, hi = Blocks.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (Blocks[mid].Start <= s) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>Ширина улицы в точке s (линейно между точками профиля блока).</summary>
        public Fixed WidthAt(Fixed s)
        {
            ref readonly PlacedBlock b = ref Blocks[BlockIndexAt(s)];
            return ProfileWidth(b.Def, s - b.Start);
        }

        /// <summary>Минимум ширины на отрезке [s0, s1] (в блоке, где лежит s0), по точкам профиля.</summary>
        public Fixed MinWidthOn(Fixed s0, Fixed s1)
        {
            ref readonly PlacedBlock b = ref Blocks[BlockIndexAt(s0)];
            Fixed a = s0 - b.Start, c = s1 - b.Start;
            Fixed m = Fixed.Min(ProfileWidth(b.Def, a), ProfileWidth(b.Def, c));
            var pts = b.Def.Width;
            for (int i = 0; i < pts.Length; i++)
                if (pts[i].S > a && pts[i].S < c) m = Fixed.Min(m, pts[i].Width);
            return m;
        }

        public static readonly Fixed SlopeDown = Fixed.FromFraction(11, 10);
        public static readonly Fixed SlopeUp = Fixed.FromFraction(9, 10);

        /// <summary>Множитель скорости уклона: спуск 1.10, подъём 0.90, ровно 1.00 (design/F-003.md §2).</summary>
        public Fixed SlopeAt(Fixed s)
        {
            switch (Blocks[BlockIndexAt(s)].Def.Slope)
            {
                case Slope.Down: return SlopeDown;
                case Slope.Up: return SlopeUp;
                default: return Fixed.One;
            }
        }

        /// <summary>Препятствия с s0 ≤ s &lt; s1, по возрастанию s. Срез массива улицы, без аллокаций.</summary>
        public ReadOnlySpan<StreetObstacle> ObstaclesIn(Fixed s0, Fixed s1)
        {
            int a = LowerBound(s0), b = LowerBound(s1);
            if (b <= a) return ReadOnlySpan<StreetObstacle>.Empty;
            return new ReadOnlySpan<StreetObstacle>(Obstacles, a, b - a);
        }

        int LowerBound(Fixed s)
        {
            int lo = 0, hi = Obstacles.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (Obstacles[mid].S < s) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        internal static Fixed ProfileWidth(BlockDef d, Fixed local)
        {
            var p = d.Width;
            if (local <= p[0].S) return p[0].Width;
            for (int i = 1; i < p.Length; i++)
            {
                if (local <= p[i].S)
                {
                    Fixed span = p[i].S - p[i - 1].S;
                    return p[i - 1].Width + (p[i].Width - p[i - 1].Width) * (local - p[i - 1].S) / span;
                }
            }
            return p[p.Length - 1].Width;
        }

        /// <summary>FNV-1a по блокам и препятствиям — для сравнения улиц.</summary>
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < Blocks.Length; i++)
            {
                h = Mix(h, (ulong)Blocks[i].Start.Raw);
                h = Mix(h, (ulong)Blocks[i].Length.Raw);
                h = Mix(h, (ulong)Blocks[i].WaterSide);
                string id = Blocks[i].Def.Id;
                for (int k = 0; k < id.Length; k++) h = Mix(h, id[k]);
            }
            for (int i = 0; i < Obstacles.Length; i++)
            {
                ref readonly StreetObstacle o = ref Obstacles[i];
                string id = o.Def.Id;
                for (int k = 0; k < id.Length; k++) h = Mix(h, id[k]);
                h = Mix(h, (ulong)o.S.Raw); h = Mix(h, (ulong)o.X.Raw);
                h = Mix(h, (ulong)o.Lane); h = Mix(h, (ulong)o.Param.Raw);
            }
            return h;
        }

        static ulong Mix(ulong h, ulong v)
        {
            unchecked { return (h ^ v) * 1099511628211UL; }
        }
    }
}
