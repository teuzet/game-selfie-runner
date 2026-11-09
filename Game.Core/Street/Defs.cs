using Game.Core.Math;

namespace Game.Core.Street
{
    public enum Slope { Flat, Down, Up }

    public readonly struct WidthPoint
    {
        public readonly Fixed S;
        public readonly Fixed Width;
        public WidthPoint(Fixed s, Fixed width) { S = s; Width = width; }
    }

    public sealed class BlockObstacleDef
    {
        public string Type = "";
        /// <summary>Лейны как есть: L, R, C, M, F, D.</summary>
        public string[] Lanes = new string[0];
        public int Weight;
    }

    /// <summary>Фиксированный элемент блока: fountain, water, zone. Неиспользуемые поля = 0/пусто.</summary>
    public sealed class FixedElementDef
    {
        public string Kind = "";
        public string Type = "";
        public string Side = "";
        public Fixed S, X, Size, S0, S1, Across;
    }

    public sealed class BlockDef
    {
        public string Id = "";
        public string Name = "";
        public string Tier = "";
        public bool Special;
        public Fixed LengthMin, LengthMax;
        /// <summary>Шаг длины (0, если длина фиксирована).</summary>
        public Fixed LengthStep;
        public WidthPoint[] Width = new WidthPoint[0];
        public Slope Slope;
        public Fixed Rise;
        public Fixed SlotStep, SlotMargin, DensityMul;
        public BlockObstacleDef[] Obstacles = new BlockObstacleDef[0];
        public FixedElementDef[] Fixed = new FixedElementDef[0];
        public string[] Next = new string[0];
        public string Note = "";

        public Fixed StartWidth => Width[0].Width;
        public Fixed EndWidth => Width[Width.Length - 1].Width;
    }

    public sealed class ObstacleDef
    {
        public string Id = "";
        public string Name = "";
        public string Tier = "";
        /// <summary>circle | rect | zone</summary>
        public string Shape = "";
        /// <summary>Диаметр круга.</summary>
        public Fixed Size;
        public Fixed SizeAlong, SizeAcross;
        public bool FullWidth;
        public string[] Lanes = new string[0];
        /// <summary>stun | slow | zone</summary>
        public string Effect = "";
        public Fixed Stun, SlowMul, SlowTime, PushBack;
        /// <summary>none | door | walk</summary>
        public string MotionKind = "none";
        public Fixed OpenTime, TriggerMin, TriggerMax, Speed, WallGap;
        public string Note = "";
    }
}
