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

        private static StreetData? _defaultData;
        private static RunnerData? _defaultRunnerData;

        /// <param name="street">Готовая улица; null — собрать из сида по Data/street.json, пул core.</param>
        public Simulation(RaceConfig config, StreetMap? street = null)
        {
            Config = config;
            RunnerData = _defaultRunnerData ??= RunnerData.Load();
            State = new RaceState(config.Seed, RunnerData);
            Street = street ?? StreetGenerator.Build(config.Seed, _defaultData ??= StreetData.Load(), StreetPool.Core);
        }

        private Simulation(RaceConfig config, RaceState state, StreetMap street, RunnerData rd) { Config = config; State = state; Street = street; RunnerData = rd; }

        public Simulation Clone() => new Simulation(Config, State.Clone(), Street, RunnerData);

        public void CopyFrom(Simulation o) => State.CopyFrom(o.State);

        public ulong StateHash() => State.StateHash();

        /// <summary>Тик: движение бегунов (темп, выносливость, руль, уклон, стены), затем расталкивание.</summary>
        public void Step(TickInputs inputs)
        {
            var st = State;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                // Внешний вход бота игнорируется (место ведёт сам Simulation; мозги ботов — позже).
                var inp = Config.GetSeat(i).Kind == SeatKind.Bot ? default : inputs.Get(i);
                RunnerMovement.StepRunner(RunnerData, Street, ref st.Runners[i], inp);
            }
            RunnerMovement.Separate(RunnerData, Street, st.Runners);
            st.Rng.NextUInt(); // поток ГСЧ продвигается каждый тик
            st.Tick++;
        }
    }
}
