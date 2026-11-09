namespace Game.Core
{
    public enum SeatKind : byte { Human = 0, Bot = 1 }

    /// <summary>Настройки одного места. Пока только хранятся.</summary>
    public readonly struct SeatConfig
    {
        public readonly SeatKind Kind;
        public readonly int CharacterId;
        /// <summary>Характер бота (id из данных); для человека не используется.</summary>
        public readonly int BotTemperament;

        public SeatConfig(SeatKind kind, int characterId, int botTemperament = 0)
        {
            Kind = kind; CharacterId = characterId; BotTemperament = botTemperament;
        }
    }

    public sealed class RaceConfig
    {
        public const int SeatCount = 4;
        public readonly ulong Seed;
        private readonly SeatConfig[] _seats;

        public RaceConfig(ulong seed, SeatConfig[] seats)
        {
            if (seats == null || seats.Length != SeatCount)
                throw new System.ArgumentException("Нужно ровно 4 места", nameof(seats));
            Seed = seed;
            _seats = (SeatConfig[])seats.Clone();
        }

        public SeatConfig GetSeat(int seat) => _seats[seat];
    }
}
