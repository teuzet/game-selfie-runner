namespace Game.Core.Race
{
    /// <summary>Один «лучший момент»: место, тик и величина. Seat == -1 — момента не было.</summary>
    public struct Moment
    {
        public int Seat;
        /// <summary>Тик начала момента (для максимума зрителей — тик достижения).</summary>
        public int Tick;
        /// <summary>MaxViewers: зрители (Fixed raw); LongestHot: длина отрезка в тиках; CostliestFail: потеряно зрителей (Fixed raw).</summary>
        public long Value;

        public static Moment None => new Moment { Seat = -1, Tick = -1, Value = 0 };

        public ulong Hash(ulong h)
        {
            h = Math.Hashing.Mix(h, (ulong)(uint)Seat);
            h = Math.Hashing.Mix(h, (ulong)(uint)Tick);
            return Math.Hashing.Mix(h, (ulong)Value);
        }
    }

    /// <summary>3 лучших момента забега, накапливаются по ходу и входят в RaceState и хэш (design/overview.md, «итоговый экран»).</summary>
    public struct Highlights
    {
        public Moment MaxViewers;      // максимум зрителей
        public Moment LongestHot;      // самый длинный отрезок подряд в горячей зоне
        public Moment CostliestFail;   // самый дорогой фейл: подброс, потеря зрителей за «нет сигнала»

        public static Highlights Empty => new Highlights { MaxViewers = Moment.None, LongestHot = Moment.None, CostliestFail = Moment.None };

        public ulong Hash(ulong h) => CostliestFail.Hash(LongestHot.Hash(MaxViewers.Hash(h)));
    }

    /// <summary>Итоги забега: порядок мест; лучшие моменты лежат в RaceState.Best.</summary>
    public static class Results
    {
        /// <summary>Заполняет order[0..3] местами от победителя: по донатам, при равенстве — по зрителям, затем по меньшему месту.</summary>
        public static void Standings(RaceState st, int[] order)
        {
            for (int i = 0; i < RaceConfig.SeatCount; i++) order[i] = i;
            for (int i = 1; i < RaceConfig.SeatCount; i++)
            {
                int s = order[i];
                int j = i - 1;
                while (j >= 0 && Better(st, s, order[j])) { order[j + 1] = order[j]; j--; }
                order[j + 1] = s;
            }
        }

        public static int Winner(RaceState st)
        {
            int best = 0;
            for (int i = 1; i < RaceConfig.SeatCount; i++) if (Better(st, i, best)) best = i;
            return best;
        }

        static bool Better(RaceState st, int a, int b)
        {
            long da = st.Runners[a].Viewing.Donations, db = st.Runners[b].Viewing.Donations;
            if (da != db) return da > db;
            return st.Runners[a].Viewing.Viewers > st.Runners[b].Viewing.Viewers; // полное равенство — меньшее место выше (сортировка устойчива)
        }
    }
}
