using Game.Core.Bull;
using Game.Core.Math;
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

        public RunnerData RunnerData { get; }
        public BullData BullData { get; }
        public CharacterData CharacterData { get; }
        public ActionData ActionData { get; }
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
            _seatData = new RunnerData[RaceConfig.SeatCount];
            for (int i = 0; i < _seatData.Length; i++)
                _seatData[i] = Characters.ForSeat(RunnerData, CharacterData.Get(config.GetSeat(i).CharacterId));
            Street = street ?? StreetGenerator.Build(config.Seed, data.StreetData, StreetPool.Core);
            State = new RaceState(config.Seed, RunnerData, BullData, BullMovement.StartLine(BullData, Street), Street, config, CharacterData);
        }

        private Simulation(RaceConfig config, RaceState state, StreetMap street, RunnerData rd, BullData bd, CharacterData cd, ActionData ad, RunnerData[] seatData)
        { Config = config; State = state; Street = street; RunnerData = rd; BullData = bd; CharacterData = cd; ActionData = ad; _seatData = seatData; }

        /// <summary>Набор параметров персонажа места (SeatConfig.CharacterId).</summary>
        public CharacterDef CharacterOf(int seat) => CharacterData.Get(Config.GetSeat(seat).CharacterId);

        /// <summary>RunnerData места с применённым персонажем.</summary>
        public RunnerData SeatData(int seat) => _seatData[seat];

        public Simulation Clone() => new Simulation(Config, State.Clone(), Street, RunnerData, BullData, CharacterData, ActionData, _seatData);

        public void CopyFrom(Simulation o) => State.CopyFrom(o.State);

        public ulong StateHash() => State.StateHash();

        /// <summary>Тик: движение бегунов (темп, выносливость, руль, уклон, стены), затем расталкивание.</summary>
        public void Step(TickInputs inputs)
        {
            var st = State;
            Collisions.BeforeMove(st);
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                // Внешний вход бота игнорируется (место ведёт сам Simulation; мозги ботов — позже).
                var inp = Config.GetSeat(i).Kind == SeatKind.Bot ? default : inputs.Get(i);
                RunnerMovement.StepRunner(_seatData[i], Street, ref st.Runners[i], inp);
            }
            RunnerMovement.Separate(RunnerData, Street, st.Runners);
            MovingObstacles.Step(Street, st.Dyn, st.Runners, st.Tick);
            Collisions.After(RunnerData, Street, st);
            Actions.Step(ActionData, CharacterData, _seatData, Street, st, Config, inputs);
            BullMovement.Step(BullData, Street, ref st.Bull, st.Runners);
            st.Rng.NextUInt(); // поток ГСЧ продвигается каждый тик
            st.Tick++;
        }
    }
}
