using System;
using Game.Core;
using Game.Net;
using Xunit;

namespace Game.Core.Tests
{
    public class RollbackTests
    {
        private const int Ticks = 3000;

        private static RaceConfig Cfg() => new RaceConfig(77, new[]
        {
            new SeatConfig(SeatKind.Human, 0), new SeatConfig(SeatKind.Human, 1),
            new SeatConfig(SeatKind.Human, 2), new SeatConfig(SeatKind.Human, 3),
        });

        private static PlayerInput InputOf(int seat, int tick)
        {
            uint x = (uint)(seat * 7919 + (tick / 5) * 104729) * 2654435761u;
            return new PlayerInput((int)((x >> 8) % 3) - 1, (int)((x >> 12) % 3) - 1, ((x >> 20) & 3) == 0);
        }

        private sealed class Result
        {
            public RollbackSession[] S = null!;
            public ulong[][] Hash = null!;
            public int[][] Calls = null!;
            public int NetTicks;
        }

        private static Result Run(bool lockstep, ulong netSeed, int loss = 5)
        {
            var net = new FakeNetwork(4, netSeed, new FakeNetworkSettings { DelayTicks = 2, JitterTicks = 3, LossPercent = loss });
            var r = new Result { S = new RollbackSession[4], Hash = new ulong[4][], Calls = new int[4][] };
            var cfg = Cfg();
            for (int i = 0; i < 4; i++)
            {
                int seat = i;
                r.Hash[i] = new ulong[Ticks + 64];
                r.Calls[i] = new int[Ticks + 64];
                r.S[i] = new RollbackSession(new Simulation(cfg), i, new[] { 0, 1, 2, 3 }, net.Node(i), lockstep);
                r.S[i].TickConfirmed += (t, sim) => { r.Calls[seat][t]++; r.Hash[seat][t] = sim.StateHash(); };
            }
            int guard = 0;
            while (true)
            {
                net.Advance();
                r.NetTicks++;
                int minConf = int.MaxValue;
                foreach (var s in r.S)
                {
                    s.Update();
                    if (s.Tick < Ticks + 40) s.LocalInput(s.Tick, InputOf(Array.IndexOf(r.S, s), s.Tick));
                    minConf = System.Math.Min(minConf, s.ConfirmedTick);
                }
                if (minConf >= Ticks) break;
                Assert.True(++guard < 30000, "дедлок? " + string.Join(",", System.Linq.Enumerable.Select(r.S, q => q.Tick + "/" + q.ConfirmedTick + "/" + q.StallCount)));
            }
            return r;
        }

        private static void AssertAgree(Result r)
        {
            for (int t = 0; t < Ticks; t++)
                for (int s = 1; s < 4; s++)
                {
                    Assert.Equal(1, r.Calls[s][t]);
                    Assert.Equal(r.Hash[0][t], r.Hash[s][t]);
                }
            foreach (var s in r.S) Assert.False(s.Desynced);
        }

        [Fact]
        public void Rollback_FourSessions_HashesAgree_RollbacksInWindow()
        {
            var r = Run(false, 5);
            AssertAgree(r);
            int total = 0;
            foreach (var s in r.S)
            {
                total += s.Rollbacks;
                Assert.True(s.MaxRollbackDepth <= RollbackSession.Window, $"глубина {s.MaxRollbackDepth}");
            }
            Assert.True(total > 0);
        }

        [Fact]
        public void Rollback_ConfirmedMatchesStraightRun()
        {
            var r = Run(false, 9);
            var sim = new Simulation(Cfg());
            for (int t = 0; t < Ticks; t++)
            {
                var ti = TickInputs.Empty;
                for (int s = 0; s < 4; s++) ti = ti.With(s, InputOf(s, t));
                sim.Step(ti);
                Assert.Equal(sim.StateHash(), r.Hash[0][t]);
            }
        }

        [Fact]
        public void Rollback_IsDeterministic()
        {
            var a = Run(false, 3);
            var b = Run(false, 3);
            Assert.Equal(a.NetTicks, b.NetTicks);
            Assert.Equal(a.S[2].Rollbacks, b.S[2].Rollbacks);
            Assert.Equal(a.Hash[1], b.Hash[1]);
        }

        [Fact]
        public void Lockstep_FourSessions_HashesAgree_NoRollbacks()
        {
            var r = Run(true, 5);
            AssertAgree(r);
            foreach (var s in r.S) Assert.Equal(0, s.Rollbacks);
        }

        [Fact]
        public void Rollback_PausesWhenWindowFull()
        {
            var net = new FakeNetwork(2, 1, new FakeNetworkSettings { DelayTicks = 0 });
            var cfg = new RaceConfig(1, new[]
            {
                new SeatConfig(SeatKind.Human, 0), new SeatConfig(SeatKind.Human, 1),
                new SeatConfig(SeatKind.Bot, 2, 1), new SeatConfig(SeatKind.Bot, 3, 1),
            });
            var a = new RollbackSession(new Simulation(cfg), 0, new[] { 1 }, net.Node(0));
            int ok = 0;
            for (int t = 0; t < 30; t++)
            {
                net.Advance();
                a.Update();
                if (a.LocalInput(a.Tick, default)) ok++;
            }
            Assert.Equal(RollbackSession.Window, ok);
            Assert.Equal(RollbackSession.Window, a.Tick);
        }
    }
}
