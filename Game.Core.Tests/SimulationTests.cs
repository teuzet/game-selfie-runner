using Game.Core;
using Xunit;

public class SimulationTests
{
    [Fact]
    public void SameInputsGiveSameHash()
    {
        var a = new Simulation();
        var b = new Simulation();
        for (int i = 0; i < 1000; i++) { a.Step(TickInputs.Empty); b.Step(TickInputs.Empty); }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(1000, a.Tick);
    }
}
