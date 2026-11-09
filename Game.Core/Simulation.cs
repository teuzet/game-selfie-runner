using Game.Core.Bots;
using Game.Core.Bull;
using Game.Core.Economy;
using Game.Core.Events;
using Game.Core.Hints;
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
        public HintData HintData { get; }
        /// <summary>Откуда боты берут подсказки чата: по умолчанию модуль Hints (F-021); null — боты без подсказок (тесты).</summary>
        public IBotHintSource? BotHints { get; set; } = new ChatHintSource();
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
            HintData = data.HintData;
            _brains = BotBrains.CreateAll(BotData, config);
            _seatData = Characters.ForSeats(RunnerData, CharacterData, config);
            Street = street ?? StreetGenerator.Build(config.Seed, data.StreetData, StreetPool.Core);
            ArenaStart = Phases.ArenaStart(Street);
            State = new RaceState(config, data, Street, BullMovement.StartLine(BullData, Street));
        }

        private Simulation(RaceConfig config, RaceState state, StreetMap street, RunnerData rd, BullData bd, CharacterData cd, ActionData ad, EconomyData ed, RunnerData[] seatData, Fixed arenaStart, BotData bots, IBotBrain?[] brains, HintData hints)
        { HintData = hints; BotData = bots; _brains = brains; ArenaStart = arenaStart; EconomyData = ed; Config = config; State = state; Street = street; RunnerData = rd; BullData = bd; CharacterData = cd; ActionData = ad; _seatData = seatData; }

        /// <summary>Набор параметров персонажа места (SeatConfig.CharacterId).</summary>
        public CharacterDef CharacterOf(int seat) => CharacterData.Get(Config.GetSeat(seat).CharacterId);

        /// <summary>RunnerData места с применённым персонажем.</summary>
        public RunnerData SeatData(int seat) => _seatData[seat];

        public Simulation Clone()
        {
            var c = new Simulation(Config, State.Clone(), Street, RunnerData, BullData, CharacterData, ActionData, EconomyData, _seatData, ArenaStart, BotData, _brains, HintData);
            c.BotHints = BotHints;
            c.Events.CopyFrom(Events);
            return c;
        }

        public void CopyFrom(Simulation o) { State.CopyFrom(o.State); Events.CopyFrom(o.Events); }

        public ulong StateHash() => State.StateHash();

        /// <summary>Тик: список вызовов модулей в порядке MODULES.md → «Порядок шага». Логики здесь нет.</summary>
        public void Step(TickInputs inputs)
        {
            var st = State;
            Events.Clear();
            if (st.Phase == RacePhase.Over) { st.Tick++; return; } // итоги: состояние заморожено
            if (st.Phase == RacePhase.Countdown) { StepCountdown(); return; }
            ChatHints.Step(this);
            Collisions.BeforeMove(st);
            BotBrains.FillInputs(_brains, st, Street, BotData, Config, BotHints, ref inputs);
            RunnerMovement.MuteNoSignal(ref inputs, st.Runners);
            RunnerMovement.StepAll(_seatData, Street, st.Runners, inputs);
            RunnerMovement.Separate(RunnerData, Street, st.Runners);
            MovingObstacles.Step(Street, st.Dyn, st.Runners, st.Tick);
            Collisions.After(RunnerData, Street, st);
            Actions.Step(ActionData, CharacterData, _seatData, Street, st, Config, inputs);
            BullCharge.StepBull(this);
            Viewers.Step(this);
            FrameSteal.Step(this);
            EventScan.Run(this);
            Phases.Step(this);
            st.Rng.NextUInt(); // поток ГСЧ продвигается каждый тик
            st.Tick++;
        }

        /// <summary>Отсчёт (шаг 1): бегуны стоят, зрители не меняются; бык в загоне только ведёт таймер срыва.</summary>
        private void StepCountdown()
        {
            BullMovement.Step(BullData, Street, ref State.Bull, State.Runners);
            Phases.StepCountdown(this);
            State.Tick++;
        }
    }
}
