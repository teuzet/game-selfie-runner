namespace Game.Core
{
    /// <summary>Deterministic simulation skeleton. Unity never holds game logic; it drives this.</summary>
    public sealed class Simulation
    {
        public const int TicksPerSecond = 30;
        public int Tick { get; private set; }

        public void Step(TickInputs inputs)
        {
            Tick++;
        }

        /// <summary>Order-independent hash of the whole state; equal hashes mean equal states.</summary>
        public ulong StateHash()
        {
            unchecked { return 1469598103934665603UL ^ (ulong)Tick * 1099511628211UL; }
        }
    }

    public readonly struct TickInputs
    {
        public static readonly TickInputs Empty = default;
    }
}
