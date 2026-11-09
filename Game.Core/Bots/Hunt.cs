using Game.Core.Math;

namespace Game.Core.Bots
{
    /// <summary>Общие куски характеров по design/F-005.md §2.4: охота на соседа (тролль; адреналинщик на квесте «Уложи соседа»),
    /// квесты «Обгони X», гистерезис темпа. Все числа — из BotData/BotDef.</summary>
    public abstract partial class BotBrain
    {
        protected bool Quest(in BotView v, string id) => v.Quest == id && Def.Takes(id);

        /// <summary>Сосед по наблюдению (виден) или по памяти впереди (§2.1 п.5–6).</summary>
        bool Victim(in BotView v, ref BotMemory m, int o, out Fixed ds, out Fixed dx, out bool visible)
        {
            BotNeighbor n = v.Neighbor(o);
            visible = n.Valid;
            if (n.Valid) { ds = n.DS; dx = n.DX; return true; }
            bool ok = m.AheadMemory(o, v.Tick, Data.AheadMemory, out long mds, out long mdx);
            ds = Fixed.FromRaw(mds); dx = Fixed.FromRaw(mdx);
            return ok;
        }

        /// <summary>Цель квеста «Обгони X» ещё впереди (виден спереди-сбоку или помнится).</summary>
        protected bool QuestTargetAhead(in BotView v, ref BotMemory m)
        {
            if (!v.HasQuestTarget || v.QuestTarget == v.Seat) return false;
            return Victim(v, ref m, v.QuestTarget, out Fixed ds, out _, out _) && ds > Fixed.Zero;
        }

        protected static int Sign(Fixed x) => x > Fixed.Zero ? 1 : x < Fixed.Zero ? -1 : 0;

        void HuntAct(in BotView v, ref BotMemory m, int kind) { m.HuntAct = kind; m.HuntActTick = v.Tick; }

        bool InObstacleThird(in BotView v, Fixed x, int side)
        {
            Fixed half = v.Width / Fixed.FromInt(2);
            Fixed third = half - v.Width / Data.ThirdDiv, wall = half - Data.WallZone;
            return Fixed.FromInt(side) * x > (third < wall ? third : wall);
        }

        /// <summary>Один тик охоты: поиск жертвы раз в период, ожидание «R + wait» тиков, затем решение по геометрии (§2.4 тролль п.1–3).
        /// Пишет в память, что рулить (HuntKind/HuntTx) и какое действие нажать (HuntAct).</summary>
        protected void HuntStep(in BotView v, ref BotMemory m, Fixed radius, int period)
        {
            m.HuntKind = 0;
            if (v.Arena) { m.HuntVictim = -1; return; }
            Fixed ds, dx; bool visible;
            if (m.HuntVictim < 0)
            {
                if (v.Tick < m.HuntNext) return;
                m.HuntNext = v.Tick + period;
                int best = -1; Fixed bestD = Fixed.Zero, r2 = radius * radius;
                for (int o = 0; o < RaceConfig.SeatCount; o++)
                {
                    if (o == v.Seat || !Victim(v, ref m, o, out ds, out dx, out visible)) continue;
                    Fixed d2 = ds * ds + dx * dx;
                    if (d2 <= r2 && (best < 0 || d2 < bestD)) { best = o; bestD = d2; }
                }
                if (best < 0) return;
                m.HuntVictim = best;
                m.HuntEnd = v.Tick + Data.HuntTicks;
                m.HuntReady = v.Tick + React(ref m) + Def.WaitNeighbor;
            }
            if (!Victim(v, ref m, m.HuntVictim, out ds, out dx, out visible)) { m.HuntVictim = -1; return; }
            if (v.Tick < m.HuntReady) return; // ждёт, пока жертва увернётся
            Fixed vx = v.X + dx;
            bool beside = visible && Fixed.Abs(ds) <= Data.SideFrame && ds * ds + dx * dx <= Data.SideFrame * Data.SideFrame;
            if (v.Tick >= m.HuntEnd)
            {
                // п.3: охота кончается — толкает просто так, если жертва сбоку
                if (beside) HuntAct(v, ref m, 2); else m.HuntVictim = -1;
                return;
            }
            ref BotPlan p = ref NearPlan(ref m);
            if (p.Used != 0 && p.Full == 0 && p.Side != 0 && v.Speed > Fixed.Zero)
            {
                int ticks = ((Fixed.FromRaw(p.ExpectedS) - v.S) / v.Speed * Fixed.FromInt(Simulation.TicksPerSecond)).ToInt();
                if (ticks > Def.PushMax) return; // рано: ждёт окна
                if (ticks >= Def.PushMin && InObstacleThird(v, vx, p.Side) && InObstacleThird(v, vx + Fixed.FromInt(p.Side) * Data.PushShift, p.Side))
                {
                    // п.1: встаёт с противоположной от препятствия стороны жертвы и толкает
                    m.HuntKind = 1;
                    m.HuntTx = (vx - Fixed.FromInt(p.Side) * Data.NeighborSeparation).Raw;
                    if (beside && p.Side * Sign(dx) > 0) HuntAct(v, ref m, 2);
                    return;
                }
            }
            // п.2: жертва увернулась — идёт за ней, ≤ 1.2 м прямо впереди — подножка
            if (ds > Fixed.Zero)
            {
                m.HuntKind = 2;
                m.HuntTx = vx.Raw;
                if (ds <= Data.TripReach && ds >= Fixed.Abs(dx)) HuntAct(v, ref m, 1);
            }
        }

        /// <summary>Нажатие действия охоты: подножка/толчок, когда перезарядка готова; охота после нажатия кончается.</summary>
        protected bool HuntPress(in BotView v, ref BotMemory m)
        {
            if (m.HuntActTick != v.Tick || v.Arena) return false;
            if (m.HuntAct == 1 ? v.TripCd > 0 : v.ShoveCd > 0) return false;
            m.HuntVictim = -1; m.HuntActTick = -1;
            return true;
        }

        /// <summary>Темп на охоте (§2.4 тролль): отстаёт — бег или спринт при выносливости ≥ запаса, обогнал — трусца. false — охоты нет.</summary>
        protected bool HuntPace(in BotView v, ref BotMemory m, out int pace)
        {
            pace = 0;
            if (m.HuntVictim < 0 || !Victim(v, ref m, m.HuntVictim, out Fixed ds, out _, out _)) return false;
            if (ds > Data.SideFrame) pace = v.Stamina >= Def.ReserveStamina ? 1 : 0;
            else if (ds < -Data.SideFrame) pace = -1;
            return true;
        }

        /// <summary>Порог с гистерезисом (§2.2): включается при d &lt; порог − г, выключается при d &gt; порог + г.</summary>
        protected static bool Below(bool on, Fixed d, Fixed threshold, Fixed hyst) => on ? d <= threshold + hyst : d < threshold - hyst;
    }
}
