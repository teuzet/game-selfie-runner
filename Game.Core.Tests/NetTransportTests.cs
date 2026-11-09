using System;
using Game.Core;
using Game.Net;
using Xunit;

namespace Game.Core.Tests
{
    public class NetTransportTests
    {
        private static PlayerInput In(int t) => new PlayerInput(((t % 3) + 3) % 3 - 1, ((t / 3) % 3 + 3) % 3 - 1, t % 5 == 0);

        [Fact]
        public void InputPacket_RoundTrip_AndSize()
        {
            var p = new InputPacket { Tick = 123456, Seat = 3 };
            for (int i = 0; i < 8; i++) p.SetBack(i, In(i + 7));
            var buf = new byte[32];
            int n = p.Write(buf);
            Assert.True(n <= 24);
            Assert.True(InputPacket.TryRead(buf.AsSpan(0, n), out var q));
            Assert.Equal(p.Tick, q.Tick);
            Assert.Equal(p.Seat, q.Seat);
            for (int i = 0; i < 8; i++) Assert.Equal(p.GetBack(i), q.GetBack(i));
            Assert.False(InputPacket.TryRead(buf.AsSpan(0, n - 1), out _));
        }

        [Fact]
        public void HashPacket_RoundTrip()
        {
            var p = new HashPacket { Tick = 90, Hash = 0xDEADBEEF12345678UL };
            var buf = new byte[16];
            int n = p.Write(buf);
            Assert.True(HashPacket.TryRead(buf.AsSpan(0, n), out var q));
            Assert.Equal(p.Tick, q.Tick);
            Assert.Equal(p.Hash, q.Hash);
            Assert.False(InputPacket.TryRead(buf.AsSpan(0, n), out _));
        }

        [Fact]
        public void FakeTransport_DelayAndPeer()
        {
            var net = new FakeNetwork(2, 1, new FakeNetworkSettings { DelayTicks = 3 });
            net.Node(0).Send(1, new byte[] { 7, 8 });
            var buf = new byte[8];
            Assert.Equal(0, net.Node(1).Poll(out _, buf));
            net.Advance(3);
            Assert.Equal(2, net.Node(1).Poll(out int peer, buf));
            Assert.Equal(0, peer);
            Assert.Equal(7, buf[0]);
            Assert.Equal(0, net.Node(1).Poll(out _, buf));
        }

        private static (int lost, int delivered, bool[] have) Run(ulong seed, int redundancy)
        {
            const int Ticks = 600;
            var net = new FakeNetwork(2, seed, new FakeNetworkSettings
            { DelayTicks = 2, JitterTicks = 3, LossPercent = 20, DuplicatePercent = 10 });
            var tx = net.Node(0);
            var rx = net.Node(1);
            var have = new bool[Ticks];
            var buf = new byte[64];
            var w = new byte[InputPacket.Size];
            int delivered = 0;
            for (int t = 0; t < Ticks + 10; t++)
            {
                if (t < Ticks)
                {
                    var p = new InputPacket { Tick = t, Seat = 1 };
                    for (int b = 0; b < redundancy; b++) p.SetBack(b, In(t - b));
                    int n = p.Write(w);
                    tx.Send(1, w.AsSpan(0, n));
                }
                net.Advance();
                int len;
                while ((len = rx.Poll(out _, buf)) > 0)
                {
                    delivered++;
                    Assert.True(InputPacket.TryRead(buf.AsSpan(0, len), out var q));
                    for (int b = 0; b < redundancy; b++)
                    {
                        int tt = q.Tick - b;
                        if (tt < 0 || tt >= Ticks) continue;
                        Assert.True(q.TryGet(tt, out var inp));
                        Assert.Equal(In(tt), inp);
                        have[tt] = true;
                    }
                }
            }
            int lost = 0;
            foreach (var h in have) if (!h) lost++;
            return (lost, delivered, have);
        }

        [Fact]
        public void Redundancy_RecoversInputsUnder20PercentLoss()
        {
            var single = Run(42, 1);
            Assert.True(single.lost > 0, "без избыточности потери должны быть видны");
            var full = Run(42, 8);
            Assert.Equal(0, full.lost);
        }

        [Fact]
        public void FakeNetwork_IsDeterministic()
        {
            var a = Run(7, 3);
            var b = Run(7, 3);
            Assert.Equal(a.delivered, b.delivered);
            Assert.Equal(a.have, b.have);
        }
    }
}
