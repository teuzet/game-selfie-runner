using Game.Core.Bull;
using Game.Core.Economy;
using Game.Core.Events;
using Game.Core.Math;

namespace Game.Core.Race
{
    /// <summary>Фаза забега: отсчёт → забег → финиш (таймер после первого на арене) → итоги.</summary>
    public enum RacePhase { Countdown = 0, Running = 1, Finishing = 2, Over = 3 }

    /// <summary>Что считается по ходу забега для «лучших моментов» (по месту). Входит в RaceState и хэш.</summary>
    public struct HighlightTracker
    {
        public int HotRun, HotStart;       // текущий отрезок подряд в горячей зоне: тиков и тик начала
        public int FailActive, FailTick;   // идёт «нет сигнала» после подброса; тик подброса
        public long FailViewers;           // зрители в начале фейла
    }

    /// <summary>Фазы забега, финиш на арене и запись лучших моментов. Числа — Data/bull.json (countdownSec, finishSec). Без аллокаций.</summary>
    public static class Phases
    {
        const int Tps = Simulation.TicksPerSecond;

        public static int CountdownTicks(BullData d) => (d.CountdownSec * Fixed.FromInt(Tps)).ToInt();
        public static int FinishTicks(BullData d) => (d.FinishSec * Fixed.FromInt(Tps)).ToInt();

        public static bool IsFinished(RaceState st, int seat) => st.Viewing[seat].FinishTick >= 0;

        /// <summary>Начало блока финиш-арены (вход в него = финиш); нет арены — конец улицы.</summary>
        public static Fixed ArenaStart(Game.Core.Street.Street street)
        {
            for (int i = 0; i < street.Blocks.Length; i++)
                if (street.Blocks[i].Def.Id == "arena") return street.Blocks[i].Start;
            return street.Length;
        }

        /// <summary>Тик отсчёта: бегуны стоят, зрители не меняются. На «0» — Running и событие «старт».</summary>
        public static void StepCountdown(Simulation sim)
        {
            var st = sim.State;
            if (--st.PhaseTicks > 0) return;
            st.PhaseTicks = 0;
            st.Phase = RacePhase.Running;
            sim.Events.Add(RaceEventType.RaceStart, st.Tick, -1);
        }

        /// <summary>В конце Step забега: лучшие моменты, вход на арену, таймер финиша.</summary>
        public static void Step(Simulation sim)
        {
            var st = sim.State;
            var ev = sim.Events;
            int tick = st.Tick;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
                if (!IsFinished(st, i)) Record(st, i, tick, false);

            long arenaS = sim.ArenaStart.Raw;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                if (IsFinished(st, i) || st.Runners[i].S < arenaS) continue;
                Finish(st, i, tick);
                if (st.Phase == RacePhase.Running)
                {
                    st.Phase = RacePhase.Finishing;
                    st.PhaseTicks = FinishTicks(sim.BullData);
                    ev.Add(RaceEventType.FinishSoon, tick, i);
                }
            }
            if (st.Phase != RacePhase.Finishing) return;

            bool all = true;
            for (int i = 0; i < RaceConfig.SeatCount; i++) if (!IsFinished(st, i)) all = false;
            if (!all && --st.PhaseTicks > 0) return;
            // время вышло (или все уже на арене): остальные финишируют на месте с текущим счётом
            for (int i = 0; i < RaceConfig.SeatCount; i++)
                if (!IsFinished(st, i)) { Record(st, i, tick, false); Finish(st, i, tick); }
            st.PhaseTicks = 0;
            st.Phase = RacePhase.Over;
            ev.Add(RaceEventType.RaceFinish, tick, Results.Winner(st));
        }

        /// <summary>Место финишировало: зрители и донаты заморожены, открытые отрезки моментов закрыты.</summary>
        static void Finish(RaceState st, int seat, int tick)
        {
            Record(st, seat, tick, true);
            st.Viewing[seat].FinishTick = tick;
        }

        /// <summary>Обновляет лучшие моменты места за тик; final — закрыть открытые отрезки.</summary>
        static void Record(RaceState st, int seat, int tick, bool final)
        {
            ref var b = ref st.Best;
            ref var tr = ref st.Trackers[seat];
            ref var v = ref st.Viewing[seat];
            if (v.Viewers > b.MaxViewers.Value) b.MaxViewers = new Moment { Seat = seat, Tick = tick, Value = v.Viewers };

            bool hot = !final && v.Zone == ViewerZone.Hot;
            if (hot)
            {
                if (tr.HotRun == 0) tr.HotStart = tick;
                tr.HotRun++;
                if (tr.HotRun > b.LongestHot.Value) b.LongestHot = new Moment { Seat = seat, Tick = tr.HotStart, Value = tr.HotRun };
            }
            else tr.HotRun = 0;

            bool noSignal = !final && st.Tosses[seat].NoSignalTicks > 0;
            if (noSignal && tr.FailActive == 0) { tr.FailActive = 1; tr.FailTick = tick; tr.FailViewers = v.Viewers; }
            if (tr.FailActive != 0)
            {
                long cost = tr.FailViewers - v.Viewers;
                if (cost > b.CostliestFail.Value) b.CostliestFail = new Moment { Seat = seat, Tick = tr.FailTick, Value = cost };
                if (!noSignal) tr.FailActive = 0;
            }
        }
    }
}
