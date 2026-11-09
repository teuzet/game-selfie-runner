using System;
using Game.Core.Bull;
using Game.Core.Math;
using Game.Core.Street;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Bots
{
    /// <summary>Тип текущего блока для бота (design/F-005.md §2.1 п.1): правило «памяти о блоке» зависит только от него.</summary>
    public enum BotBlock : byte { Straight = 0, Market = 1, Plaza = 2, Pond = 3 }

    /// <summary>Подсказка чата о препятствии (§2.1 п.7): сторона и «через сколько тиков». Тип «бельё на всю ширину» — Full.</summary>
    public struct BotHint
    {
        public int Side;       // −1 слева, 0 по центру, +1 справа
        public bool Full;      // на всю ширину: не рулит
        public int TicksUntil; // до препятствия
    }

    /// <summary>Источник подсказок бота: заглушка до F-021 (чат). Вызывается раз в тик для места бота, пишет новые подсказки этого тика.</summary>
    public interface IBotHintSource
    {
        int Collect(RaceState st, StreetMap street, int seat, Span<BotHint> dst);
    }

    /// <summary>Сосед в кадре бота: сзади до 30 м или сбоку (|Δs| ≤ 1.5 м). Всё, что видно во фронталке (§2.1 п.5).</summary>
    public struct BotNeighbor
    {
        public bool Valid;
        public Fixed DS, DX; // относительно бота: DS > 0 — сосед впереди
        public Fixed X;      // абсолютный x соседа (бот видит, куда тот смещается)
        public Fixed Speed;
    }

    /// <summary>Наблюдения бота за тик — ровно design/F-005.md §2.1, и ничего сверх (препятствий впереди, чужой выносливости здесь нет).</summary>
    public struct BotView
    {
        public int Seat, Tick;
        // п.1 свой x и улица
        public Fixed S, X, Width;
        public BotBlock Block;
        public int WaterSide;     // −1/+1 — сторона воды пруда, 0 — нет
        public Fixed WaterAcross; // ширина воды у стены
        // п.2 свои темп и состояние
        public Fixed Speed, Stamina;
        public int StunTicks, NoSignalTicks, InvulnTicks, ShoveCd, TripCd;
        // п.3 счётчики стрима
        public long Viewers, Donations;
        // п.4 бык
        public Fixed BullDist, BullX; // BullDist = мой s − s быка
        public bool BullRushing, RushOnMe;
        // п.5 соседи в кадре по абсолютному месту; п.6 память о впереди ведёт BotMemory
        public BotNeighbor Nb0, Nb1, Nb2, Nb3;
        // п.7 подсказки этого тика
        public int HintCount;
        public BotHint Hint0, Hint1;
        // п.9 квест (id или ""): до F-021/квестов всегда пусто
        public string Quest;

        public bool Down => StunTicks > 0 || NoSignalTicks > 0;
        public const int MaxHints = 2;

        public BotNeighbor Neighbor(int seat)
        {
            switch (seat) { case 0: return Nb0; case 1: return Nb1; case 2: return Nb2; default: return Nb3; }
        }

        public BotHint Hint(int i) => i == 0 ? Hint0 : Hint1;

        /// <summary>Собирает наблюдения места из состояния. Зовёт Simulation, раз в тик; без аллокаций.</summary>
        public static BotView Build(RaceState st, StreetMap street, BotData bd, int seat, IBotHintSource? hints)
        {
            var v = new BotView { Seat = seat, Tick = st.Tick, Quest = "" };
            ref readonly RunnerState me = ref st.Runners[seat];
            v.S = Fixed.FromRaw(me.S); v.X = Fixed.FromRaw(me.X); v.Speed = Fixed.FromRaw(me.Speed);
            v.Stamina = Fixed.FromRaw(me.Stamina);
            v.Width = street.WidthAt(v.S);
            int bi = street.BlockIndexAt(v.S);
            ref readonly PlacedBlock pb = ref street.Blocks[bi];
            switch (pb.Def.Id)
            {
                case "market": v.Block = BotBlock.Market; break;
                case "plaza": v.Block = BotBlock.Plaza; break;
                case "pond": v.Block = BotBlock.Pond; break;
                default: v.Block = BotBlock.Straight; break;
            }
            if (v.Block == BotBlock.Pond && pb.WaterSide != 0)
            {
                v.WaterSide = pb.WaterSide;
                var fx = pb.Def.Fixed;
                for (int f = 0; f < fx.Length; f++) if (fx[f].Kind == "water") v.WaterAcross = fx[f].Across;
            }
            v.StunTicks = me.StunTicks;
            v.NoSignalTicks = st.Runners[seat].Toss.NoSignalTicks;
            v.InvulnTicks = st.Runners[seat].Toss.InvulnTicks;
            v.ShoveCd = st.Runners[seat].Action.ShoveCd; v.TripCd = st.Runners[seat].Action.TripCd;
            v.Viewers = st.Runners[seat].Viewing.Viewers; v.Donations = st.Runners[seat].Viewing.Donations;
            v.BullDist = v.S - Fixed.FromRaw(st.Bull.S);
            v.BullX = Fixed.FromRaw(st.Bull.X);
            v.BullRushing = st.Charge.ChargeLeft > 0;
            v.RushOnMe = v.BullRushing && st.Charge.Target == seat;
            for (int o = 0; o < RaceConfig.SeatCount; o++)
            {
                if (o == seat) continue;
                Fixed ds = Fixed.FromRaw(st.Runners[o].S) - v.S;
                if (ds < -bd.RearFrame || ds > bd.SideFrame) continue; // впереди дальше бок-зоны — не в кадре
                var n = new BotNeighbor { Valid = true, DS = ds, X = Fixed.FromRaw(st.Runners[o].X), Speed = Fixed.FromRaw(st.Runners[o].Speed) };
                n.DX = n.X - v.X;
                switch (o) { case 0: v.Nb0 = n; break; case 1: v.Nb1 = n; break; case 2: v.Nb2 = n; break; default: v.Nb3 = n; break; }
            }
            if (hints != null)
            {
                Span<BotHint> buf = stackalloc BotHint[MaxHints];
                int k = hints.Collect(st, street, seat, buf);
                if (k > MaxHints) k = MaxHints;
                v.HintCount = k;
                if (k > 0) v.Hint0 = buf[0];
                if (k > 1) v.Hint1 = buf[1];
            }
            return v;
        }
    }
}
