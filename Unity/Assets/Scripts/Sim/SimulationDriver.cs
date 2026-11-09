using Game.Core;
using UnityEngine;

namespace SelfieRunner.Sim
{
    /// <summary>Единственный MonoBehaviour, который зовёт Simulation.Step: тики 30/с, вход игрока — в его место.</summary>
    public sealed class SimulationDriver : MonoBehaviour
    {
        private readonly TickAccumulator _accumulator = new TickAccumulator();
        private IPlayerInputSource _source;

        public Simulation Sim { get; private set; }
        /// <summary>Место первого SeatKind.Human; −1, если людей в конфиге нет.</summary>
        public int PlayerSeat { get; private set; } = -1;
        public TickInputs LastInputs { get; private set; }
        public PlayerInput PlayerInput { get; private set; }

        public void Init(RaceConfig config, GameData data, IPlayerInputSource source)
        {
            Sim = new Simulation(config, data);
            _source = source;
            PlayerSeat = -1;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
                if (config.GetSeat(i).Kind == SeatKind.Human) { PlayerSeat = i; break; }
            LastInputs = default;
            PlayerInput = default;
        }

        /// <summary>Копит время и делает столько шагов, сколько вернул аккумулятор; возвращает число шагов.</summary>
        public int Advance(double dt)
        {
            int steps = _accumulator.Advance(dt);
            for (int i = 0; i < steps; i++) StepOnce();
            return steps;
        }

        public void StepOnce()
        {
            PlayerInput = _source != null ? _source.Read() : default;
            var inputs = TickInputs.Empty;
            if (PlayerSeat >= 0) inputs = inputs.With(PlayerSeat, PlayerInput);
            LastInputs = inputs;
            Sim.Step(inputs);
        }

        private void Update()
        {
            if (Sim != null) Advance(Time.deltaTime);
        }
    }
}
