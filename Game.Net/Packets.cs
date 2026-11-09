using System;
using Game.Core;

namespace Game.Net
{
    public static class PacketType
    {
        public const byte Input = 1;
        public const byte Hash = 2;
    }

    /// <summary>
    /// Входы места seat за тики tick-7..tick (избыточность против потерь).
    /// Формат (14 байт): type, seat, tick u32 LE, 8 байт входов (индекс 0 = самый старый, tick-7).
    /// </summary>
    public struct InputPacket
    {
        public const int History = 8;
        public const int Size = 6 + History;

        public int Tick;
        public int Seat;
        private ulong _bytes; // упакованные PlayerInput, старший байт — самый старый тик

        /// <summary>Вход тика (Tick - back), back 0..7.</summary>
        public PlayerInput GetBack(int back) => PlayerInput.Unpack((byte)(_bytes >> ((History - 1 - back) * 8)));

        public void SetBack(int back, PlayerInput input)
        {
            int shift = (History - 1 - back) * 8;
            _bytes = (_bytes & ~(0xFFUL << shift)) | ((ulong)input.Pack() << shift);
        }

        /// <summary>Вход на абсолютном тике t, если он в окне пакета.</summary>
        public bool TryGet(int t, out PlayerInput input)
        {
            int back = Tick - t;
            if (back < 0 || back >= History) { input = default; return false; }
            input = GetBack(back);
            return true;
        }

        public int Write(Span<byte> dst)
        {
            if (dst.Length < Size) throw new ArgumentException("buffer too small");
            dst[0] = PacketType.Input;
            dst[1] = (byte)Seat;
            WriteU32(dst.Slice(2), (uint)Tick);
            for (int i = 0; i < History; i++) dst[6 + i] = (byte)(_bytes >> ((History - 1 - i) * 8));
            return Size;
        }

        public static bool TryRead(ReadOnlySpan<byte> src, out InputPacket p)
        {
            p = default;
            if (src.Length < Size || src[0] != PacketType.Input || src[1] >= RaceConfig.SeatCount) return false;
            p.Seat = src[1];
            p.Tick = (int)ReadU32(src.Slice(2));
            for (int i = 0; i < History; i++) p._bytes |= (ulong)src[6 + i] << ((History - 1 - i) * 8);
            return true;
        }

        internal static void WriteU32(Span<byte> d, uint v)
        {
            d[0] = (byte)v; d[1] = (byte)(v >> 8); d[2] = (byte)(v >> 16); d[3] = (byte)(v >> 24);
        }

        internal static uint ReadU32(ReadOnlySpan<byte> s) =>
            (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));
    }

    /// <summary>Хэш состояния подтверждённого тика. Формат (13 байт): type, tick u32 LE, hash u64 LE.</summary>
    public struct HashPacket
    {
        public const int Size = 13;

        public int Tick;
        public ulong Hash;

        public int Write(Span<byte> dst)
        {
            if (dst.Length < Size) throw new ArgumentException("buffer too small");
            dst[0] = PacketType.Hash;
            InputPacket.WriteU32(dst.Slice(1), (uint)Tick);
            InputPacket.WriteU32(dst.Slice(5), (uint)Hash);
            InputPacket.WriteU32(dst.Slice(9), (uint)(Hash >> 32));
            return Size;
        }

        public static bool TryRead(ReadOnlySpan<byte> src, out HashPacket p)
        {
            p = default;
            if (src.Length < Size || src[0] != PacketType.Hash) return false;
            p.Tick = (int)InputPacket.ReadU32(src.Slice(1));
            p.Hash = InputPacket.ReadU32(src.Slice(5)) | ((ulong)InputPacket.ReadU32(src.Slice(9)) << 32);
            return true;
        }
    }
}
