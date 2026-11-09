using Game.Core.Math;

namespace Game.Core
{
    /// <summary>Всё состояние забега. Только структуры и массивы фиксированного размера.</summary>
    public sealed class RaceState
    {
        public int Tick;
        public DetRandom Rng;
        /// <summary>Всё состояние места в одной структуре (F-063): движение, столкновения, устойчивость, толчки, подброс, зрители.</summary>
        public readonly RunnerState[] Runners = new RunnerState[RaceConfig.SeatCount];
        public BullState Bull;
        /// <summary>Рывки быка (F-017): таймеры, цель, счётчики.</summary>
        public Game.Core.Bull.BullChargeState Charge;
        /// <summary>Что сканер событий уже видел по местам (F-019).</summary>
        public readonly Game.Core.Events.EventState[] Ev = new Game.Core.Events.EventState[RaceConfig.SeatCount];
        /// <summary>«Влез в кадр» по местам-ворам (F-019).</summary>
        public readonly Game.Core.Economy.FrameState[] Frame = new Game.Core.Economy.FrameState[RaceConfig.SeatCount];
        /// <summary>Фаза забега (F-020). PhaseTicks: Countdown — тиков до старта, Finishing — тиков до закрытия.</summary>
        public Game.Core.Race.RacePhase Phase;
        public int PhaseTicks;
        /// <summary>Лучшие моменты (F-020) и их промежуточные счётчики по местам.</summary>
        public Game.Core.Race.Highlights Best;
        /// <summary>Память мозгов ботов по местам (F-023): задержки, планы уворота, соседи. Для мест-людей не используется.</summary>
        public readonly Game.Core.Bots.BotMemory[] Bots = new Game.Core.Bots.BotMemory[RaceConfig.SeatCount];
        public readonly Game.Core.Race.HighlightTracker[] Trackers = new Game.Core.Race.HighlightTracker[RaceConfig.SeatCount];
        /// <summary>Динамика подвижных препятствий, индекс = индекс в Street.Obstacles.</summary>
        public readonly Game.Core.Street.ObstacleDyn[] Dyn;
        /// <summary>«Препятствие уже действовало на бегуна»: seat * stride + индекс (препятствия, затем фонтан/вода по блокам).</summary>
        public readonly byte[] Hit;

        public RaceState(ulong seed, Game.Core.Runners.RunnerData runnerData, Game.Core.Bull.BullData bullData, Fixed lineS,
            Game.Core.Street.Street street, RaceConfig config, Game.Core.Runners.CharacterData characters, Game.Core.Economy.EconomyData economy)
        {
            Dyn = new Game.Core.Street.ObstacleDyn[street.Obstacles.Length];
            Hit = new byte[RaceConfig.SeatCount * Game.Core.Runners.Collisions.HitStride(street)];
            Game.Core.Street.MovingObstacles.Init(street, Dyn);
            for (int i = 0; i < Runners.Length; i++)
            {
                Game.Core.Runners.Collisions.InitState(ref Runners[i].Collision);
                Game.Core.Runners.Actions.InitState(ref Runners[i].Action);
                Runners[i].Stability = characters.Get(config.GetSeat(i).CharacterId).Stability.Raw;
                Game.Core.Bull.BullCharge.InitToss(ref Runners[i].Toss);
                Game.Core.Economy.Viewers.InitState(economy, ref Runners[i].Viewing);
            }
            Rng = new DetRandom(seed);
            for (int i = 0; i < Bots.Length; i++) Game.Core.Bots.BotMemory.Init(ref Bots[i], seed, i);
            Phase = Game.Core.Race.RacePhase.Countdown;
            PhaseTicks = Game.Core.Race.Phases.CountdownTicks(bullData);
            Best = Game.Core.Race.Highlights.Empty;
            Game.Core.Bull.BullCharge.InitState(seed, ref Charge);
            for (int i = 0; i < Frame.Length; i++) Game.Core.Economy.FrameSteal.InitState(ref Frame[i]);
            Game.Core.Bull.BullMovement.Init(bullData, lineS, ref Bull);
            for (int i = 0; i < Runners.Length; i++)
            {
                Game.Core.Runners.RunnerMovement.InitRunner(runnerData, ref Runners[i]);
                Runners[i].S = lineS.Raw; // бегуны на линии старта, бык — 25 м позади
            }
        }

        private RaceState(int dynLen, int hitLen)
        {
            Dyn = new Game.Core.Street.ObstacleDyn[dynLen];
            Hit = new byte[hitLen];
        }

        public RaceState Clone()
        {
            var c = new RaceState(Dyn.Length, Hit.Length);
            c.CopyFrom(this);
            return c;
        }

        public void CopyFrom(RaceState o)
        {
            Tick = o.Tick;
            Rng = o.Rng;
            Bull = o.Bull;
            Charge = o.Charge;
            Phase = o.Phase;
            PhaseTicks = o.PhaseTicks;
            Best = o.Best;
            System.Array.Copy(o.Bots, Bots, Bots.Length);
            System.Array.Copy(o.Trackers, Trackers, Trackers.Length);
            System.Array.Copy(o.Ev, Ev, Ev.Length);
            System.Array.Copy(o.Frame, Frame, Frame.Length);
            System.Array.Copy(o.Runners, Runners, Runners.Length);
            System.Array.Copy(o.Dyn, Dyn, Dyn.Length);
            System.Array.Copy(o.Hit, Hit, Hit.Length);
        }

        /// <summary>FNV-1a (64 бита) по всем полям.</summary>
        public ulong StateHash()
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (ulong)Tick);
            h = Mix(h, Rng.State);
            h = Mix(h, Rng.Inc);
            for (int i = 0; i < Runners.Length; i++)
            {
                h = Mix(h, (ulong)Runners[i].S);
                h = Mix(h, (ulong)Runners[i].X);
                h = Mix(h, (ulong)Runners[i].Speed);
                h = Mix(h, (ulong)Runners[i].Stamina);
                h = Mix(h, (ulong)Runners[i].Exhausted);
                h = Mix(h, (ulong)Runners[i].StunTicks);
            }
            for (int i = 0; i < Runners.Length; i++)
            {
                ref Game.Core.Runners.CollisionState c = ref Runners[i].Collision;
                h = Mix(h, (ulong)c.Count);
                h = Mix(h, (ulong)c.WaterCount);
                h = Mix(h, (ulong)c.LastKind);
                h = Mix(h, (ulong)c.LastS);
                h = Mix(h, (ulong)c.LastX);
                h = Mix(h, (ulong)c.LastTick);
                h = Mix(h, (ulong)c.SlowTicks);
                h = Mix(h, (ulong)c.SlowCap);
                h = Mix(h, (ulong)c.WaterPending);
                h = Mix(h, (ulong)c.ZoneActive);
                h = Mix(h, (ulong)c.ZoneRaw);
                h = Mix(h, (ulong)Runners[i].Stability);
            }
            for (int i = 0; i < Runners.Length; i++)
            {
                ref Game.Core.Runners.ActionState a = ref Runners[i].Action;
                h = Mix(h, (ulong)(uint)a.ShoveCd);
                h = Mix(h, (ulong)(uint)a.TripCd);
                h = Mix(h, (ulong)(uint)a.Shoved);
                h = Mix(h, (ulong)(uint)a.Tripped);
                h = Mix(h, (ulong)(uint)a.GotShoved);
                h = Mix(h, (ulong)(uint)a.GotTripped);
                h = Mix(h, (ulong)(uint)a.LastShoveTick);
                h = Mix(h, (ulong)(uint)a.LastTripTick);
                h = Mix(h, (ulong)(uint)a.LastGotShovedTick);
                h = Mix(h, (ulong)(uint)a.LastGotTrippedTick);
                h = Mix(h, (ulong)(uint)a.LastShoveTarget);
                h = Mix(h, (ulong)(uint)a.LastTripTarget);
                h = Mix(h, (ulong)(uint)a.LastShovedBy);
                h = Mix(h, (ulong)(uint)a.LastTrippedBy);
            }
            for (int i = 0; i < Dyn.Length; i++)
            {
                h = Mix(h, (ulong)Dyn[i].X);
                h = Mix(h, (ulong)(uint)Dyn[i].Dir);
                h = Mix(h, (ulong)(uint)Dyn[i].OpenTick);
            }
            for (int i = 0; i < Hit.Length; i++) h = Mix(h, Hit[i]);
            h = Mix(h, Charge.Rng.State);
            h = Mix(h, Charge.Rng.Inc);
            h = Mix(h, (ulong)(uint)Charge.Cooldown);
            h = Mix(h, (ulong)(uint)Charge.ChargeLeft);
            h = Mix(h, (ulong)(uint)Charge.Target);
            h = Mix(h, (ulong)(uint)Charge.Count);
            h = Mix(h, (ulong)(uint)Charge.Tosses);
            h = Mix(h, (ulong)(uint)Charge.LastChargeTick);
            h = Mix(h, (ulong)(uint)Charge.LastChargeTarget);
            for (int i = 0; i < Runners.Length; i++)
            {
                ref Game.Core.Bull.TossState t = ref Runners[i].Toss;
                h = Mix(h, (ulong)(uint)t.NoSignalTicks);
                h = Mix(h, (ulong)(uint)t.InvulnTicks);
                h = Mix(h, (ulong)(uint)t.Tossed);
                h = Mix(h, (ulong)(uint)t.Rose);
                h = Mix(h, (ulong)(uint)t.LastTossTick);
                h = Mix(h, (ulong)(uint)t.LastRoseTick);
                h = Mix(h, (ulong)t.LastTossFromS);
            }
            for (int i = 0; i < Runners.Length; i++)
            {
                ref Game.Core.Economy.ViewerState v = ref Runners[i].Viewing;
                h = Mix(h, (ulong)v.Viewers);
                h = Mix(h, (ulong)v.Donations);
                h = Mix(h, (ulong)(uint)v.Zone);
                h = Mix(h, (ulong)(uint)v.PrevZone);
                h = Mix(h, (ulong)(uint)v.ZoneChanges);
                h = Mix(h, (ulong)(uint)v.LastZoneTick);
            }
            for (int i = 0; i < Ev.Length; i++)
            {
                h = Mix(h, (ulong)(uint)Ev[i].SeenCollisions);
                h = Mix(h, (ulong)(uint)Ev[i].SeenWater);
                h = Mix(h, (ulong)(uint)Ev[i].SeenTossed);
                h = Mix(h, (ulong)(uint)Ev[i].SeenRose);
                h = Mix(h, (ulong)(uint)Ev[i].SeenShoved);
                h = Mix(h, (ulong)(uint)Ev[i].SeenTripped);
                h = Mix(h, (ulong)(uint)Ev[i].SeenGotShoved);
                h = Mix(h, (ulong)(uint)Ev[i].SeenGotTripped);
                h = Mix(h, (ulong)(uint)Ev[i].SeenZoneChanges);
                h = Mix(h, (ulong)(uint)Ev[i].LaidStamp);
                h = Mix(h, (ulong)(uint)Ev[i].Level);
                h = Mix(h, (ulong)(uint)Frame[i].Victim);
                h = Mix(h, (ulong)(uint)Frame[i].Ticks);
            }
            h = Mix(h, (ulong)(uint)Phase);
            h = Mix(h, (ulong)(uint)PhaseTicks);
            h = MixMoment(h, Best.MaxViewers);
            h = MixMoment(h, Best.LongestHot);
            h = MixMoment(h, Best.CostliestFail);
            for (int i = 0; i < Trackers.Length; i++)
            {
                h = Mix(h, (ulong)(uint)Trackers[i].HotRun);
                h = Mix(h, (ulong)(uint)Trackers[i].HotStart);
                h = Mix(h, (ulong)(uint)Trackers[i].FailActive);
                h = Mix(h, (ulong)(uint)Trackers[i].FailTick);
                h = Mix(h, (ulong)Trackers[i].FailViewers);
                h = Mix(h, (ulong)(uint)Runners[i].Viewing.FinishTick);
            }
            for (int i = 0; i < Bots.Length; i++) h = Bots[i].Hash(h);
            h = Mix(h, (ulong)Bull.S);
            h = Mix(h, (ulong)Bull.X);
            h = Mix(h, (ulong)Bull.Speed);
            h = Mix(h, (ulong)Bull.Phase);
            h = Mix(h, (ulong)Bull.Timer);
            h = Mix(h, (ulong)Bull.SlowTicks);
            h = Mix(h, (ulong)Bull.Mode);
            return h;
        }

        private static ulong MixMoment(ulong h, Game.Core.Race.Moment m)
        {
            h = Mix(h, (ulong)(uint)m.Seat);
            h = Mix(h, (ulong)(uint)m.Tick);
            return Mix(h, (ulong)m.Value);
        }

        internal static ulong Mix(ulong h, ulong v)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (v >> (i * 8)) & 0xFF;
                    h *= 1099511628211UL;
                }
            }
            return h;
        }
    }
}
