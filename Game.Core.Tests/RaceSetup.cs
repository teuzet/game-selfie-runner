using Game.Core;
using Game.Core.Race;

/// <summary>Тестовая настройка: пропустить отсчёт (F-020), чтобы тесты движения, толчков и экономики стартовали сразу с забега.</summary>
public static class RaceSetup
{
    public static Simulation Running(this Simulation sim)
    {
        var st = sim.State;
        if (st.Phase != RacePhase.Countdown) return sim;
        // Таймер срыва быка не трогаем: тесты до F-020 откалиброваны на срыв через countdown+release от тика 0.
        st.Phase = RacePhase.Running;
        st.PhaseTicks = 0;
        return sim;
    }
}
