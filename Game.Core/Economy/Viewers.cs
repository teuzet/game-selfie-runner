using Game.Core.Data;
using Game.Core.Math;

namespace Game.Core.Economy
{
    /// <summary>Зрители и донаты места. Входит в RaceState и хэш. Fixed хранится как raw.</summary>
    public struct ViewerState
    {
        public long Viewers;        // зрители (raw)
        public long Donations;      // накопленные донаты (raw), целая часть — деньги
        public int Zone;            // ViewerZone текущей зоны; -1 — ещё не определена
        public int PrevZone;        // зона до последней смены, -1 — смен не было
        public int ZoneChanges;     // сколько раз зона менялась (факт для F-019: горячо/холодно)
        public int LastZoneTick;    // тик последней смены, -1 — не было
        public int FinishTick;      // тик финиша (F-020), -1 — ещё бежит; после финиша зрители и донаты заморожены
    }

    /// <summary>Зоны дистанции до быка: 0 — горячая, 3 — холодная. Для F-019 «горячо/холодно».</summary>
    public static class ViewerZone { public const int Hot = 0, Warm = 1, Cool = 2, Cold = 3; }

    /// <summary>Data/economy.json. Источник чисел — design/overview.md, «Зрители и донаты».</summary>
    public sealed class EconomyData
    {
        public Fixed HotRange = Fixed.FromInt(8), WarmRange = Fixed.FromInt(20), CoolRange = Fixed.FromInt(40);
        public Fixed HotRate = Fixed.FromInt(15), WarmRate = Fixed.FromInt(5), CoolRate = Fixed.FromInt(-4), ColdRate = Fixed.FromInt(-10);
        public Fixed Start = Fixed.FromInt(100), Cap = Fixed.FromInt(2000), Floor = Fixed.Zero;
        public Fixed DonationDivisor = Fixed.FromInt(100);
        public Fixed NoSignalLoss = Fixed.FromFraction(1, 10);

        public static EconomyData Default() => new EconomyData();

        public static EconomyData Parse(string json)
        {
            var j = Json.Parse("economy.json", json);
            return new EconomyData
            {
                HotRange = j.Num("hotRange"), WarmRange = j.Num("warmRange"), CoolRange = j.Num("coolRange"),
                HotRate = j.Num("hotRate"), WarmRate = j.Num("warmRate"), CoolRate = j.Num("coolRate"), ColdRate = j.Num("coldRate"),
                Start = j.Num("start"), Cap = j.Num("cap"), Floor = j.Num("floor"),
                DonationDivisor = j.Num("donationDivisor"), NoSignalLoss = j.Num("noSignalLoss"),
            };
        }
    }

    /// <summary>Зрители по зонам дистанции до быка, потолок/пол, донаты, «нет сигнала». Без аллокаций.</summary>
    public static class Viewers
    {
        const int Tps = Simulation.TicksPerSecond;

        public static void InitState(EconomyData d, ref ViewerState v)
        {
            v = default;
            v.Viewers = d.Start.Raw;
            v.Zone = -1; v.PrevZone = -1; v.LastZoneTick = -1; v.FinishTick = -1;
        }

        public static int ZoneOf(EconomyData d, Fixed dist) =>
            dist < d.HotRange ? ViewerZone.Hot : dist < d.WarmRange ? ViewerZone.Warm : dist < d.CoolRange ? ViewerZone.Cool : ViewerZone.Cold;

        /// <summary>Целые зрители места.</summary>
        public static int ViewersOf(RaceState st, int seat) => Fixed.FromRaw(st.Viewing[seat].Viewers).ToInt();
        /// <summary>Целые донаты места.</summary>
        public static int DonationsOf(RaceState st, int seat) => Fixed.FromRaw(st.Viewing[seat].Donations).ToInt();

        /// <summary>Один тик по всем местам (после движения бегунов и быка).</summary>
        public static void Step(Simulation sim)
        {
            var st = sim.State;
            var d = sim.EconomyData;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                ref var v = ref st.Viewing[i];
                if (v.FinishTick >= 0) continue; // финишировал: зрители не меняются
                var r = st.Runners[i];
                var fs = Fixed.FromRaw(r.S - st.Bull.S);
                var fx = Fixed.FromRaw(r.X - st.Bull.X);
                int zone = ZoneOf(d, Fixed.Sqrt(fs * fs + fx * fx));
                if (zone != v.Zone)
                {
                    if (v.Zone >= 0) { v.PrevZone = v.Zone; v.ZoneChanges++; v.LastZoneTick = st.Tick; }
                    v.Zone = zone;
                }
                long delta;
                if (st.Tosses[i].NoSignalTicks > 0)
                {
                    delta = -((v.Viewers * d.NoSignalLoss.Raw) >> Fixed.FractionBits) / Tps;
                }
                else
                {
                    Fixed rate = zone == ViewerZone.Hot ? d.HotRate : zone == ViewerZone.Warm ? d.WarmRate : zone == ViewerZone.Cool ? d.CoolRate : d.ColdRate;
                    // множитель персонажа — только на прирост; убыль от персонажа не зависит
                    if (rate.Raw > 0) rate = rate * sim.CharacterOf(i).Viewers;
                    delta = rate.Raw / Tps;
                    // донаты: зрители/100 в секунду, по зрителям до обновления
                    v.Donations += v.Viewers / (d.DonationDivisor.Raw >> Fixed.FractionBits) / Tps;
                }
                long nv = v.Viewers + delta;
                if (nv > d.Cap.Raw) nv = d.Cap.Raw;
                if (nv < d.Floor.Raw) nv = d.Floor.Raw;
                v.Viewers = nv;
            }
        }
    }
}
