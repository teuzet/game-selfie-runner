using Game.Core;
using UnityEngine;

namespace SelfieRunner.Sim
{
    /// <summary>Собирает драйвер и HUD из кода: место 0 — человек, остальные — боты.</summary>
    public sealed class RaceBootstrap : MonoBehaviour
    {
        public ulong Seed = 1;

        private void Awake()
        {
            var seats = new[]
            {
                new SeatConfig(SeatKind.Human, 0),
                new SeatConfig(SeatKind.Bot, 0, 0),
                new SeatConfig(SeatKind.Bot, 0, 1),
                new SeatConfig(SeatKind.Bot, 0, 2),
            };
            var driver = gameObject.AddComponent<SimulationDriver>();
            driver.Init(new RaceConfig(Seed, seats), CoreDataLoader.Load(), new KeyboardInputSource());
            gameObject.AddComponent<RaceHud>().Driver = driver;
        }
    }
}
