using Game.Core.Data;
using Game.Core.Math;

namespace Game.Core.Hints
{
    /// <summary>Data/hints.json: дальности источника, опережение и доли шума подсказок чата (design/overview.md → «Подсказки чата»).</summary>
    public sealed class HintData
    {
        public Fixed NearMin, NearMax, LeadSec, WrongSideChance, DropChance;
        public int Thirds, LeadTicks;

        public const string FileName = "hints.json";

        public static HintData Parse(string json)
        {
            var j = Json.Parse(FileName, json);
            var d = new HintData
            {
                NearMin = j.Num("nearMin"), NearMax = j.Num("nearMax"), LeadSec = j.Num("leadSec"),
                WrongSideChance = j.Num("wrongSideChance"), DropChance = j.Num("dropChance"), Thirds = j.Int("thirds"),
            };
            d.LeadTicks = (d.LeadSec * Fixed.FromInt(Simulation.TicksPerSecond)).ToInt();
            return d;
        }
    }
}
