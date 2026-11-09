using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Net
{
    /// <summary>
    /// Rollback-сессия одного игрока. Свой вход применяется сразу, чужие предсказываются повтором последнего;
    /// при приходе настоящих входов подтверждённая копия идёт вперёд, а живое состояние откатывается к ней
    /// и пересчитывается. Окно — 8 тиков, дальше <see cref="LocalInput"/> возвращает false (пауза ожидания).
    /// Режим lockstep (запасной путь): без предсказания, вход, поданный на тике t, действует на t+inputDelay,
    /// симуляция идёт, только когда известны входы всех мест.
    /// Номер peer в транспорте равен номеру места. Места ботов и места вне peers считаются нейтральными.
    /// Кадр: Update(); затем LocalInput(Tick, input).
    /// </summary>
    public sealed class RollbackSession
    {
        public const int Window = 8;
        public const int HashInterval = 30;

        private readonly Simulation _sim;
        private readonly Simulation _conf;      // подтверждённое состояние (в lockstep — та же _sim)
        private readonly int _local;
        private readonly bool _lockstep;
        private readonly int _delay;
        private readonly ITransport _net;
        private readonly bool[] _remote = new bool[RaceConfig.SeatCount];
        private readonly InputBuffer[] _buf = new InputBuffer[RaceConfig.SeatCount];
        private readonly TickInputs[] _used = new TickInputs[InputBuffer.Capacity];
        private readonly byte[] _packet = new byte[32];
        private readonly byte[] _rx = new byte[64];

        private bool _needRollback;
        private bool _sentSinceUpdate;
        private int _latestLocal = -1;
        private readonly Dictionary<int, ulong> _myHash = new Dictionary<int, ulong>();
        private readonly Dictionary<int, ulong>[] _peerHash = new Dictionary<int, ulong>[RaceConfig.SeatCount];
        private int _lastHashTick = -1;

        /// <summary>Живое (предсказанное) состояние для рендера.</summary>
        public Simulation Sim => _sim;
        public Simulation Confirmed => _conf;
        public int Tick => _sim.Tick;
        /// <summary>Число тиков, пройденных с настоящими входами всех мест.</summary>
        public int ConfirmedTick => _conf.Tick;
        public bool IsLockstep => _lockstep;
        public int Rollbacks { get; private set; }
        public int MaxRollbackDepth { get; private set; }
        public long ResimulatedTicks { get; private set; }
        public int StallCount { get; private set; }
        public bool Desynced { get; private set; }
        public int DesyncTick { get; private set; } = -1;

        /// <summary>
        /// Вызывается ровно один раз на каждый подтверждённый тик, по порядку: (номер тика, подтверждённая симуляция
        /// сразу после его шага). Отсюда Unity берёт события тика; события откаченных тиков сюда не попадают.
        /// </summary>
        public event Action<int, Simulation>? TickConfirmed;

        public RollbackSession(Simulation sim, int localSeat, int[] peers, ITransport transport,
            bool lockstep = false, int inputDelay = 3)
        {
            _sim = sim;
            _local = localSeat;
            _net = transport;
            _lockstep = lockstep;
            _delay = lockstep ? inputDelay : 0;
            if (_delay < 0 || _delay > 4) throw new ArgumentOutOfRangeException(nameof(inputDelay));
            foreach (int p in peers)
                if (p != localSeat && p >= 0 && p < RaceConfig.SeatCount && sim.Config.GetSeat(p).Kind == SeatKind.Human)
                    _remote[p] = true;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                _buf[i] = new InputBuffer();
                _peerHash[i] = new Dictionary<int, ulong>();
            }
            _conf = lockstep ? sim : sim.Clone();
            // Первые inputDelay тиков в lockstep — нейтральные у всех.
            for (int t = 0; t < _delay; t++)
                for (int s = 0; s < RaceConfig.SeatCount; s++) _buf[s].Set(t, default);
            if (lockstep) _latestLocal = _delay - 1;
        }

        /// <summary>
        /// Локальный вход на текущий тик. Rollback: сразу шагает живое состояние; false — окно заполнено (ждём).
        /// Lockstep: tick не используется; вход становится следующим по порядку (на тик Tick+delay в steady state),
        /// false — вход опережает симуляцию на delay тиков, ждём.
        /// </summary>
        public bool LocalInput(int tick, PlayerInput input)
        {
            Poll();
            if (_lockstep)
            {
                // Каждый вызов даёт следующий по порядку вход; опережать симуляцию можно не больше чем на delay тиков.
                int at = _latestLocal + 1;
                if (at > _sim.Tick + _delay) { Advance(); return false; }
                _buf[_local].Set(at, input);
                _latestLocal = at;
                SendInputs();
                Advance();
                return true;
            }

            if (tick != _sim.Tick || _sim.Tick - _conf.Tick >= Window) { StallCount++; return false; }
            _buf[_local].Set(tick, input);
            _latestLocal = tick;
            var ti = Compose(tick, _conf.Tick);
            _used[tick & (InputBuffer.Capacity - 1)] = ti;
            _sim.Step(ti);
            SendInputs();
            Advance();
            return true;
        }

        /// <summary>Принимает пакеты, двигает подтверждённое состояние, откатывает и пересчитывает при нужде, шлёт повтор.</summary>
        public void Update()
        {
            Poll();
            Advance();
            if (!_sentSinceUpdate && _latestLocal >= 0) SendInputs();
            _sentSinceUpdate = false;
            SendHash();
        }

        private TickInputs Compose(int tick, int floor)
        {
            var ti = TickInputs.Empty;
            for (int s = 0; s < RaceConfig.SeatCount; s++)
                ti = ti.With(s, _buf[s].Predict(tick, floor));
            return ti;
        }

        private bool AllKnown(int tick)
        {
            bool all = true;
            for (int s = 0; s < RaceConfig.SeatCount; s++)
            {
                if (s == _local || _remote[s]) { if (!_buf[s].Has(tick)) all = false; }
                else _buf[s].Set(tick, default);
            }
            return all;
        }

        private void Advance()
        {
            while ((_lockstep || _conf.Tick < _sim.Tick) && AllKnown(_conf.Tick))
            {
                int t = _conf.Tick;
                var ti = TickInputs.Empty;
                for (int s = 0; s < RaceConfig.SeatCount; s++)
                {
                    var inp = _buf[s].Get(t);
                    ti = ti.With(s, inp);
                    _buf[s].LastConfirmed = inp;
                }
                if (!_lockstep && !_used[t & (InputBuffer.Capacity - 1)].Equals(ti)) _needRollback = true;
                _conf.Step(ti);
                OnConfirmed(t);
            }

            if (_needRollback && !_lockstep)
            {
                _needRollback = false;
                int depth = _sim.Tick - _conf.Tick;
                int target = _sim.Tick;
                _sim.CopyFrom(_conf);
                for (int t = _conf.Tick; t < target; t++)
                {
                    var ti = Compose(t, _conf.Tick);
                    _used[t & (InputBuffer.Capacity - 1)] = ti;
                    _sim.Step(ti);
                }
                Rollbacks++;
                ResimulatedTicks += depth;
                if (depth > MaxRollbackDepth) MaxRollbackDepth = depth;
            }
        }

        private void OnConfirmed(int tick)
        {
            TickConfirmed?.Invoke(tick, _conf);
            if (_conf.Tick % HashInterval == 0)
            {
                int k = _conf.Tick / HashInterval;
                _myHash[k] = _conf.StateHash();
                _lastHashTick = _conf.Tick;
                for (int s = 0; s < RaceConfig.SeatCount; s++) CompareHash(s, k);
            }
        }

        private void CompareHash(int seat, int k)
        {
            if (Desynced) return;
            if (_myHash.TryGetValue(k, out ulong mine) && _peerHash[seat].TryGetValue(k, out ulong theirs) && mine != theirs)
            {
                Desynced = true;
                DesyncTick = k * HashInterval;
            }
        }

        private void Poll()
        {
            while (true)
            {
                int n = _net.Poll(out int peer, _rx);
                if (n <= 0) break;
                var span = new ReadOnlySpan<byte>(_rx, 0, n);
                if (InputPacket.TryRead(span, out var ip))
                {
                    if (!_remote[ip.Seat]) continue;
                    for (int back = 0; back < InputPacket.History; back++)
                    {
                        int t = ip.Tick - back;
                        if (t < _conf.Tick || t >= _conf.Tick + InputBuffer.Capacity / 2) continue;
                        _buf[ip.Seat].Set(t, ip.GetBack(back));
                    }
                }
                else if (HashPacket.TryRead(span, out var hp))
                {
                    if (peer < 0 || peer >= RaceConfig.SeatCount || !_remote[peer] || hp.Tick % HashInterval != 0) continue;
                    int k = hp.Tick / HashInterval;
                    _peerHash[peer][k] = hp.Hash;
                    CompareHash(peer, k);
                }
            }
        }

        private void SendInputs()
        {
            _sentSinceUpdate = true;
            int tick = _latestLocal;
            // Два пакета: последний и на окно раньше — покрывают отставание получателя до 16 тиков.
            SendWindow(tick);
            if (tick - InputPacket.History >= 0) SendWindow(tick - InputPacket.History);
        }

        private void SendWindow(int tick)
        {
            var p = new InputPacket { Tick = tick, Seat = _local };
            for (int back = 0; back < InputPacket.History; back++)
            {
                int t = tick - back;
                p.SetBack(back, _buf[_local].Has(t) ? _buf[_local].Get(t) : default);
            }
            int n = p.Write(_packet);
            for (int s = 0; s < RaceConfig.SeatCount; s++)
                if (_remote[s]) _net.Send(s, new ReadOnlySpan<byte>(_packet, 0, n));
        }

        private void SendHash()
        {
            if (_lastHashTick < 0) return;
            var p = new HashPacket { Tick = _lastHashTick, Hash = _myHash[_lastHashTick / HashInterval] };
            int n = p.Write(_packet);
            for (int s = 0; s < RaceConfig.SeatCount; s++)
                if (_remote[s]) _net.Send(s, new ReadOnlySpan<byte>(_packet, 0, n));
        }
    }
}
