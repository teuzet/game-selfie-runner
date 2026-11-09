using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core;

namespace Game.Sim
{
    /// <summary>Числа без float: десятые и проценты считаются на long, текст не зависит от культуры и платформы.</summary>
    public static class Fmt
    {
        /// <summary>Десятые: 123 → «12.3».</summary>
        public static string Tenths(long t)
        {
            long a = Math.Abs(t);
            return (t < 0 ? "-" : "") + (a / 10).ToString(CultureInfo.InvariantCulture) + "." + (a % 10).ToString(CultureInfo.InvariantCulture);
        }

        public static long TicksToTenths(long ticks) => ticks * 10 / Simulation.TicksPerSecond;

        /// <summary>Медиана в тех же единицах; чётное число — среднее двух средних (целочисленное).</summary>
        public static long Median(List<long> v)
        {
            if (v.Count == 0) return 0;
            var s = v.ToArray(); Array.Sort(s);
            int n = s.Length;
            return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
        }

        /// <summary>Перцентиль p (0..100), ближайший ранг сверху.</summary>
        public static long Percentile(List<long> v, int p)
        {
            if (v.Count == 0) return 0;
            var s = v.ToArray(); Array.Sort(s);
            int idx = (int)(((long)p * s.Length + 99) / 100) - 1;
            return s[Math.Clamp(idx, 0, s.Length - 1)];
        }
    }

    /// <summary>Минимальный писатель JSON с отступами; порядок полей — как вызваны, "\n" вместо Environment.NewLine.</summary>
    public sealed class Jw
    {
        readonly StringBuilder _sb = new StringBuilder();
        readonly Stack<bool> _first = new Stack<bool>();
        int _depth;

        public Jw() { _first.Push(true); }

        void Sep()
        {
            if (!_first.Pop()) _sb.Append(',');
            _first.Push(false);
            if (_depth > 0) { _sb.Append('\n'); _sb.Append(' ', _depth * 2); }
        }

        public Jw Open(string? name, char c)
        {
            Sep();
            if (name != null) _sb.Append('"').Append(name).Append("\": ");
            _sb.Append(c);
            _depth++; _first.Push(true);
            return this;
        }

        public Jw Close(char c)
        {
            bool empty = _first.Pop();
            _depth--;
            if (!empty) { _sb.Append('\n'); _sb.Append(' ', _depth * 2); }
            _sb.Append(c);
            return this;
        }

        public Jw Obj(string? name = null) => Open(name, '{');
        public Jw EndObj() => Close('}');
        public Jw Arr(string? name = null) => Open(name, '[');
        public Jw EndArr() => Close(']');

        public Jw Raw(string name, string raw) { Sep(); _sb.Append('"').Append(name).Append("\": ").Append(raw); return this; }
        public Jw Int(string name, long v) => Raw(name, v.ToString(CultureInfo.InvariantCulture));
        public Jw Dec(string name, long tenths) => Raw(name, Fmt.Tenths(tenths));
        public Jw Bool(string name, bool v) => Raw(name, v ? "true" : "false");
        public Jw Str(string name, string v) => Raw(name, "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");

        public override string ToString() => _sb.ToString() + "\n";
    }

    /// <summary>DIR/metrics.json: каждый забег и сводка.</summary>
    public static class MetricsJson
    {
        public static string Build(IReadOnlyList<RunRecord> runs)
        {
            var w = new Jw();
            w.Obj();
            w.Str("note", "Времена — секунды (десятые), доли — проценты. Смены лидера: leaderChanges — по донатам после 30-й секунды забега, leaderChangesTotal — за весь забег. longestQuietSec — самая долгая пауза между событиями ядра, кроме подсказок и смены зоны. donationGapPct — разрыв донатов 1-го и 2-го в % от донатов 2-го (null, если у 2-го ноль). zoneSec — секунды у быка по зонам до финиша места. F-077: tossesCharge/tossesContact — подбросы по причине (рывок / контакт); zeroViewerSec и zeroViewerPct — время с 0 зрителей и его доля от забега места до финиша; donationsNoToss — донаты минус донаты за подброс. По забегу: bullDistToCrowdMedianM — средняя дистанция быка до медианы s толпы (м), bullFarPct — доля тиков с дистанцией больше 25 м, bullSpeedMps — средняя скорость быка, charges/chargeTossPct — рывки и доля рывков, кончившихся подбросом (null без рывков), leaderChangesNoToss — смены лидера после 30-й с по донатам без подбросов, tossesOfFinished — подбросы финишировавших (должно быть 0).");
            w.Int("runCount", runs.Count);
            w.Arr("runs");
            foreach (var r in runs) WriteRun(w, r);
            w.EndArr();
            WriteSummary(w, runs);
            w.EndObj();
            return w.ToString();
        }

        static long GapPct(RunRecord r) => r.DonationsSecond > 0 ? (r.DonationsFirst - r.DonationsSecond) * 1000L / r.DonationsSecond : -1;

        // F-077: производные числа забега и места (десятые; проценты — десятые процента)
        static long BullDistTenths(RunRecord r) => r.BullTicks > 0 ? r.BullDistRawSum * 10 / (Game.Core.Math.Fixed.OneRaw * r.BullTicks) : 0;
        static long BullSpeedTenths(RunRecord r) => r.BullTicks > 0 ? r.BullSpeedRawSum * 10 / (Game.Core.Math.Fixed.OneRaw * r.BullTicks) : 0;
        static long BullFarPct(RunRecord r) => r.BullTicks > 0 ? r.BullFarTicks * 1000L / r.BullTicks : 0;
        static long ChargeTossPct(RunRecord r) => r.Charges > 0 ? r.ChargesEndedInToss * 1000L / r.Charges : -1;
        static long ZeroPct(SeatRecord s) => s.RaceTicks > 0 ? s.ZeroViewerTicks * 1000L / s.RaceTicks : 0;

        static void WriteRun(Jw w, RunRecord r)
        {
            var win = r.Seats[r.WinnerSeat];
            w.Obj();
            w.Int("seed", (long)r.Seed);
            w.Bool("finished", r.Finished);
            w.Dec("durationSec", Fmt.TicksToTenths(r.DurationTicks));
            w.Obj("winner").Int("seat", win.Seat).Str("character", win.Character).Str("temperament", win.Temperament).EndObj();
            w.Int("leaderChanges", r.LeaderChanges);
            w.Int("leaderChangesTotal", r.LeaderChangesTotal);
            w.Dec("longestQuietSec", Fmt.TicksToTenths(r.LongestQuietTicks));
            w.Int("leaderChangesNoToss", r.LeaderChangesNoToss);
            w.Obj("bull").Dec("distToCrowdMedianM", BullDistTenths(r)).Dec("farPct", BullFarPct(r)).Dec("speedMps", BullSpeedTenths(r))
             .Int("charges", r.Charges);
            long ctp = ChargeTossPct(r);
            if (ctp < 0) w.Raw("chargeTossPct", "null"); else w.Dec("chargeTossPct", ctp);
            w.Int("tossesOfFinished", r.TossesOfFinished).EndObj();
            w.Obj("donationGap").Int("first", r.DonationsFirst).Int("second", r.DonationsSecond);
            long gap = GapPct(r);
            if (gap < 0) w.Raw("pct", "null"); else w.Dec("pct", gap);
            w.EndObj();
            w.Arr("seats");
            foreach (var s in r.Seats)
            {
                w.Obj().Int("seat", s.Seat).Str("character", s.Character).Str("temperament", s.Temperament).Int("place", s.Place)
                 .Int("donations", s.Donations).Int("viewers", s.Viewers)
                 .Int("tosses", s.Tosses).Int("collisions", s.Collisions).Int("shoves", s.Shoves).Int("trips", s.Trips)
                 .Int("gotShoved", s.GotShoved).Int("gotTripped", s.GotTripped)
                 .Int("tossesCharge", s.TossesCharge).Int("tossesContact", s.TossesContact)
                 .Dec("zeroViewerSec", Fmt.TicksToTenths(s.ZeroViewerTicks)).Dec("zeroViewerPct", ZeroPct(s))
                 .Int("donationsNoToss", s.DonationsNoToss);
                w.Obj("zoneSec").Dec("hot", Fmt.TicksToTenths(s.ZoneTicks[0])).Dec("warm", Fmt.TicksToTenths(s.ZoneTicks[1]))
                 .Dec("cool", Fmt.TicksToTenths(s.ZoneTicks[2])).Dec("cold", Fmt.TicksToTenths(s.ZoneTicks[3])).EndObj();
                w.EndObj();
            }
            w.EndArr();
            w.EndObj();
        }

        static void WriteSummary(Jw w, IReadOnlyList<RunRecord> runs)
        {
            var dur = new List<long>(); var quiet = new List<long>(); var lc = new List<long>(); var lct = new List<long>(); var gap = new List<long>();
            int unfinished = 0;
            foreach (var r in runs)
            {
                if (!r.Finished) unfinished++;
                dur.Add(Fmt.TicksToTenths(r.DurationTicks)); quiet.Add(Fmt.TicksToTenths(r.LongestQuietTicks));
                lc.Add(r.LeaderChanges); lct.Add(r.LeaderChangesTotal);
                long g = GapPct(r); if (g >= 0) gap.Add(g);
            }
            w.Obj("summary");
            w.Int("unfinished", unfinished);
            w.Obj("durationSec").Dec("median", Median(dur)).Dec("min", Min(dur)).Dec("max", Max(dur)).EndObj();
            w.Obj("longestQuietSec").Dec("median", Median(quiet)).Dec("p90", Fmt.Percentile(quiet, 90)).Dec("max", Max(quiet)).EndObj();
            w.Obj("leaderChanges").Int("median", Median(lc)).Int("min", MinI(lc)).Int("max", MaxI(lc)).EndObj();
            w.Obj("leaderChangesTotal").Int("median", Median(lct)).Int("min", MinI(lct)).Int("max", MaxI(lct)).EndObj();
            w.Obj("donationGapPct").Dec("median", Median(gap)).Dec("p90", Fmt.Percentile(gap, 90)).EndObj();

            // F-077: медианы и p90 новых чисел (забеги, затем места)
            var lcn = new List<long>(); var bd = new List<long>(); var far = new List<long>(); var bs = new List<long>();
            var ch = new List<long>(); var ctp = new List<long>(); var tf = new List<long>();
            var tc = new List<long>(); var tk = new List<long>(); var zs = new List<long>(); var zp = new List<long>(); var dn = new List<long>();
            foreach (var r in runs)
            {
                lcn.Add(r.LeaderChangesNoToss); bd.Add(BullDistTenths(r)); far.Add(BullFarPct(r)); bs.Add(BullSpeedTenths(r));
                ch.Add(r.Charges); tf.Add(r.TossesOfFinished);
                long c = ChargeTossPct(r); if (c >= 0) ctp.Add(c);
                foreach (var s in r.Seats)
                {
                    tc.Add(s.TossesCharge); tk.Add(s.TossesContact); zs.Add(Fmt.TicksToTenths(s.ZeroViewerTicks)); zp.Add(ZeroPct(s)); dn.Add(s.DonationsNoToss);
                }
            }
            w.Obj("leaderChangesNoToss").Int("median", Median(lcn)).Int("p90", Fmt.Percentile(lcn, 90)).EndObj();
            w.Obj("bullDistToCrowdMedianM").Dec("median", Median(bd)).Dec("p90", Fmt.Percentile(bd, 90)).EndObj();
            w.Obj("bullFarPct").Dec("median", Median(far)).Dec("p90", Fmt.Percentile(far, 90)).EndObj();
            w.Obj("bullSpeedMps").Dec("median", Median(bs)).Dec("p90", Fmt.Percentile(bs, 90)).EndObj();
            w.Obj("charges").Int("median", Median(ch)).Int("p90", Fmt.Percentile(ch, 90)).EndObj();
            w.Obj("chargeTossPct").Dec("median", Median(ctp)).Dec("p90", Fmt.Percentile(ctp, 90)).EndObj();
            w.Obj("tossesOfFinished").Int("median", Median(tf)).Int("p90", Fmt.Percentile(tf, 90)).EndObj();
            w.Obj("seatTossesCharge").Int("median", Median(tc)).Int("p90", Fmt.Percentile(tc, 90)).EndObj();
            w.Obj("seatTossesContact").Int("median", Median(tk)).Int("p90", Fmt.Percentile(tk, 90)).EndObj();
            w.Obj("seatZeroViewerSec").Dec("median", Median(zs)).Dec("p90", Fmt.Percentile(zs, 90)).EndObj();
            w.Obj("seatZeroViewerPct").Dec("median", Median(zp)).Dec("p90", Fmt.Percentile(zp, 90)).EndObj();
            w.Obj("seatDonationsNoToss").Int("median", Median(dn)).Int("p90", Fmt.Percentile(dn, 90)).EndObj();
            WriteGroups(w, "byCharacter", runs, s => s.Character);
            WriteGroups(w, "byTemperament", runs, s => s.Temperament);
            w.EndObj();
        }

        static long Min(List<long> v) { long m = 0; for (int i = 0; i < v.Count; i++) if (i == 0 || v[i] < m) m = v[i]; return m; }
        static long Max(List<long> v) { long m = 0; for (int i = 0; i < v.Count; i++) if (i == 0 || v[i] > m) m = v[i]; return m; }
        static long MinI(List<long> v) => Min(v);
        static long MaxI(List<long> v) => Max(v);
        static long Median(List<long> v) => Fmt.Median(v);

        /// <summary>Сводка по персонажу/характеру. Группы — в порядке первого появления по месту и сиду (детерминированно). Доли — на появление.</summary>
        static void WriteGroups(Jw w, string name, IReadOnlyList<RunRecord> runs, Func<SeatRecord, string> key)
        {
            var keys = new List<string>();
            foreach (var r in runs) foreach (var s in r.Seats) { string k = key(s); if (!keys.Contains(k)) keys.Add(k); }
            keys.Sort(StringComparer.Ordinal);
            w.Arr(name);
            foreach (string k in keys)
            {
                int app = 0, wins = 0;
                long don = 0, vw = 0, toss = 0, col = 0, sh = 0, tr = 0, tossC = 0, tossK = 0, zeroTicks = 0;
                var zeroList = new List<long>();
                var zone = new long[4];
                var tossList = new List<long>(); var donList = new List<long>(); var actList = new List<long>(); var maxZone = new List<long>();
                foreach (var r in runs)
                    foreach (var s in r.Seats)
                    {
                        if (key(s) != k) continue;
                        app++; if (s.Seat == r.WinnerSeat) wins++;
                        don += s.Donations; vw += s.Viewers; toss += s.Tosses; col += s.Collisions; sh += s.Shoves; tr += s.Trips;
                        tossC += s.TossesCharge; tossK += s.TossesContact; zeroTicks += s.ZeroViewerTicks; zeroList.Add(Fmt.TicksToTenths(s.ZeroViewerTicks));
                        for (int z = 0; z < 4; z++) zone[z] += s.ZoneTicks[z];
                        tossList.Add(s.Tosses); donList.Add(s.Donations); actList.Add(s.Shoves + s.Trips);
                        if (s.Seat == r.WinnerSeat)
                        {
                            long tot = 0, mx = 0;
                            foreach (int zt in s.ZoneTicks) { tot += zt; if (zt > mx) mx = zt; }
                            if (tot > 0) maxZone.Add(mx * 1000 / tot);
                        }
                    }
                w.Obj().Str("id", k).Int("appearances", app).Int("wins", wins);
                w.Dec("winSharePct", app > 0 ? wins * 1000L / app : 0);
                w.Int("donationsTotal", don).Int("viewersTotal", vw);
                w.Int("tossesChargeTotal", tossC).Int("tossesContactTotal", tossK);
                w.Dec("zeroViewerSecTotal", Fmt.TicksToTenths(zeroTicks)).Dec("zeroViewerSecMedian", Median(zeroList)).Dec("zeroViewerSecP90", Fmt.Percentile(zeroList, 90));
                w.Int("tossesTotal", toss).Int("collisionsTotal", col).Int("shovesTotal", sh).Int("tripsTotal", tr);
                w.Int("tossesMedian", Median(tossList)).Int("donationsMedian", Median(donList)).Int("shovesTripsMedian", Median(actList));
                w.Obj("zoneSecTotal").Dec("hot", Fmt.TicksToTenths(zone[0])).Dec("warm", Fmt.TicksToTenths(zone[1]))
                 .Dec("cool", Fmt.TicksToTenths(zone[2])).Dec("cold", Fmt.TicksToTenths(zone[3])).EndObj();
                // Доля забега в самой длинной зоне у победителя этой группы (H7: «ни одна зона не дольше 70%»); пусто, если группа не побеждала.
                if (maxZone.Count > 0) w.Dec("winnerLongestZonePctMedian", Median(maxZone));
                w.EndObj();
            }
            w.EndArr();
        }
    }
}
