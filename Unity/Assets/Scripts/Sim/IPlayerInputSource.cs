using Game.Core;

namespace SelfieRunner.Sim
{
    public interface IPlayerInputSource
    {
        PlayerInput Read();
    }
}
