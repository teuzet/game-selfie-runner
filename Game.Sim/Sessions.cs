using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Bull;
using Game.Core.Economy;
using Game.Core.Events;
using Game.Core.Math;
using Game.Core.Race;
using Game.Core.Runners;

namespace Game.Sim
{
    /// <summary>
    /// Служебные архетипы гейта ботов (F-082): живут в Game.Sim, не в ядре и не в bots.json. Место 0 типа Human ведёт этим входом
    /// через TickInputs, остальные три места — боты как обычно. <c>idle</c> — пустой вход; <c>random</c> — новое случайное значение раз в 0.5 с.
    /// </summary>
    public sealed class ServiceInput
    {
        public const string Idle = "idle", Random = "random";
        public static readonly string[] All = { Idle, Random };

        const int ChangeEveryTicks = Simulation.TicksPerSecond / 2; // F-082: «новое значение раз в 0.5 с»
        const int ActionOneIn = 6;                                  // шанс кнопки действия на значение: 1 из 6 (выбор исполнителя, решение F-082)

        readonly bool _random;
        DetRandom _rng;
        int _steer, _pace;

        public ServiceInput(string archetype, ulong seed)
        {
            _random = archetype == Random;
            _rng = new DetRandom(seed ^ 0x5E1F5E1F5E1FUL, 91);
        }

        /// <summary>Вход места на тик; действие — нажатие на один тик первого тика значения.</summary>
        public PlayerInput Next(int tick)
        {
            if (!_random) return default;
            bool fresh = tick % ChangeEveryTicks == 0;
            bool action = false;
            if (fresh)
            {
                _steer = _rng.Range(-1, 2); _pace = _rng.Range(-1, 2);
                action = _rng.Range(0, ActionOneIn) == 0;
            }
            return new PlayerInput(_steer, _pace, action);
        }
    }

    /// <summary>
    /// F-082: строки sessions.jsonl одного забега для карты темпа и гейта ботов тула sim. События берутся из RaceEvents и состояния
    /// после каждого Step, спред решения — форком: Clone состояния тика решения, альтернатива проигрывается до 3 с теми же ботами.
    /// Ничего не меняет в основном прогоне: форки идут на отдельных Simulation.
    /// </summary>
    public sealed class SessionTracker
    {
        // Форки: acceptance F-082, «spread решения — по форку»; выборка «каждое k-е решение» нужна, чтобы уложиться в 5 минут на 40+80 забегов.
        const int ForkTicks = 3 * Simulation.TicksPerSecond;      // альтернатива проигрывается ≤ 3 с
        const int SteerWindow = Simulation.TicksPerSecond;        // альтернативный руль/без действия — первая секунда, дальше как обычно
        // Выборки нет: форк на каждое решение укладывается в бюджет (замер — в notes), поэтому spread есть у всех решений.

        // Угроза: бык ≤ 8 м — 0.7..1.0, 8–20 м — 0.7..0.3, дальше не пишется (acceptance F-082).
        const int NearCm = 800, FarCm = 2000;

        const int KindPace = 0, KindSteer = 1, KindAction = 2;

        sealed class Ev
        {
            public int Tick;
            public int Threat = -1, Reward = -1, Options, Spread = -1;
            public string? Novel;
        }

        readonly Simulation _sim, _prev;
        readonly GameData _data;
        readonly RaceConfig _cfg;
        readonly int[] _seats;
        readonly string[] _archetype;
        readonly List<Ev>[] _ev;
        readonly PlayerInput[] _prevIn;
        readonly IBotBrain?[] _brains;
        readonly Simulation?[] _shadowA, _shadowB;

        /// <param name="seats">Отслеживаемые места; archetype[i] — архетип i-го из них.</param>
        public SessionTracker(Simulation sim, GameData data, RaceConfig cfg, int[] seats, string[] archetype)
        {
            _sim = sim; _data = data; _cfg = cfg; _seats = seats; _archetype = archetype;
            _prev = new Simulation(cfg, data, sim.Street);
            _ev = new List<Ev>[seats.Length];
            for (int i = 0; i < seats.Length; i++) _ev[i] = new List<Ev>();
            _prevIn = new PlayerInput[RaceConfig.SeatCount];
            _brains = BotBrains.CreateAll(data.BotData, cfg);
            _shadowA = new Simulation?[RaceConfig.SeatCount];
            _shadowB = new Simulation?[RaceConfig.SeatCount];
        }

        /// <summary>Перед Step: снимок состояния тика (из него стартуют форки).</summary>
        public void BeforeStep() => _prev.CopyFrom(_sim);

        /// <summary>После Step: события и решения тика. <paramref name="stepTick"/> — тик, который только что шагнул.</summary>
        public void AfterStep(int stepTick, int startTick, TickInputs inputs)
        {
            var st = _sim.State;
            var events = _sim.Events;
            for (int i = 0; i < _seats.Length; i++)
            {
                int seat = _seats[i];
                bool human = _cfg.GetSeat(seat).Kind == SeatKind.Human;
                var cur = inputs.Get(seat);
                if (startTick < 0 || stepTick < startTick) { _prevIn[seat] = cur; continue; }

                for (int e = 0; e < events.Count; e++)
                {
                    var ev = events[e];
                    if (ev.Seat != seat) continue;
                    Tag(i, ev);
                }

                // решения: смена темпа, руль по подсказке (бот: новый план уворота), действие
                bool pace, steer = false, action;
                if (human)
                {
                    pace = cur.Pace != _prevIn[seat].Pace;
                    action = cur.Action && !_prevIn[seat].Action;
                }
                else
                {
                    ref var was = ref _prev.State.Bots[seat];
                    ref var now = ref st.Bots[seat];
                    pace = now.Pace != was.Pace;
                    steer = NewPlan(was.P0, now.P0) || NewPlan(was.P1, now.P1);
                    action = false;
                    for (int e = 0; e < events.Count; e++)
                        if (events[e].Seat == seat && (events[e].Type == RaceEventType.Shoved || events[e].Type == RaceEventType.Tripped)) action = true;
                }
                _prevIn[seat] = cur;
                if (pace) Decide(i, seat, stepTick, KindPace, 3, cur);
                if (steer) Decide(i, seat, stepTick, KindSteer, 3, cur);
                if (action) Decide(i, seat, stepTick, KindAction, 2, cur);
            }
        }

        static bool NewPlan(in BotPlan was, in BotPlan now) => now.Used != 0 && (was.Used == 0 || was.ReactAt != now.ReactAt);

        void Tag(int i, RaceEvent ev)
        {
            var d = _sim.EconomyData;
            switch (ev.Type)
            {
                case RaceEventType.ZoneChanged:
                {
                    int th = ThreatCenti(ev.Seat);
                    if (th >= 0) Add(i, ev.Tick).Threat = th;
                    break;
                }
                case RaceEventType.BullCharge:
                    { var x = Add(i, ev.Tick); x.Threat = 100; x.Novel = "bull-charge"; break; }
                case RaceEventType.Collision:
                    {
                        var x = Add(i, ev.Tick);
                        x.Reward = d.CollisionViewers + (ev.Param1 == (int)CollisionKind.Water ? d.WaterDonations : 0);
                        x.Novel = "collision:" + ((CollisionKind)ev.Param1).ToString().ToLowerInvariant();
                        break;
                    }
                case RaceEventType.Tossed:
                    { var x = Add(i, ev.Tick); x.Reward = d.TossViewers + d.TossDonations; x.Novel = "tossed"; break; }
                case RaceEventType.Shoved:
                    { var x = Add(i, ev.Tick); x.Reward = d.ShoveViewers; x.Novel = "shoved"; break; }
                case RaceEventType.Tripped:
                    { var x = Add(i, ev.Tick); x.Reward = d.TripViewers; x.Novel = "tripped"; break; }
                case RaceEventType.GotShoved: Add(i, ev.Tick).Novel = "got-shoved"; break; // зрители за него отрицательные, награды нет
                case RaceEventType.GotTripped: Add(i, ev.Tick).Novel = "got-tripped"; break;
                case RaceEventType.FramedIn:
                    { var x = Add(i, ev.Tick); x.Reward = System.Math.Max(1, Fixed.FromRaw(ev.Param1).ToInt()); x.Novel = "framed-in"; break; }
                case RaceEventType.ViewerThreshold: Add(i, ev.Tick).Reward = ev.Param1; break; // рекорд зрителей: размер — достигнутый порог
            }
        }

        Ev Add(int i, int tick)
        {
            var x = new Ev { Tick = tick };
            _ev[i].Add(x);
            return x;
        }

        /// <summary>Угроза места по расстоянию до быка, сотые доли (70..100 при ≤ 8 м, 70..30 при 8–20 м); −1 — дальше, не пишем.</summary>
        int ThreatCenti(int seat)
        {
            var st = _sim.State;
            var fs = Fixed.FromRaw(st.Runners[seat].S - st.Bull.S);
            var fx = Fixed.FromRaw(st.Runners[seat].X - st.Bull.X);
            long cm = Fixed.Sqrt(fs * fs + fx * fx).Raw * 100 / Fixed.OneRaw;
            if (cm <= NearCm) return (int)(70 + 30 * (NearCm - cm) / NearCm);
            if (cm <= FarCm) return (int)(70 - 40 * (cm - NearCm) / (FarCm - NearCm));
            return -1;
        }

        void Decide(int i, int seat, int tick, int kind, int options, PlayerInput cur)
        {
            var x = Add(i, tick);
            x.Options = options;
            x.Spread = SpreadCenti(seat, kind, cur);
        }

        // ---------- форк ----------

        /// <summary>
        /// Спред решения: |Δ(донаты + зрители/100)| между вариантами / (донаты участника + 100), сотые доли, 0..100.
        /// Вариант A — как играл бы участник (мозг бота или удержание входа), B — то же, но с другим темпом / рулём / без действия.
        /// </summary>
        int SpreadCenti(int seat, int kind, PlayerInput cur)
        {
            var a = Shadow(_shadowA, seat);
            var b = Shadow(_shadowB, seat);
            int altPace = 0, altSteer = 0;
            long delta = System.Math.Abs(Play(a, seat, kind, false, cur, ref altPace, ref altSteer) - Play(b, seat, kind, true, cur, ref altPace, ref altSteer));
            long don = Viewers.DonationsOf(_prev.State, seat);
            long spread = delta / (don + 100);
            return (int)(spread > 100 ? 100 : spread);
        }

        /// <summary>Тот же забег, но место ведёт Game.Sim (Human): иначе вход бота подменить нельзя. Улица и данные общие.</summary>
        Simulation Shadow(Simulation?[] pool, int seat)
        {
            var s = pool[seat];
            if (s != null) return s;
            var sc = _cfg.GetSeat(seat);
            var seats = new SeatConfig[RaceConfig.SeatCount];
            for (int k = 0; k < seats.Length; k++) seats[k] = _cfg.GetSeat(k);
            seats[seat] = new SeatConfig(SeatKind.Human, sc.CharacterId, sc.BotTemperament);
            s = new Simulation(new RaceConfig(_cfg.Seed, seats), _data, _sim.Street) { BotHints = _sim.BotHints };
            pool[seat] = s;
            return s;
        }

        /// <summary>Играет ForkTicks тиков из снимка тика решения; результат — донаты×100 + зрители места.</summary>
        long Play(Simulation sh, int seat, int kind, bool alt, PlayerInput cur, ref int altPace, ref int altSteer)
        {
            sh.CopyFrom(_prev);
            var brain = _brains[seat];
            for (int k = 0; k < ForkTicks && sh.State.Phase != RacePhase.Over; k++)
            {
                PlayerInput inp = brain != null
                    ? BotBrains.Think(brain, sh.State, sh.Street, _data.BotData, seat, sh.BotHints)
                    : new PlayerInput(cur.Steer, cur.Pace, k == 0 && cur.Action);
                if (k == 0)
                {
                    altPace = inp.Pace == 0 ? 1 : 0;
                    altSteer = inp.Steer != 0 ? -inp.Steer : (sh.State.Runners[seat].X > 0 ? -1 : 1);
                }
                if (alt)
                {
                    if (kind == KindPace) inp = new PlayerInput(inp.Steer, altPace, inp.Action);
                    else if (kind == KindSteer && k < SteerWindow) inp = new PlayerInput(altSteer, inp.Pace, inp.Action);
                    else if (kind == KindAction && k < SteerWindow) inp = new PlayerInput(inp.Steer, inp.Pace, false);
                }
                sh.Step(TickInputs.Empty.With(seat, inp));
            }
            return (long)Viewers.DonationsOf(sh.State, seat) * 100 + Viewers.ViewersOf(sh.State, seat);
        }

        // ---------- итог ----------

        /// <summary>Строки jsonl отслеживаемых мест; <paramref name="order"/> — места от победителя (Results.Standings).</summary>
        public List<string> Finish(RunRecord rec, int startTick, int[] order)
        {
            var lines = new List<string>(_seats.Length);
            for (int i = 0; i < _seats.Length; i++)
                lines.Add(Line(i, rec, startTick, order));
            return lines;
        }

        string Line(int i, RunRecord rec, int startTick, int[] order)
        {
            int seat = _seats[i];
            int place = 0;
            for (int p = 0; p < order.Length; p++) if (order[p] == seat) place = p;
            var seats = rec.Seats;
            int winnerDon = seats[order[0]].Donations;
            bool tiedTop = seats[order[0]].Donations == seats[order[1]].Donations;
            string outcome = place == 0 ? (tiedTop ? "draw" : "win") : (place == 1 && tiedTop ? "draw" : "loss");
            // соседи по таблице: победитель — со вторым, остальные — с тем, кто выше
            int neighbor = place == 0 ? order[1] : order[place - 1];
            long gap = System.Math.Abs(seats[seat].Donations - seats[neighbor].Donations);
            long margin = winnerDon <= 0 ? 0 : System.Math.Min(100, gap * 100 / winnerDon);

            var sb = new StringBuilder();
            sb.Append("{\"seed\":").Append(rec.Seed.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"archetype\":\"").Append(_archetype[i]).Append('"');
            sb.Append(",\"outcome\":\"").Append(outcome).Append('"');
            sb.Append(",\"duration_s\":").Append(Fmt.Tenths(Fmt.TicksToTenths(rec.DurationTicks)));
            sb.Append(",\"margin\":").Append(Fmt.Hundredths(margin));
            sb.Append(",\"events\":[");
            var list = _ev[i];
            for (int k = 0; k < list.Count; k++)
            {
                var x = list[k];
                if (k > 0) sb.Append(',');
                sb.Append("{\"t\":").Append(Fmt.Hundredths((x.Tick - startTick) * 100L / Simulation.TicksPerSecond));
                if (x.Threat >= 0) sb.Append(",\"threat\":").Append(Fmt.Hundredths(x.Threat));
                if (x.Reward >= 0) sb.Append(",\"reward\":").Append(x.Reward.ToString(CultureInfo.InvariantCulture));
                if (x.Options > 0)
                {
                    sb.Append(",\"decision\":{\"options\":").Append(x.Options.ToString(CultureInfo.InvariantCulture));
                    if (x.Spread >= 0) sb.Append(",\"spread\":").Append(Fmt.Hundredths(x.Spread));
                    sb.Append('}');
                }
                if (x.Novel != null) sb.Append(",\"novel\":\"").Append(x.Novel).Append('"');
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
