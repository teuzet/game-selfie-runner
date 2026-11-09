using System;

namespace Game.Net
{
    /// <summary>Ненадёжный датаграммный транспорт. peer — номер удалённого участника (0..N-1).</summary>
    public interface ITransport
    {
        void Send(int peer, ReadOnlySpan<byte> data);

        /// <summary>
        /// Достаёт следующий пришедший пакет. Возвращает длину записанного в buffer (&gt; 0)
        /// или 0, если пакетов нет. Пакет длиннее buffer отбрасывается.
        /// </summary>
        int Poll(out int peer, Span<byte> buffer);
    }
}
