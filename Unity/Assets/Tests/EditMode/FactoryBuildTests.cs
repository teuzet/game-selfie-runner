using System.IO;
using Factory.Editor;
using NUnit.Framework;
using SelfieRunner.Sim;

namespace SelfieRunner.Tests
{
    public class FactoryBuildTests
    {
        [Test]
        public void CopyData_PutsCoreJsonWhereLoaderLooksInBuild()
        {
            string outDir = Path.Combine(Path.GetTempPath(), "factory-build-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string target = FactoryBuildImpl.DataTarget(outDir, "Game");
                StringAssert.EndsWith(CoreDataLoader.BuildSubfolder, target.Replace('\\', '/'));
                FactoryBuildImpl.CopyData(CoreDataLoader.DataDir(), target);
                Assert.AreEqual(Directory.GetFiles(CoreDataLoader.DataDir(), "*.json").Length,
                                Directory.GetFiles(target, "*.json").Length);
                Assert.IsNotNull(CoreDataLoader.Load(target));
            }
            finally
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            }
        }
    }
}
