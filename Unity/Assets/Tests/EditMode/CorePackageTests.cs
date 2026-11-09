using NUnit.Framework;

namespace SelfieRunner.Tests
{
    public class CorePackageTests
    {
        [Test]
        public void GameCorePackageResolvesAndCompiles()
        {
            Assert.AreEqual("Game.Core", typeof(Game.Core.Simulation).Assembly.GetName().Name);
        }
    }
}
