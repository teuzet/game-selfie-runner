using Game.Core.Math;

namespace Game.Core.Bots
{
    /// <summary>Мозг бота: вход места по наблюдениям и памяти (design/F-005.md §2). Ни до RaceState, ни до улицы дела нет.</summary>
    public interface IBotBrain
    {
        PlayerInput Decide(in BotView v, ref BotMemory m);
    }

    /// <summary>Общий цикл решения (§2.2) и руль (§2.3). Темп и действие — в наследнике (§2.4): у каждого характера свой код.</summary>
    public abstract class BotBrain : IBotBrain
    {
        protected readonly BotData Data;
        protected readonly BotDef Def;

        protected BotBrain(BotData data, BotDef def) { Data = data; Def = def; }

        public PlayerInput Decide(in BotView v, ref BotMemory m)
        {
            Observe(v, ref m);
            if (v.Down) { Forget(v, ref m); return default; }
            bool rush = v.RushOnMe && m.RushSeen != 0 && v.Tick >= m.RushReactAt;
            Fixed tx = TargetX(v, ref m, rush);
            int steer = Steer(v.X, tx);
            int pace = ChoosePace(v, ref m, rush);
            bool action = ChooseAction(v, ref m);
            return new PlayerInput(steer, pace, action);
        }

        /// <summary>Темп −1/0/+1 (раз в период характера; рывок на меня — вне периода).</summary>
        protected abstract int ChoosePace(in BotView v, ref BotMemory m, bool rushOnMe);

        /// <summary>Контекстное действие. По умолчанию — никогда (трус).</summary>
        protected virtual bool ChooseAction(in BotView v, ref BotMemory m) => false;

        // ---- задержка реакции ----
        protected int React(ref BotMemory m) => m.Rng.Range(Def.ReactionMin, Def.ReactionMax + 1);

        // ---- память: соседи, подсказки, рывок ----
        void Observe(in BotView v, ref BotMemory m)
        {
            Prune(ref m.P0, v); Prune(ref m.P1, v);
            Fixed ax = Fixed.Abs(v.X);
            if (ax > Data.LaneSwitch) m.LaneSide = v.X > Fixed.Zero ? 1 : -1;

            for (int o = 0; o < RaceConfig.SeatCount; o++)
            {
                if (o == v.Seat) continue;
                BotNeighbor n = v.Neighbor(o);
                ref BotNeighborMem mem = ref BotMemory.Nb(ref m, o);
                if (!n.Valid) { mem.PrevTick = -1; mem.Dir = 0; continue; }
                int dir = 0;
                if (mem.PrevTick >= 0 && mem.PrevTick == v.Tick - 1)
                {
                    Fixed dx = n.X - Fixed.FromRaw(mem.PrevX);
                    if (dx > Data.ShiftEps) dir = 1; else if (dx < -Data.ShiftEps) dir = -1;
                }
                bool started = dir != 0 && dir != mem.Dir;
                mem.Dir = dir; mem.PrevX = n.X.Raw; mem.PrevTick = v.Tick; mem.LastTick = v.Tick;
                mem.DS = n.DS.Raw; mem.DX = n.DX.Raw; mem.Speed = n.Speed.Raw;
                if (started && !v.Down && m.NbReactAt < 0 && n.DS >= -Data.NeighborReact && n.DS <= Data.SideFrame)
                {
                    m.NbReactAt = v.Tick + React(ref m);
                    m.NbDir = dir;
                }
            }

            if (v.RushOnMe)
            {
                if (m.RushSeen == 0 && !v.Down) { m.RushSeen = 1; m.RushReactAt = v.Tick + React(ref m); }
            }
            else m.RushSeen = 0;

            if (v.Down) return;
            for (int k = 0; k < v.HintCount; k++)
            {
                BotHint h = v.Hint(k);
                int r = React(ref m);
                if (!m.Rng.Chance(Def.TrustHints)) continue;
                var plan = new BotPlan
                {
                    Used = 1, ReactAt = v.Tick + r, Side = h.Side, Full = h.Full ? 1 : 0,
                    ExpectedS = (v.S + v.Speed * Fixed.FromFraction(h.TicksUntil, Simulation.TicksPerSecond)).Raw,
                };
                if (m.P0.Used == 0) m.P0 = plan; else if (m.P1.Used == 0) m.P1 = plan; // третья подсказка при двух живых — теряется
            }
        }

        void Prune(ref BotPlan p, in BotView v)
        {
            if (p.Used != 0 && Fixed.FromRaw(p.ExpectedS) + Data.PassMargin < v.S) p = default;
        }

        /// <summary>«Нет сигнала»/стан: вход не действует; задержки сбрасываются, планы позади уже стёрты Prune.</summary>
        void Forget(in BotView v, ref BotMemory m)
        {
            m.NbReactAt = -1; m.RushSeen = 0;
            if (m.P0.Used != 0 && m.P0.ReactAt > v.Tick) m.P0.ReactAt = v.Tick;
            if (m.P1.Used != 0 && m.P1.ReactAt > v.Tick) m.P1.ReactAt = v.Tick;
        }

        // ---- руль (§2.3) ----
        static int Steer(Fixed x, Fixed tx, Fixed deadZone)
        {
            Fixed d = tx - x;
            return d > deadZone ? 1 : d < -deadZone ? -1 : 0;
        }

        int Steer(Fixed x, Fixed tx) => Steer(x, tx, Data.DeadZone);

        Fixed Lim(in BotView v, Fixed x)
        {
            Fixed half = v.Width / Fixed.FromInt(2) - Data.WallMargin;
            if (half < Fixed.Zero) half = Fixed.Zero;
            return Fixed.Clamp(x, -half, half);
        }

        Fixed TargetX(in BotView v, ref BotMemory m, bool rush)
        {
            Fixed w = v.Width;
            // 1. рывок на меня (только у тех, у кого dodgeRush)
            if (rush && Def.DodgeRush)
            {
                int toCenter = v.BullX > Fixed.Zero ? -1 : v.BullX < Fixed.Zero ? 1 : m.LaneSide;
                return Lim(v, v.BullX + Fixed.FromInt(toCenter) * Data.RushSidestep);
            }
            // 2. подсказка: ближайшая живая; бельё на всю ширину не рулит
            ref BotPlan near = ref m.P0;
            if (m.P0.Used == 0 || (m.P1.Used != 0 && m.P1.ExpectedS < m.P0.ExpectedS)) near = ref m.P1;
            if (near.Used != 0 && near.Full == 0 && v.Tick >= near.ReactAt)
            {
                Fixed quarter = w / Data.DodgeDiv;
                if (near.Side < 0) return Lim(v, quarter);
                if (near.Side > 0) return Lim(v, -quarter);
                int dir = v.X > Fixed.Zero ? 1 : v.X < Fixed.Zero ? -1 : m.LaneSide;
                return Lim(v, Fixed.FromInt(dir) * (w / Fixed.FromInt(2) - Data.CenterDodgeMargin));
            }
            // 3. соседи: отодвинуться от того, кто ближе 1 м сбоку; повторить уворот соседа
            for (int o = 0; o < RaceConfig.SeatCount; o++)
            {
                if (o == v.Seat) continue;
                BotNeighbor n = v.Neighbor(o);
                if (!n.Valid || Fixed.Abs(n.DS) > Data.SideFrame || Fixed.Abs(n.DX) >= Data.NeighborSeparation) continue;
                int away = n.DX < Fixed.Zero ? 1 : n.DX > Fixed.Zero ? -1 : m.LaneSide;
                return Lim(v, n.X + Fixed.FromInt(away) * Data.NeighborSeparation);
            }
            if (m.NbReactAt >= 0 && v.Tick >= m.NbReactAt)
            {
                m.NbReactAt = -1;
                if (m.P0.Used == 0 && m.P1.Used == 0 && m.Rng.Chance(Def.TrustNeighbors))
                {
                    m.NbHoldUntil = v.Tick + Data.NeighborHold;
                    m.NbTargetX = Lim(v, v.X + Fixed.FromInt(m.NbDir) * (w / Data.DodgeDiv)).Raw;
                }
            }
            if (v.Tick < m.NbHoldUntil) return Lim(v, Fixed.FromRaw(m.NbTargetX));
            // 4. память о блоке: только тип блока
            return Lim(v, BlockLane(v, m.LaneSide));
        }

        Fixed BlockLane(in BotView v, int side)
        {
            Fixed w = v.Width;
            Fixed s = Fixed.FromInt(side);
            switch (v.Block)
            {
                case BotBlock.Market: return s * Data.MarketAxis;
                case BotBlock.Plaza: return s * (w / Data.DodgeDiv + Data.PlazaExtra);
                case BotBlock.Pond:
                    if (v.WaterSide != 0)
                        return Fixed.FromInt(v.WaterSide) * (w / Fixed.FromInt(2) - v.WaterAcross - Data.PondEdge);
                    return s * (w / Data.DodgeDiv);
                default: return s * (w / Data.DodgeDiv);
            }
        }
    }

    /// <summary>Выбор мозга по характеру места.</summary>
    public static class BotBrains
    {
        /// <summary>трус — Coward; адреналинщик и тролль до F-024 ведутся тем же кодом с их числами из bots.json.</summary>
        public static IBotBrain Create(BotData data, int temperament) => new Coward(data, data.Get(temperament));

        /// <summary>Один тик мозга места: наблюдения → решение. Память лежит в RaceState.Bots.</summary>
        public static PlayerInput Think(IBotBrain brain, RaceState st, Game.Core.Street.Street street, BotData data, int seat, IBotHintSource? hints)
        {
            BotView v = BotView.Build(st, street, data, seat, hints);
            return brain.Decide(v, ref st.Bots[seat]);
        }
    }
}
