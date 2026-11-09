using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>Журнал входов по тикам: запись и проигрывание.</summary>
    public sealed class InputLog
    {
        private readonly List<TickInputs> _inputs = new List<TickInputs>();

        public int Count => _inputs.Count;

        /// <summary>Записать вход тика (последовательно или перезаписью).</summary>
        public void Record(int tick, TickInputs inputs)
        {
            if (tick < 0 || tick > _inputs.Count) throw new ArgumentOutOfRangeException(nameof(tick));
            if (tick == _inputs.Count) _inputs.Add(inputs); else _inputs[tick] = inputs;
        }

        public TickInputs Get(int tick) => _inputs[tick];

        /// <summary>Шагает симуляцию по журналу, пока Tick &lt; untilTick.</summary>
        public void Replay(Simulation sim, int untilTick)
        {
            while (sim.Tick < untilTick) sim.Step(_inputs[sim.Tick]);
        }
    }
}
