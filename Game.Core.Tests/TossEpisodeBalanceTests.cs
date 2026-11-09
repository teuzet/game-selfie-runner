using System;
using System.Text;
using Game.Core;
using Game.Core.Bull;
using Game.Core.Math;
using Game.Core.Race;
using Game.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Game.Core.Tests
{
    /// <summary>
    /// F-071, design/F-100.md → «Подброс закрывает эпизод» (3.1): «Бык держит дистанцию позади защищённого, а не паркуется на его `s` и не обходит:
    /// ближе 4 м к защищённому впереди бык не подходит»; «Охлаждение цели: 30 с от подброса бык не выбирает этого бегуна целью рывка»;
    /// «Финишировавшего и бегуна на пороге арены бык не подбрасывает». Гипотеза H6: подбросы на место за забег — медиана у каждого места 1–4, ни у кого > 5.
    /// Прогон 40 сидов ботов (RaceRecorder.MakeConfig); при падении печатается таблица.
    /// </summary>
    public class TossEpisodeBalanceTests
    {
        static readonly GameData Data = GameData.Load();
        readonly ITestOutputHelper _out;
        public TossEpisodeBalanceTests(ITestOutputHelper o) { _out = o; }

        const int Seeds = 40;
        const int MaxTicks = 20000;

        static readonly Lazy<(string table, string mechanics, string h6)> Result = new Lazy<(string, string, string)>(Simulate40);

        /// <summary>Механика эпизода (F-100 §3.1) на 40 сидах: защита, отступ, охлаждение, никто позади, финишировавший.</summary>
        [Fact]
        public void TossIsAnEpisode_40Seeds()
        {
            _out.WriteLine(Result.Value.table);
            Assert.True(Result.Value.mechanics.Length == 0, Result.Value.mechanics + "\n" + Result.Value.table);
        }

        /// <summary>
        /// Пороги гипотезы H6 (F-100 §2): ≤ 5 подбросов у места, медиана по местам 1–4, бык ≤ 25 м от медианы толпы ≥ 90% тиков.
        /// Пропущен: пороги достижимы только вместе с F-072 (рывок 2 с, доворот 1 м/с), F-073 (бот спринтует после подъёма — без этого
        /// подброшенный остаётся в 4–8 м впереди быка и держит его на крейсере) и F-075 (скорости персонажей по F-101). Таблица — в выводе теста.
        /// </summary>
        [Fact(Skip = "пороги H6 F-100 — гейт F-079 после F-072/F-075; при снятии Skip тест печатает таблицу")]
        public void H6Thresholds_40Seeds()
        {
            _out.WriteLine(Result.Value.table);
            Assert.True(Result.Value.h6.Length == 0, Result.Value.h6 + "\n" + Result.Value.table);
        }

        static (string, string, string) Simulate40()
        {
            var table = new StringBuilder();
            var failures = new StringBuilder();
            var h6 = new StringBuilder();
            var perPlace = new int[4][];
            for (int p = 0; p < 4; p++) perPlace[p] = new int[Seeds];
            int maxTosses = 0;
            long farTicks = 0, totalTicks = 0, rushTosses = 0, allTosses = 0;
            long gapEps = Fixed.FromFraction(1, 4).Raw; // расталкивание соседей может сдвинуть бегуна на доли метра
            long tossGap = Data.BullData.TossWaitGap.Raw;
            int cooldownTicks = Data.BullData.TossCooldownSec * Simulation.TicksPerSecond;
            int protectTicks = Data.BullData.ChargeTossGapSec * Simulation.TicksPerSecond;
            var order = new int[4];

            for (int k = 0; k < Seeds; k++)
            {
                ulong seed = (ulong)(k + 1);
                var sim = new Simulation(RaceRecorder.MakeConfig(seed, Data), Data);
                var st = sim.State;
                var prevTossTick = new int[4]; var prevTossed = new int[4];
                for (int i = 0; i < 4; i++) prevTossTick[i] = -1;
                int ticks = 0, prevCharges = 0;
                while (ticks < MaxTicks && st.Phase != RacePhase.Over)
                {
                    var finishedBefore = new bool[4];
                    for (int i = 0; i < 4; i++) finishedBefore[i] = st.Runners[i].Viewing.FinishTick >= 0;
                    sim.Step(TickInputs.Empty);
                    ticks++;
                    st = sim.State;
                    ref var b = ref st.Bull;
                    if (st.Charge.Count != prevCharges) // начат рывок: цель не раньше охлаждения от прошлого подброса
                    {
                        prevCharges = st.Charge.Count;
                        int tg = st.Charge.LastChargeTarget;
                        if (prevTossTick[tg] >= 0 && st.Tick - prevTossTick[tg] < cooldownTicks)
                            failures.AppendLine($"seed {seed} t{st.Tick}: рывок на место {tg} через {st.Tick - prevTossTick[tg]} тиков после подброса");
                    }
                    bool active = b.Phase == (int)BullPhase.Cruise || b.Phase == (int)BullPhase.Charge || b.Phase == (int)BullPhase.Slowed;
                    for (int i = 0; i < 4; i++)
                    {
                        ref var r = ref st.Runners[i];
                        if (r.Toss.Tossed != prevTossed[i])
                        {
                            allTosses++;
                            if (r.Toss.LastCause == TossCause.Charge) rushTosses++;
                            if (finishedBefore[i]) failures.AppendLine($"seed {seed} t{st.Tick}: подброшен финишировавший {i}");
                            if (r.Toss.LastTossFromS >= sim.ArenaStart.Raw - Data.BullData.ChargeTossDistance.Raw)
                                failures.AppendLine($"seed {seed} t{st.Tick}: подброшен {i} на пороге арены");
                            if (prevTossTick[i] >= 0)
                            {
                                int since = st.Tick - prevTossTick[i];
                                if (since < protectTicks) failures.AppendLine($"seed {seed} t{st.Tick}: место {i} подброшено повторно через {since} тиков");
                            }
                            prevTossTick[i] = st.Tick; prevTossed[i] = r.Toss.Tossed;
                        }
                        // бык не в точке лежащего и держит отступ позади защищённого лежащего/неуязвимого
                        if (active && BullCharge.Protected(r.Toss) && r.Viewing.FinishTick < 0)
                        {
                            long ds = r.S - b.S;
                            if (ds >= 0 && ds < tossGap - gapEps) failures.AppendLine($"seed {seed} t{st.Tick}: бык в {ds / (double)Fixed.OneRaw:F2} м от защищённого {i}");
                            if (ds < 0) failures.AppendLine($"seed {seed} t{st.Tick}: защищённый {i} позади быка");
                        }
                        // никто (не финишировавший) позади быка
                        if (active && r.Viewing.FinishTick < 0 && r.S < b.S && !BullCharge.Protected(r.Toss))
                            failures.AppendLine($"seed {seed} t{st.Tick}: бегун {i} позади быка");
                    }
                    if (active)
                    {
                        totalTicks++;
                        if (BullMovement.MedianS(st.Runners).Raw - b.S > Fixed.FromInt(25).Raw || b.S - BullMovement.MedianS(st.Runners).Raw > Fixed.FromInt(25).Raw) farTicks++;
                    }
                }
                Results.Standings(st, order);
                table.Append($"seed {seed,2}: ticks={ticks,5} tosses by place:");
                for (int p = 0; p < 4; p++)
                {
                    int n = st.Runners[order[p]].Toss.Tossed;
                    perPlace[p][k] = n; maxTosses = System.Math.Max(maxTosses, n);
                    table.Append($" {n,2}");
                }
                table.Append("  | by seat:");
                for (int i = 0; i < 4; i++) table.Append($" {st.Runners[i].Toss.Tossed,2}({sim.CharacterOf(i).Id})");
                table.AppendLine();
                if (st.Phase != RacePhase.Over) failures.AppendLine($"seed {seed}: забег не закончился за {MaxTicks} тиков");
            }

            double farShare = totalTicks == 0 ? 1 : farTicks / (double)totalTicks;
            table.AppendLine($"max tosses/seat={maxTosses}; bull>25m from median share={farShare:P1}; rush tosses={rushTosses}/{allTosses}");
            for (int p = 0; p < 4; p++)
            {
                var a = (int[])perPlace[p].Clone(); Array.Sort(a);
                double med = (a[Seeds / 2 - 1] + a[Seeds / 2]) / 2.0;
                table.AppendLine($"place {p + 1}: median tosses {med:F1}");
                if (med < 1 || med > 4) h6.AppendLine($"медиана подбросов места {p + 1} = {med}, нужно 1–4");
            }
            if (maxTosses > 5) h6.AppendLine($"у кого-то {maxTosses} подбросов, нужно ≤ 5");
            if (farShare > 0.10) h6.AppendLine($"бык дальше 25 м от медианы толпы {farShare:P1} времени, нужно ≤ 10%");
            return (table.ToString(), failures.ToString(), h6.ToString());
        }

        [Fact]
        public void FinishedRunnerIsNeverTossed_AndDeterministic()
        {
            var a = new Simulation(RaceRecorder.MakeConfig(7, Data), Data);
            var b = new Simulation(RaceRecorder.MakeConfig(7, Data), Data);
            Simulation? c = null;
            for (int t = 0; t < MaxTicks && a.State.Phase != RacePhase.Over; t++)
            {
                if (t == 3000) c = a.Clone();
                var fin = new bool[4]; var tossed = new int[4];
                for (int i = 0; i < 4; i++) { fin[i] = a.State.Runners[i].Viewing.FinishTick >= 0; tossed[i] = a.State.Runners[i].Toss.Tossed; }
                a.Step(TickInputs.Empty); b.Step(TickInputs.Empty); c?.Step(TickInputs.Empty);
                for (int i = 0; i < 4; i++) if (fin[i]) Assert.Equal(tossed[i], a.State.Runners[i].Toss.Tossed);
                Assert.Equal(a.StateHash(), b.StateHash());
                if (c != null) Assert.Equal(a.StateHash(), c.StateHash());
            }
            Assert.Equal(RacePhase.Over, a.State.Phase);
        }
    }
}
