using System;
using System.Collections.Generic;
using Game.Core.Math;

namespace Game.Net
{
    public struct FakeNetworkSettings
    {
        /// <summary>Базовая задержка в тиках сети.</summary>
        public int DelayTicks;
        /// <summary>Джиттер: к задержке добавляется [0, JitterTicks].</summary>
        public int JitterTicks;
        /// <summary>Вероятность потери, 0..100 %.</summary>
        public int LossPercent;
        /// <summary>Вероятность дубля, 0..100 %.</summary>
        public int DuplicatePercent;
    }

    /// <summary>
    /// Фейковая сеть для тестов: узлы в одном процессе, время — ручное (<see cref="Advance"/>),
    /// потери/дубли/джиттер от <see cref="DetRandom"/> с сидом. Джиттер может переупорядочить пакеты.
    /// </summary>
    public sealed class FakeNetwork
    {
        private struct Item
        {
            public int From;
            public long Due;
            public long Seq;
            public byte[] Data;
        }

        private readonly List<Item>[] _inbox;
        private readonly FakeTransport[] _nodes;
        private DetRandom _rng;
        private long _seq;

        public FakeNetworkSettings Settings;
        public long Now { get; private set; }
        public int SentCount { get; private set; }
        public int DroppedCount { get; private set; }

        public FakeNetwork(int nodeCount, ulong seed, FakeNetworkSettings settings)
        {
            Settings = settings;
            _rng = new DetRandom(seed);
            _inbox = new List<Item>[nodeCount];
            _nodes = new FakeTransport[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                _inbox[i] = new List<Item>();
                _nodes[i] = new FakeTransport(this, i);
            }
        }

        public ITransport Node(int index) => _nodes[index];

        public void Advance(int ticks = 1) { Now += ticks; }

        internal void Enqueue(int from, int to, ReadOnlySpan<byte> data)
        {
            if (to < 0 || to >= _inbox.Length) return;
            SentCount++;
            if (_rng.Range(0, 100) < Settings.LossPercent) { DroppedCount++; return; }
            Put(from, to, data);
            if (_rng.Range(0, 100) < Settings.DuplicatePercent) Put(from, to, data);
        }

        private void Put(int from, int to, ReadOnlySpan<byte> data)
        {
            int jitter = Settings.JitterTicks > 0 ? _rng.Range(0, Settings.JitterTicks + 1) : 0;
            _inbox[to].Add(new Item
            {
                From = from,
                Due = Now + Settings.DelayTicks + jitter,
                Seq = _seq++,
                Data = data.ToArray()
            });
        }

        internal int Dequeue(int node, out int peer, Span<byte> buffer)
        {
            var list = _inbox[node];
            while (true)
            {
                int best = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Due > Now) continue;
                    if (best < 0 || list[i].Due < list[best].Due ||
                        (list[i].Due == list[best].Due && list[i].Seq < list[best].Seq)) best = i;
                }
                if (best < 0) { peer = -1; return 0; }
                var it = list[best];
                list.RemoveAt(best);
                if (it.Data.Length == 0 || it.Data.Length > buffer.Length) continue;
                it.Data.CopyTo(buffer);
                peer = it.From;
                return it.Data.Length;
            }
        }
    }

    public sealed class FakeTransport : ITransport
    {
        private readonly FakeNetwork _net;
        private readonly int _id;

        internal FakeTransport(FakeNetwork net, int id) { _net = net; _id = id; }

        public void Send(int peer, ReadOnlySpan<byte> data) => _net.Enqueue(_id, peer, data);

        public int Poll(out int peer, Span<byte> buffer) => _net.Dequeue(_id, out peer, buffer);
    }
}
