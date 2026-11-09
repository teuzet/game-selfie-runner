using Game.Core.Math;

namespace Game.Core.Bots
{
    /// <summary>План уворота от препятствия по подсказке (§2.3 п.2): ожидаемый s препятствия и тик, с которого бот на него реагирует.</summary>
    public struct BotPlan
    {
        public int Used;
        public int ReactAt;     // тик, с которого план рулит (задержка R)
        public long ExpectedS;  // raw: s бота в момент подсказки + скорость × тиков до
        public int Side;        // −1/0/+1
        public int Full;        // на всю ширину
        public int Stall;       // лоток
    }

    /// <summary>Что бот помнит о соседе (§2.1 п.6): последний раз виден и куда смещался.</summary>
    public struct BotNeighborMem
    {
        public int LastTick;    // тик, когда последний раз был в кадре; −1 — не видел
        public long DS, DX, Speed; // raw, в тот момент
        public long PrevX;      // абсолютный x на прошлом тике (если был виден тогда)
        public int PrevTick;    // −1 — нет
        public int Dir;         // −1/0/+1, куда смещается поперёк сейчас
    }

    /// <summary>Память бота по месту. Входит в RaceState (Clone/хэш): решение не зависит ни от чего, кроме BotView и этой памяти.</summary>
    public struct BotMemory
    {
        public DetRandom Rng;          // собственный поток бота: задержки реакции и «верит»
        public int TempoNext;          // тик следующего пересмотра темпа
        public int Sprinting;          // решение о спринте с гистерезисом
        public int Pace;               // удерживаемый темп между пересмотрами
        public int RushSeen, RushReactAt;
        public BotPlan P0, P1;
        public int NbReactAt, NbDir;   // отложенная реакция на уворот соседа; −1 — нет
        public int NbHoldUntil;
        public long NbTargetX;
        public int LaneSide;           // сторона, где бот бежит (−1/+1), для «памяти о блоке»
        public BotNeighborMem N0, N1, N2, N3;
        // охота (F-024): жертва (−1 — нет), конец охоты, следующий поиск, с какого тика решает по геометрии
        public int HuntVictim, HuntEnd, HuntNext, HuntReady;
        public int HuntKind;           // что рулит охота в этот тик: 0 — ничего, 1 — позиция для толчка в препятствие, 2 — «идёт за жертвой»
        public long HuntTx;
        public int HuntAct, HuntActTick; // действие охоты (1 подножка, 2 толчок), поставлено в тике HuntActTick
        public int ActionNext;         // тик следующей проверки действия (период характера)
        public long LastTx;            // цель руля прошлого решения: «жертва на пути уворота»

        public static void Init(ref BotMemory m, ulong seed, int seat)
        {
            m = default;
            m.Rng = new DetRandom(seed ^ 0xB07B07B07B07B07UL, (ulong)(seat + 1));
            m.NbReactAt = -1;
            m.NbHoldUntil = -1;
            m.HuntVictim = -1; m.HuntActTick = -1;
            m.LaneSide = (seat & 1) == 0 ? 1 : -1;
            m.N0.LastTick = m.N1.LastTick = m.N2.LastTick = m.N3.LastTick = -1;
            m.N0.PrevTick = m.N1.PrevTick = m.N2.PrevTick = m.N3.PrevTick = -1;
        }

        public static ref BotNeighborMem Nb(ref BotMemory m, int seat)
        {
            switch (seat) { case 0: return ref m.N0; case 1: return ref m.N1; case 2: return ref m.N2; default: return ref m.N3; }
        }

        /// <summary>Сосед впереди по памяти: был в кадре не дольше maxAge тиков назад и с тех пор ушёл вперёд (§2.1 п.6).</summary>
        public bool AheadMemory(int seat, int tick, int maxAge, out long ds, out long dx)
        {
            ref BotNeighborMem n = ref Nb(ref this, seat);
            ds = n.DS; dx = n.DX;
            return n.LastTick >= 0 && tick - n.LastTick > 0 && tick - n.LastTick <= maxAge && n.DS > 0;
        }

        public ulong Hash(ulong h)
        {
            h = RaceState.Mix(h, Rng.State); h = RaceState.Mix(h, Rng.Inc);
            h = RaceState.Mix(h, (ulong)(uint)TempoNext); h = RaceState.Mix(h, (ulong)(uint)Sprinting); h = RaceState.Mix(h, (ulong)(uint)Pace);
            h = RaceState.Mix(h, (ulong)(uint)RushSeen); h = RaceState.Mix(h, (ulong)(uint)RushReactAt);
            h = HashPlan(h, P0); h = HashPlan(h, P1);
            h = RaceState.Mix(h, (ulong)(uint)NbReactAt); h = RaceState.Mix(h, (ulong)(uint)NbDir);
            h = RaceState.Mix(h, (ulong)(uint)NbHoldUntil); h = RaceState.Mix(h, (ulong)NbTargetX);
            h = RaceState.Mix(h, (ulong)(uint)LaneSide);
            h = HashNb(h, N0); h = HashNb(h, N1); h = HashNb(h, N2); h = HashNb(h, N3);
            h = RaceState.Mix(h, (ulong)(uint)HuntVictim); h = RaceState.Mix(h, (ulong)(uint)HuntEnd);
            h = RaceState.Mix(h, (ulong)(uint)HuntNext); h = RaceState.Mix(h, (ulong)(uint)HuntReady);
            h = RaceState.Mix(h, (ulong)(uint)HuntKind); h = RaceState.Mix(h, (ulong)HuntTx);
            h = RaceState.Mix(h, (ulong)(uint)HuntAct); h = RaceState.Mix(h, (ulong)(uint)HuntActTick);
            h = RaceState.Mix(h, (ulong)(uint)ActionNext); h = RaceState.Mix(h, (ulong)LastTx);
            return h;
        }

        static ulong HashPlan(ulong h, in BotPlan p)
        {
            h = RaceState.Mix(h, (ulong)(uint)p.Used); h = RaceState.Mix(h, (ulong)(uint)p.ReactAt);
            h = RaceState.Mix(h, (ulong)p.ExpectedS); h = RaceState.Mix(h, (ulong)(uint)p.Side);
            h = RaceState.Mix(h, (ulong)(uint)p.Full);
            return RaceState.Mix(h, (ulong)(uint)p.Stall);
        }

        static ulong HashNb(ulong h, in BotNeighborMem n)
        {
            h = RaceState.Mix(h, (ulong)(uint)n.LastTick); h = RaceState.Mix(h, (ulong)n.DS);
            h = RaceState.Mix(h, (ulong)n.DX); h = RaceState.Mix(h, (ulong)n.Speed);
            h = RaceState.Mix(h, (ulong)n.PrevX); h = RaceState.Mix(h, (ulong)(uint)n.PrevTick);
            return RaceState.Mix(h, (ulong)(uint)n.Dir);
        }
    }
}
