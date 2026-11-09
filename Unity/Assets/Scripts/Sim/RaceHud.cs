using Game.Core;
using Game.Core.Math;
using UnityEngine;

namespace SelfieRunner.Sim
{
    /// <summary>Текстовый HUD своего бегуна: тик, s, x, скорость, темп.</summary>
    public sealed class RaceHud : MonoBehaviour
    {
        public SimulationDriver Driver;

        public static string PaceName(PlayerInput input, int exhausted)
        {
            if (input.Pace < 0) return "jog";
            if (input.Pace > 0 && exhausted == 0) return "sprint";
            return "run";
        }

        public string BuildText()
        {
            if (Driver == null || Driver.Sim == null) return "";
            int seat = Driver.PlayerSeat;
            if (seat < 0) return "tick " + Driver.Sim.State.Tick;
            var r = Driver.Sim.State.Runners[seat];
            var last = Driver.LastInputs.Get(seat);
            return string.Format("tick {0}\ns {1:F1} m\nx {2:F2} m\nspeed {3:F1} m/s\npace {4}",
                Driver.Sim.State.Tick, (double)Fixed.FromRaw(r.S), (double)Fixed.FromRaw(r.X),
                (double)Fixed.FromRaw(r.Speed), PaceName(last, r.Exhausted));
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 300, 120), BuildText());
        }
    }
}
