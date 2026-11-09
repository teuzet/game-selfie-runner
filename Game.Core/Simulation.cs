using Game.Core.Bots;
using Game.Core.Bull;
using Game.Core.Economy;
using Game.Core.Events;
using Game.Core.Math;
using Game.Core.Race;
using Game.Core.Runners;
using Game.Core.Street;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core
{
    /// <summary>Детерминированный симулятор. Unity логики не держит, только зовёт Step.</summary>
    public sealed class Simulation
    {
        public const int TicksPerSecond = 30;

        public RaceConfig Config { get; }
        public RaceState State { get; }
        public int Tick => State.Tick;

        /// <summary>Улица забега: строится из сида при создании, не входит в RaceState (неизменяема, при Clone общая).</summary>
        public StreetMap Street { get; }

        /// <summary>События последнего Step (F-019): очищается в начале Step, в RaceState и хэш не входит.</summary>
        public RaceEvents Events { get; } = new RaceEvents();

        /// <summary>Начало финиш-арены: вход в неё — финиш бегуна (F-020).</summary>
        public Fixed ArenaStart { get; }

        public RunnerData RunnerData { get; }
        public BullData BullData { get; }
        public CharacterData CharacterData { get; }
        public ActionData ActionData { get; }
        public EconomyData EconomyData { get; }
        public BotData BotData { get; }
        /// <summary>Откуда боты берут подсказки чата: заглушка (null — подсказок нет) до F-021.</summary>
        public IBotHintSource? BotHints { get; set; }
        /// <summary>Мозги по местам (null у людей); неизменяемы, при Clone общие.</summary>
        private readonly IBotBrain?[] _brains;
        /// <summary>RunnerData по местам с применённым персонажем (скорость, перк выносливости).</summary>
        private readonly RunnerData[] _seatData;

        private static GameData? _defaultGameData;

        /// <summary>Обёртка для тестов: данные читаются с диска один раз на процесс (GameData.Load()).</summary>
        public Simulation(RaceConfig config, StreetMap? street = null)
            : this(config, _defaultGameData ??= GameData.Load(), street) { }

        /// <summary>Основной конструктор: диск не читает. street == null — собрать из сида по data.StreetData, пул core.</summary>
        public Simulation(RaceConfig config, GameData data, StreetMap? street = null)
        {
            Config = config;
            RunnerData = data.RunnerData;
            BullData = data.BullData;
            CharacterData = data.CharacterData;
            ActionData = data.ActionData;
            EconomyData = data.EconomyData;
            BotData = data.BotData;
            _brains = new IBotBrain?[RaceConfig.SeatCount];
            for (int i = 0; i < _brains.Length; i++)
                if (config.GetSeat(i).Kind == SeatKind.Bot && BotData.Temperaments.Length > 0)
                    _brains[i] = BotBrains.Create(BotData, config.GetSeat(i).BotTemperament);
            _seatData = new RunnerData[RaceConfig.SeatCount];
            for (int i = 0; i < _seatData.Length; i++)
                _seatData[i] = Characters.ForSeat(RunnerData, CharacterData.Get(config.GetSeat(i).CharacterId));
            Street = street ?? StreetGenerator.Build(config.Seed, data.StreetData, StreetPool.Core);
            ArenaStart = Phases.ArenaStart(Street);
            State = new RaceState(config.Seed, RunnerData, BullData, BullMovement.StartLine(BullData, Street), Street, config, CharacterData, EconomyData);
        }

        private Simulation(RaceConfig config, RaceState state, StreetMap street, RunnerData rd, BullData bd, CharacterData cd, ActionData ad, EconomyData ed, RunnerData[] seatData, Fixed arenaStart, BotData bots, IBotBrain?[] brains)
        { BotData = bots; _brains = brains; ArenaStart = arenaStart; EconomyData = ed; Config = config; State = state; Street = street; RunnerData = rd; BullData = bd; CharacterData = cd; ActionData = ad; _seatData = seatData; }

        /// <summary>Набор параметров персонажа места (SeatConfig.CharacterId).</summary>
        public CharacterDef CharacterOf(int seat) => CharacterData.Get(Config.GetSeat(seat).CharacterId);

        /// <summary>RunnerData места с применённым персонажем.</summary>
        public RunnerData SeatData(int seat) => _seatData[seat];

        public Simulation Clone()
        {
            var c = new Simulation(Config, State.Clone(), Street, RunnerData, BullData, CharacterData, ActionData, EconomyData, _seatData, ArenaStart, BotData, _brains);
            c.BotHints = BotHints;
            c.Events.CopyFrom(Events);
            return c;
        }

        public void CopyFrom(Simulation o) { State.CopyFrom(o.State); Events.CopyFrom(o.Events); }

        public ulong StateHash() => State.StateHash();

        /// <summary>Тик: движение бегунов (темп, выносливость, руль, уклон, стены), затем расталкивание.</summary>
        public void Step(TickInputs inputs)
        {
            var st = State;
            Events.Clear();
            if (st.Phase == RacePhase.Over) { st.Tick++; return; } // итоги: состояние заморожено
            if (st.Phase == RacePhase.Countdown)
            {
                // Отсчёт: бегуны стоят, зрители не меняются; бык в загоне только ведёт таймер срыва.
                BullMovement.Step(BullData, Street, ref st.Bull, st.Runners);
                Phases.StepCountdown(this);
                st.Tick++;
                return;
            }
            Collisions.BeforeMove(st);
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                // Место бота ведёт мозг (F-023): внешний вход игнорируется, подменяется решением по наблюдениям.
                IBotBrain? brain = _brains[i];
                if (brain != null) inputs = inputs.With(i, BotBrains.Think(brain, st, Street, BotData, i, BotHints));
                else if (Config.GetSeat(i).Kind == SeatKind.Bot) inputs = inputs.With(i, default);
            }
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                // «Нет сигнала» (F-017): вход игнорируется и для движения, и для действий.
                if (st.Runners[i].Toss.NoSignalTicks > 0) inputs = inputs.With(i, default);
            }
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                RunnerMovement.StepRunner(_seatData[i], Street, ref st.Runners[i], inputs.Get(i), st.Runners[i].Toss.NoSignalTicks > 0);
            }
            RunnerMovement.Separate(RunnerData, Street, st.Runners);
            MovingObstacles.Step(Street, st.Dyn, st.Runners, st.Tick);
            Collisions.After(RunnerData, Street, st);
            Actions.Step(ActionData, CharacterData, _seatData, Street, st, Config, inputs);
            long prevBullS = st.Bull.S;
            BullMovement.Step(BullData, Street, ref st.Bull, st.Runners);
            BullCharge.Step(this, prevBullS);
            Viewers.Step(this);
            FrameSteal.Step(this);
            EventScan.Run(this);
            Phases.Step(this);
            st.Rng.NextUInt(); // поток ГСЧ продвигается каждый тик
            st.Tick++;
        }
    }
}
