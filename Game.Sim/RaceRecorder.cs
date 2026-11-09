using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core;
using Game.Core.Economy;
using Game.Core.Events;
using Game.Core.Math;
using Game.Core.Race;

namespace Game.Sim
{
    /// <summary>Итоги одного места за забег.</summary>
    public sealed class SeatRecord
    {
        public int Seat, CharacterId, TemperamentId, Place;
        public string Character = "", Temperament = "";
        public int Donations, Viewers;
        public int Tosses, Collisions, Shoves, Trips, GotShoved, GotTripped;
        /// <summary>Тиков в зонах дистанции до быка (ViewerZone: горячо, тепло, прохладно, холодно) до финиша места.</summary>
        public readonly int[] ZoneTicks = new int[4];
    }

    /// <summary>Метрики одного забега. Длительности в тиках (30/с), в секунды переводит вывод.</summary>
    public sealed class RunRecord
    {
        public ulong Seed;
        public bool Finished;
        public int DurationTicks;
        public int WinnerSeat;
        public SeatRecord[] Seats = new SeatRecord[RaceConfig.SeatCount];
        /// <summary>Смены лидера по донатам после первых 30 с забега / за весь забег.</summary>
        public int LeaderChanges, LeaderChangesTotal;
        /// <summary>Самая долгая пауза между событиями ядра (кроме подсказок и смены зоны), тиков.</summary>
        public int LongestQuietTicks;
        public int DonationsFirst, DonationsSecond;
        public string Chronicle = "";
    }

    /// <summary>
    /// Прогон одного забега 4 ботов. Метрики и хроника строятся из событий ядра (RaceEvents) и снимков состояния
    /// после каждого Step, внутрь систем ядра не лезем. Состав мест выводится из сида: персонажи — перестановка,
    /// характеры — по кругу со случайным сдвигом, так что доли побед считаются «на появление».
    /// </summary>
    public static class RaceRecorder
    {
        const int Tps = Simulation.TicksPerSecond;
        const int SnapshotEvery = 5 * Tps;
        const int LeaderGraceTicks = 30 * Tps; // design/overview.md, «Открытое»: смены лидера без первых 30 с
        const int MaxTicks = 20 * 60 * Tps;    // предохранитель: нормальный забег ≈ 5 минут

        public static RaceConfig MakeConfig(ulong seed, GameData data)
        {
            var rng = new DetRandom(seed ^ 0x5EA7C0F16UL, 77);
            int nc = data.CharacterData.Characters.Length, nt = data.BotData.Temperaments.Length;
            var chars = new int[RaceConfig.SeatCount];
            for (int i = 0; i < chars.Length; i++) chars[i] = i % nc;
            for (int i = chars.Length - 1; i > 0; i--) { int j = rng.Range(0, i + 1); (chars[i], chars[j]) = (chars[j], chars[i]); }
            var temps = new int[RaceConfig.SeatCount];
            int offset = rng.Range(0, nt);
            for (int i = 0; i < temps.Length; i++) temps[i] = (i + offset) % nt;
            for (int i = temps.Length - 1; i > 0; i--) { int j = rng.Range(0, i + 1); (temps[i], temps[j]) = (temps[j], temps[i]); }
            var seats = new SeatConfig[RaceConfig.SeatCount];
            for (int i = 0; i < seats.Length; i++) seats[i] = new SeatConfig(SeatKind.Bot, chars[i], temps[i]);
            return new RaceConfig(seed, seats);
        }

        public static RunRecord Run(ulong seed, GameData data)
        {
            var cfg = MakeConfig(seed, data);
            var sim = new Simulation(cfg, data);
            var st = sim.State;
            var rec = new RunRecord { Seed = seed };
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                var sc = cfg.GetSeat(i);
                rec.Seats[i] = new SeatRecord
                {
                    Seat = i, CharacterId = sc.CharacterId, TemperamentId = sc.BotTemperament,
                    Character = data.CharacterData.Get(sc.CharacterId).Id, Temperament = data.BotData.Get(sc.BotTemperament).Id,
                };
            }

            var sb = new StringBuilder();
            sb.Append("# Забег, сид ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append('\n').Append('\n');
            sb.Append("Места: ");
            for (int i = 0; i < RaceConfig.SeatCount; i++) sb.Append(i > 0 ? ", " : "").Append(i + 1).Append(" — ").Append(Label(rec, i));
            sb.Append('\n').Append('\n');

            int startTick = -1, finishTick = -1, lastQuiet = 0, leader = -1;
            var order = new int[RaceConfig.SeatCount];
            var events = sim.Events;
            for (int n = 0; n < MaxTicks && st.Phase != RacePhase.Over; n++)
            {
                int stepTick = st.Tick;
                sim.Step(TickInputs.Empty);
                for (int e = 0; e < events.Count; e++)
                {
                    var ev = events[e];
                    if (ev.Type == RaceEventType.RaceStart) { startTick = ev.Tick; lastQuiet = ev.Tick; leader = -1; }
                    if (startTick < 0) continue;
                    Count(rec, ev);
                    if (IsQuietBreaker(ev.Type))
                    {
                        if (ev.Tick - lastQuiet > rec.LongestQuietTicks) rec.LongestQuietTicks = ev.Tick - lastQuiet;
                        lastQuiet = ev.Tick;
                    }
                    if (ev.Type == RaceEventType.RaceFinish) finishTick = ev.Tick;
                    Line(sb, sim, rec, ev, startTick, events, e);
                }
                if (startTick < 0) continue;

                int rel = stepTick - startTick;
                for (int i = 0; i < RaceConfig.SeatCount; i++)
                {
                    ref var v = ref st.Runners[i].Viewing;
                    if (v.FinishTick < 0 && v.Zone >= 0 && stepTick >= startTick) rec.Seats[i].ZoneTicks[v.Zone]++;
                }
                int now = LeaderOf(st, leader);
                if (now != leader)
                {
                    if (leader >= 0)
                    {
                        rec.LeaderChangesTotal++;
                        if (rel >= LeaderGraceTicks) rec.LeaderChanges++;
                    }
                    leader = now;
                }
                if (rel > 0 && rel % SnapshotEvery == 0 && st.Phase != RacePhase.Over) Snapshot(sb, sim, rec, rel, order);
            }

            rec.Finished = st.Phase == RacePhase.Over && finishTick >= 0;
            if (finishTick < 0) finishTick = st.Tick;
            if (startTick < 0) startTick = finishTick;
            rec.DurationTicks = finishTick - startTick;
            if (finishTick - lastQuiet > rec.LongestQuietTicks) rec.LongestQuietTicks = finishTick - lastQuiet;

            Results.Standings(st, order);
            rec.WinnerSeat = order[0];
            for (int p = 0; p < RaceConfig.SeatCount; p++) rec.Seats[order[p]].Place = p + 1;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
            {
                rec.Seats[i].Donations = Viewers.DonationsOf(st, i);
                rec.Seats[i].Viewers = Viewers.ViewersOf(st, i);
            }
            rec.DonationsFirst = rec.Seats[order[0]].Donations;
            rec.DonationsSecond = rec.Seats[order[1]].Donations;
            Summary(sb, rec, order);
            rec.Chronicle = sb.ToString();
            return rec;
        }

        /// <summary>Подсказки чата (их ещё нет в ядре) и смена зоны паузу не прерывают (design/overview.md, «Открытое»).</summary>
        static bool IsQuietBreaker(RaceEventType t) =>
            t != RaceEventType.ZoneChanged && t != RaceEventType.RaceStart && t != RaceEventType.None;

        static void Count(RunRecord rec, RaceEvent ev)
        {
            if (ev.Seat < 0 || ev.Seat >= RaceConfig.SeatCount) return;
            var s = rec.Seats[ev.Seat];
            switch (ev.Type)
            {
                case RaceEventType.Tossed: s.Tosses++; break;
                case RaceEventType.Collision: s.Collisions++; break;
                case RaceEventType.Shoved: s.Shoves++; break;
                case RaceEventType.Tripped: s.Trips++; break;
                case RaceEventType.GotShoved: s.GotShoved++; break;
                case RaceEventType.GotTripped: s.GotTripped++; break;
            }
        }

        /// <summary>Лидер по донатам: прежний лидер остаётся, пока кто-то не обгонит его строго; на старте — максимум (равенство — меньшее место).</summary>
        static int LeaderOf(RaceState st, int current)
        {
            int best = current < 0 ? 0 : current;
            for (int i = 0; i < RaceConfig.SeatCount; i++)
                if (st.Runners[i].Viewing.Donations > st.Runners[best].Viewing.Donations) best = i;
            return best;
        }

        // ---------- хроника ----------

        static string Label(RunRecord rec, int seat) =>
            Names.Character(rec.Seats[seat].Character) + " (" + Names.Temperament(rec.Seats[seat].Temperament) + ")";

        static string Clock(int ticks)
        {
            int sec = ticks / Tps;
            return (sec / 60).ToString(CultureInfo.InvariantCulture) + ":" + (sec % 60).ToString("D2", CultureInfo.InvariantCulture);
        }

        static string BullDist(Simulation sim, int seat)
        {
            var st = sim.State;
            var fs = Fixed.FromRaw(st.Runners[seat].S - st.Bull.S);
            var fx = Fixed.FromRaw(st.Runners[seat].X - st.Bull.X);
            int d = Fixed.Sqrt(fs * fs + fx * fx).ToInt();
            return d.ToString(CultureInfo.InvariantCulture);
        }

        static string Verb(RunRecord rec, int seat, string masc, string fem) => Names.Feminine(rec.Seats[seat].Character) ? fem : masc;

        static void Line(StringBuilder sb, Simulation sim, RunRecord rec, RaceEvent ev, int startTick, RaceEvents events, int index)
        {
            string text;
            int seat = ev.Seat;
            string bonus = "";
            switch (ev.Type)
            {
                case RaceEventType.RaceStart:
                    sb.Append(Clock(0)).Append(" Старт: бегуны побежали\n");
                    return;
                case RaceEventType.Collision:
                    bonus = ev.Param1 == (int)Game.Core.Runners.CollisionKind.Water ? "+40 зрителей, +80 донатов" : "+" + EventBonuses.CollisionViewers + " зрителей";
                    text = Label(rec, seat) + " " + Verb(rec, seat, "врезался", "врезалась") + " в " + Names.Obstacle(ev.Param1);
                    break;
                case RaceEventType.Tossed:
                    bonus = "+" + EventBonuses.TossViewers + " зрителей, +" + EventBonuses.TossDonations + " донатов";
                    text = "Бык подбросил: " + Label(rec, seat);
                    break;
                case RaceEventType.Rose:
                    text = Label(rec, seat) + " " + Verb(rec, seat, "встал", "встала") + ": сигнал вернулся";
                    break;
                case RaceEventType.Shoved:
                    bonus = "+" + EventBonuses.ShoveViewers + " зрителей, цель " + EventBonuses.GotShovedViewers + " зрителей";
                    text = Label(rec, seat) + " " + Verb(rec, seat, "толкнул", "толкнула") + " " + Names.CharacterAcc(rec.Seats[ev.Target].Character);
                    break;
                case RaceEventType.Tripped:
                    bonus = "+" + EventBonuses.TripViewers + " зрителей";
                    text = Label(rec, seat) + " " + Verb(rec, seat, "поставил", "поставила") + " подножку " + Dative(rec.Seats[ev.Target].Character);
                    break;
                case RaceEventType.LaidOut:
                    // подножка уже описана строкой «Tripped» того же тика; здесь — только толчок в препятствие
                    for (int k = 0; k < events.Count; k++)
                        if (events[k].Type == RaceEventType.Tripped && events[k].Seat == ev.Seat && events[k].Target == ev.Target) return;
                    text = Label(rec, seat) + " " + Verb(rec, seat, "уложил", "уложила") + " " + Names.CharacterAcc(rec.Seats[ev.Target].Character) + " толчком";
                    break;
                case RaceEventType.FramedIn:
                    bonus = "+" + Fixed.FromRaw(ev.Param1).ToInt().ToString(CultureInfo.InvariantCulture) + " зрителей/с";
                    text = Label(rec, seat) + " " + Verb(rec, seat, "влез", "влезла") + " в кадр: " + Label(rec, ev.Target);
                    break;
                case RaceEventType.ViewerThreshold:
                    text = Label(rec, seat) + ": рекорд " + ev.Param1.ToString(CultureInfo.InvariantCulture) + " зрителей";
                    break;
                case RaceEventType.FinishSoon:
                    sb.Append(Clock(ev.Tick - startTick)).Append(' ').Append(Label(rec, seat)).Append(' ')
                      .Append(Verb(rec, seat, "вбежал", "вбежала")).Append(" на арену: до конца 15 с\n");
                    return;
                case RaceEventType.RaceFinish:
                    sb.Append(Clock(ev.Tick - startTick)).Append(" Финиш: забег закрыт\n");
                    return;
                default:
                    return; // GotShoved/GotTripped/InYourFrame/ZoneChanged описаны строкой второго участника
            }
            sb.Append(Clock(ev.Tick - startTick)).Append(' ').Append(text);
            if (bonus.Length > 0) sb.Append(": ").Append(bonus);
            sb.Append("; бык в ").Append(BullDist(sim, seat)).Append(" м от ").Append(Names.CharacterGen(rec.Seats[seat].Character)).Append('\n');
        }

        static string Dative(string id) => id switch
        {
            "glamour" => "Гламур",
            "athlete" => "Спортсмену",
            "grandma" => "Бабуле",
            "nerd" => "Ботанику",
            _ => id,
        };

        static void Snapshot(StringBuilder sb, Simulation sim, RunRecord rec, int rel, int[] order)
        {
            var st = sim.State;
            Results.Standings(st, order);
            sb.Append("--- ").Append(Clock(rel)).Append(" | места: ");
            for (int p = 0; p < RaceConfig.SeatCount; p++)
                sb.Append(p > 0 ? ", " : "").Append(p + 1).Append(' ').Append(Names.Character(rec.Seats[order[p]].Character));
            sb.Append(" | до быка, м: ");
            for (int p = 0; p < RaceConfig.SeatCount; p++)
                sb.Append(p > 0 ? ", " : "").Append(Names.Character(rec.Seats[order[p]].Character)).Append(' ').Append(BullDist(sim, order[p]));
            sb.Append(" | зрители: ");
            for (int p = 0; p < RaceConfig.SeatCount; p++)
                sb.Append(p > 0 ? ", " : "").Append(Viewers.ViewersOf(st, order[p]));
            sb.Append(" | донаты: ");
            for (int p = 0; p < RaceConfig.SeatCount; p++)
                sb.Append(p > 0 ? ", " : "").Append(Viewers.DonationsOf(st, order[p]));
            sb.Append(" | с, дистанция: ");
            for (int p = 0; p < RaceConfig.SeatCount; p++)
                sb.Append(p > 0 ? ", " : "").Append(Fixed.FromRaw(st.Runners[order[p]].S).ToInt());
            sb.Append('\n');
        }

        static void Summary(StringBuilder sb, RunRecord rec, int[] order)
        {
            sb.Append('\n').Append("## Итог\n\n");
            sb.Append(rec.Finished ? "Забег закрыт" : "Забег НЕ закрыт (предохранитель по времени)").Append(", длительность ")
              .Append(Clock(rec.DurationTicks)).Append(". Победитель: ").Append(Label(rec, rec.WinnerSeat)).Append(".\n\n");
            sb.Append("| Место | Персонаж (характер) | Донаты | Зрители | Подбросы | Столкновения | Толчки | Подножки |\n");
            sb.Append("|---|---|---|---|---|---|---|---|\n");
            for (int p = 0; p < RaceConfig.SeatCount; p++)
            {
                var s = rec.Seats[order[p]];
                sb.Append("| ").Append(p + 1).Append(" | ").Append(Label(rec, s.Seat)).Append(" | ").Append(s.Donations).Append(" | ").Append(s.Viewers)
                  .Append(" | ").Append(s.Tosses).Append(" | ").Append(s.Collisions).Append(" | ").Append(s.Shoves).Append(" | ").Append(s.Trips).Append(" |\n");
            }
            sb.Append('\n').Append("Смены лидера по донатам: ").Append(rec.LeaderChanges).Append(" (после 30 с), ").Append(rec.LeaderChangesTotal)
              .Append(" (всего). Самая долгая пауза без событий: ").Append(Fmt.Tenths(rec.LongestQuietTicks * 10L / Tps)).Append(" с.\n");
        }
    }
}
