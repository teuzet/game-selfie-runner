using System;

namespace Game.Core
{
    /// <summary>Вход одного места: руль −1/0/+1, темп −1/0/+1, действие. Упаковка в 1 байт.</summary>
    public readonly struct PlayerInput : IEquatable<PlayerInput>
    {
        public readonly sbyte Steer;
        public readonly sbyte Pace;
        public readonly bool Action;

        public PlayerInput(int steer, int pace, bool action)
        {
            Steer = (sbyte)(steer < -1 ? -1 : steer > 1 ? 1 : steer);
            Pace = (sbyte)(pace < -1 ? -1 : pace > 1 ? 1 : pace);
            Action = action;
        }

        /// <summary>Биты: 0-1 руль+1, 2-3 темп+1, 4 действие.</summary>
        public byte Pack() => (byte)((Steer + 1) | ((Pace + 1) << 2) | (Action ? 16 : 0));

        public static PlayerInput Unpack(byte b)
        {
            int s = (b & 3) - 1, p = ((b >> 2) & 3) - 1;
            return new PlayerInput(s, p, (b & 16) != 0);
        }

        public bool Equals(PlayerInput o) => Steer == o.Steer && Pace == o.Pace && Action == o.Action;
        public override bool Equals(object? obj) => obj is PlayerInput o && Equals(o);
        public override int GetHashCode() => Pack();
    }

    /// <summary>Входы всех 4 мест на один тик. 4 байта в упакованном виде.</summary>
    public readonly struct TickInputs : IEquatable<TickInputs>
    {
        public const int PackedSize = RaceConfig.SeatCount;
        public static readonly TickInputs Empty = default;

        // По байту на место, хранится XOR с Neutral, чтобы default был «руль 0, темп 0, без действия».
        private const uint Neutral = 0x05050505u;
        private readonly uint _packed;

        private TickInputs(uint packed) { _packed = packed; }

        public PlayerInput Get(int seat)
        {
            return PlayerInput.Unpack((byte)((_packed ^ Neutral) >> (seat * 8)));
        }

        public TickInputs With(int seat, PlayerInput input)
        {
            int shift = seat * 8;
            uint cur = (_packed ^ Neutral) & ~(0xFFu << shift);
            return new TickInputs((cur | ((uint)input.Pack() << shift)) ^ Neutral);
        }

        public void WriteTo(Span<byte> dst)
        {
            for (int i = 0; i < PackedSize; i++) dst[i] = Get(i).Pack();
        }

        public static TickInputs ReadFrom(ReadOnlySpan<byte> src)
        {
            var r = Empty;
            for (int i = 0; i < PackedSize; i++) r = r.With(i, PlayerInput.Unpack(src[i]));
            return r;
        }

        public byte[] ToBytes() { var b = new byte[PackedSize]; WriteTo(b); return b; }

        public bool Equals(TickInputs o) => _packed == o._packed;
        public override bool Equals(object? obj) => obj is TickInputs o && Equals(o);
        public override int GetHashCode() => (int)_packed;
    }
}
