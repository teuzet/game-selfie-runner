using Game.Core.Math;

namespace Game.Core
{
    /// <summary>Детерминированный симулятор. Unity логики не держит, только зовёт Step.</summary>
    public sealed class Simulation
    {
        public const int TicksPerSecond = 30;

        public RaceConfig Config { get; }
        public RaceState State { get; }
        public int Tick => State.Tick;

        public Simulation(RaceConfig config)
        {
            Config = config;
            State = new RaceState(config.Seed);
        }

        private Simulation(RaceConfig config, RaceState state) { Config = config; State = state; }

        public Simulation Clone() => new Simulation(Config, State.Clone());

        public void CopyFrom(Simulation o) => State.CopyFrom(o.State);

        public ulong StateHash() => State.StateHash();

        private static readonly Fixed Dt = Fixed.FromFraction(1, TicksPerSecond);
        private static readonly Fixed MaxSpeed = Fixed.FromInt(8);
        private static readonly Fixed Accel = Fixed.FromInt(4);

        /// <summary>Каркасная динамика-заглушка: настоящую логику добавляют следующие фичи.</summary>
        public void Step(TickInputs inputs)
        {
            var st = State;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                // Внешний вход бота игнорируется (место ведёт сам Simulation; мозги ботов — позже).
                var inp = Config.GetSeat(i).Kind == SeatKind.Bot ? default : inputs.Get(i);
                ref RunnerState r = ref st.Runners[i];
                var target = MaxSpeed * Fixed.FromFraction(2 + inp.Pace, 3);
                var speed = Fixed.FromRaw(r.Speed);
                var d = Accel * Dt;
                if (speed < target) speed = Fixed.Min(speed + d, target);
                else speed = Fixed.Max(speed - d, target);
                r.Speed = speed.Raw;
                r.S = (Fixed.FromRaw(r.S) + speed * Dt).Raw;
                r.X = (Fixed.FromRaw(r.X) + Fixed.FromInt(inp.Steer) * Dt).Raw;
                if (inp.Action) r.Stamina = (Fixed.FromRaw(r.Stamina) - Dt).Raw;
            }
            st.Rng.NextUInt(); // поток ГСЧ продвигается каждый тик
            st.Tick++;
        }
    }
}
