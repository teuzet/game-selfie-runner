using Game.Core.Math;

namespace Game.Core
{
    /// <summary>
    /// Всё состояние забега. Только структуры и массивы фиксированного размера. Хаб: композиция состояний модулей
    /// (MODULES.md); поля и хэши структур принадлежат модулям-владельцам, здесь только порядок.
    /// </summary>
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

        /// <summary>Начальное состояние забега: каждый модуль инициализирует своё; lineS — линия старта бегунов.</summary>
        public RaceState(RaceConfig config, GameData data, Game.Core.Street.Street street, Fixed lineS)
        {
            Dyn = new Game.Core.Street.ObstacleDyn[street.Obstacles.Length];
            Hit = new byte[RaceConfig.SeatCount * Game.Core.Runners.Collisions.HitStride(street)];
            Game.Core.Street.MovingObstacles.Init(street, Dyn);
            for (int i = 0; i < Runners.Length; i++)
                RunnerState.Init(ref Runners[i], data.RunnerData, data.CharacterData.Get(config.GetSeat(i).CharacterId), data.EconomyData, lineS);
            for (int i = 0; i < Runners.Length; i++) Game.Core.Hints.ChatHints.InitState(config.Seed, i, ref Runners[i].Hints);
            Rng = new DetRandom(config.Seed);
            for (int i = 0; i < Bots.Length; i++) Game.Core.Bots.BotMemory.Init(ref Bots[i], config.Seed, i);
            Game.Core.Race.Phases.InitState(data.BullData, out Phase, out PhaseTicks, out Best);
            Game.Core.Bull.BullCharge.InitState(config.Seed, ref Charge);
            for (int i = 0; i < Frame.Length; i++) Game.Core.Economy.FrameSteal.InitState(ref Frame[i]);
            Game.Core.Bull.BullMovement.Init(data.BullData, lineS, ref Bull);
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

        /// <summary>
        /// FNV-1a (64 бита): композиция хэшей модулей в фиксированном порядке — хаб (тик, ГСЧ), Runners (места со своими
        /// подструктурами: Collision, Action, Toss из Bull, Viewing из Economy), Street, Bull, Events, Economy (кадр),
        /// Race, Bots. Поля структур хэширует их модуль-владелец.
        /// </summary>
        public ulong StateHash()
        {
            ulong h = Hashing.Seed;
            h = Hashing.Mix(h, (ulong)Tick);
            h = Hashing.Mix(h, Rng.State);
            h = Hashing.Mix(h, Rng.Inc);
            for (int i = 0; i < Runners.Length; i++) h = Runners[i].Hash(h);
            h = Game.Core.Street.MovingObstacles.Hash(h, Dyn, Hit);
            h = Bull.Hash(h);
            h = Charge.Hash(h);
            for (int i = 0; i < Ev.Length; i++) h = Ev[i].Hash(h);
            for (int i = 0; i < Frame.Length; i++) h = Frame[i].Hash(h);
            h = Game.Core.Race.Phases.Hash(h, Phase, PhaseTicks, Best, Trackers);
            for (int i = 0; i < Bots.Length; i++) h = Bots[i].Hash(h);
            return h;
        }
    }
}
