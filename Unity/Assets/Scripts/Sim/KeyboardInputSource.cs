using Game.Core;
using UnityEngine;

namespace SelfieRunner.Sim
{
    /// <summary>A/D — руль, W/S — темп, Space — действие (Input Manager).</summary>
    public sealed class KeyboardInputSource : IPlayerInputSource
    {
        public PlayerInput Read()
        {
            int steer = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            int pace = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            return new PlayerInput(steer, pace, Input.GetKey(KeyCode.Space));
        }
    }
}
