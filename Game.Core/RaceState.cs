using Game.Core.Math;

namespace Game.Core
{
    /// <summary>Всё состояние забега. Только структуры и массивы фиксированного размера.</summary>
    public sealed class RaceState
    {
        public int Tick;
        public DetRandom Rng;
        public readonly RunnerState[] Runners = new RunnerState[RaceConfig.SeatCount];
        public BullState Bull;

        public RaceState(ulong seed, Game.Core.Runners.RunnerData runnerData, Game.Core.Bull.BullData bullData)
        {
            Rng = new DetRandom(seed);
            Game.Core.Bull.BullMovement.Init(bullData, ref Bull);
            for (int i = 0; i < Runners.Length; i++)
                Game.Core.Runners.RunnerMovement.InitRunner(runnerData, ref Runners[i]);
        }

        private RaceState() { }

        public RaceState Clone()
        {
            var c = new RaceState();
            c.CopyFrom(this);
            return c;
        }

        public void CopyFrom(RaceState o)
        {
            Tick = o.Tick;
            Rng = o.Rng;
            Bull = o.Bull;
            System.Array.Copy(o.Runners, Runners, Runners.Length);
        }

        /// <summary>FNV-1a (64 бита) по всем полям.</summary>
        public ulong StateHash()
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (ulong)Tick);
            h = Mix(h, Rng.State);
            h = Mix(h, Rng.Inc);
            for (int i = 0; i < Runners.Length; i++)
            {
                h = Mix(h, (ulong)Runners[i].S);
                h = Mix(h, (ulong)Runners[i].X);
                h = Mix(h, (ulong)Runners[i].Speed);
                h = Mix(h, (ulong)Runners[i].Stamina);
                h = Mix(h, (ulong)Runners[i].Exhausted);
                h = Mix(h, (ulong)Runners[i].StunTicks);
            }
            h = Mix(h, (ulong)Bull.S);
            h = Mix(h, (ulong)Bull.X);
            h = Mix(h, (ulong)Bull.Speed);
            h = Mix(h, (ulong)Bull.Phase);
            h = Mix(h, (ulong)Bull.Timer);
            h = Mix(h, (ulong)Bull.SlowTicks);
            return h;
        }

        private static ulong Mix(ulong h, ulong v)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (v >> (i * 8)) & 0xFF;
                    h *= 1099511628211UL;
                }
            }
            return h;
        }
    }
}
