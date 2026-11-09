using System;
using System.Globalization;
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
            return new EconomyData
            {
                HotRange = Num(json, "hotRange"), WarmRange = Num(json, "warmRange"), CoolRange = Num(json, "coolRange"),
                HotRate = Num(json, "hotRate"), WarmRate = Num(json, "warmRate"), CoolRate = Num(json, "coolRate"), ColdRate = Num(json, "coldRate"),
                Start = Num(json, "start"), Cap = Num(json, "cap"), Floor = Num(json, "floor"),
                DonationDivisor = Num(json, "donationDivisor"), NoSignalLoss = Num(json, "noSignalLoss"),
            };
        }

        static Fixed Num(string json, string key)
        {
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) throw new FormatException("economy.json: нет ключа " + key);
            int p = json.IndexOf(':', k) + 1;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            int e = p;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '.' || json[e] == '-')) e++;
            string t = json.Substring(p, e - p);
            if (t.Length == 0) throw new FormatException("economy.json: ключ " + key + " не число");
            bool neg = t[0] == '-';
            if (neg) t = t.Substring(1);
            int dot = t.IndexOf('.');
            string ip = dot < 0 ? t : t.Substring(0, dot);
            string fp = dot < 0 ? "" : t.Substring(dot + 1);
            Fixed v = Fixed.FromInt(int.Parse(ip, CultureInfo.InvariantCulture));
            if (fp.Length > 0)
            {
                long den = 1;
                for (int i = 0; i < fp.Length; i++) den *= 10;
                long num = long.Parse(fp, CultureInfo.InvariantCulture);
                v = v + Fixed.FromRaw((num * Fixed.OneRaw + den / 2) / den);
            }
            return neg ? -v : v;
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
            v.Zone = -1; v.PrevZone = -1; v.LastZoneTick = -1;
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
