using System;
using System.Globalization;
using Game.Core.Math;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Runners
{
    /// <summary>Перк персонажа. Спортсмена и бабулю применяет это ядро движения; остальных читают F-015, F-018, F-021 по enum.</summary>
    public enum Perk : byte
    {
        None = 0,
        GlamourLoyalViewers = 1, // «влез в кадр» тянет зрителей в 1/PerkValue раз меньше (F-015)
        AthleteEndurance = 2,    // восстановление выносливости × PerkValue (здесь)
        GrandmaBag = 3,          // её толчок бьёт на PerkValue м (здесь)
        NerdModerator = 4,       // подсказки чата без шума (F-021)
    }

    /// <summary>Набор параметров персонажа. Индекс в CharacterData.Characters = SeatConfig.CharacterId.</summary>
    public sealed class CharacterDef
    {
        public string Id = "";
        public Fixed Speed, Stability, Viewers, Tastiness, NoSignal, PerkValue;
        public Perk Perk;
    }

    /// <summary>Data/characters.json.</summary>
    public sealed class CharacterData
    {
        public CharacterDef[] Characters = new CharacterDef[0];
        public Fixed ShoveDistance, ShoveSpeedLoss;

        public CharacterDef Get(int characterId)
        {
            if (characterId < 0 || characterId >= Characters.Length)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            return Characters[characterId];
        }

        /// <summary>Нейтральные 4 персонажа (всё ×1): для GameData.Parse без characters.json.</summary>
        public static CharacterData Neutral()
        {
            var d = new CharacterData { ShoveDistance = Fixed.FromInt(2), ShoveSpeedLoss = Fixed.FromFraction(1, 4) };
            d.Characters = new CharacterDef[4];
            for (int i = 0; i < 4; i++)
                d.Characters[i] = new CharacterDef
                {
                    Id = "neutral" + i, Speed = Fixed.One, Stability = Fixed.One, Viewers = Fixed.One,
                    Tastiness = Fixed.One, NoSignal = Fixed.FromInt(3),
                };
            return d;
        }

        public static CharacterData Parse(string json)
        {
            var d = new CharacterData { ShoveDistance = Num(json, "shoveDistance"), ShoveSpeedLoss = Num(json, "shoveSpeedLoss") };
            int arr = json.IndexOf("\"characters\"", StringComparison.Ordinal);
            if (arr < 0) throw new FormatException("characters.json: нет characters");
            var list = new System.Collections.Generic.List<CharacterDef>();
            int p = json.IndexOf('[', arr);
            while (true)
            {
                int o = json.IndexOf('{', p);
                int end = json.IndexOf(']', p);
                if (o < 0 || (end >= 0 && end < o)) break;
                int c = json.IndexOf('}', o);
                string obj = json.Substring(o, c - o + 1);
                list.Add(new CharacterDef
                {
                    Id = Str(obj, "id"), Speed = Num(obj, "speed"), Stability = Num(obj, "stability"),
                    Viewers = Num(obj, "viewers"), Tastiness = Num(obj, "tastiness"), NoSignal = Num(obj, "noSignal"),
                    PerkValue = Num(obj, "perkValue"), Perk = ParsePerk(Str(obj, "perk")),
                });
                p = c + 1;
            }
            if (list.Count < RaceConfig.SeatCount) throw new FormatException("characters.json: меньше 4 персонажей");
            d.Characters = list.ToArray();
            return d;
        }

        static Perk ParsePerk(string s)
        {
            switch (s)
            {
                case "glamourLoyalViewers": return Perk.GlamourLoyalViewers;
                case "athleteEndurance": return Perk.AthleteEndurance;
                case "grandmaBag": return Perk.GrandmaBag;
                case "nerdModerator": return Perk.NerdModerator;
                case "none": return Perk.None;
                default: throw new FormatException("characters.json: неизвестный перк " + s);
            }
        }

        static string Str(string json, string key)
        {
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) throw new FormatException("characters.json: нет ключа " + key);
            int a = json.IndexOf('"', json.IndexOf(':', k) + 1) + 1;
            return json.Substring(a, json.IndexOf('"', a) - a);
        }

        static Fixed Num(string json, string key)
        {
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) throw new FormatException("characters.json: нет ключа " + key);
            int p = json.IndexOf(':', k) + 1;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            int e = p;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '.' || json[e] == '-')) e++;
            string t = json.Substring(p, e - p);
            if (t.Length == 0) throw new FormatException("characters.json: ключ " + key + " не число");
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
                v = v + Fixed.FromRaw((long.Parse(fp, CultureInfo.InvariantCulture) * Fixed.OneRaw) / den);
            }
            return neg ? -v : v;
        }
    }

    /// <summary>Применение персонажа: числа движения на место и толчок.</summary>
    public static class Characters
    {
        /// <summary>RunnerData места: скорости × скорость персонажа, восстановление × перк спортсмена. Считается один раз.</summary>
        public static RunnerData ForSeat(RunnerData b, CharacterDef c)
        {
            Fixed regen = c.Perk == Perk.AthleteEndurance ? b.StaminaRegen * c.PerkValue : b.StaminaRegen;
            return new RunnerData
            {
                JogSpeed = b.JogSpeed * c.Speed, RunSpeed = b.RunSpeed * c.Speed, SprintSpeed = b.SprintSpeed * c.Speed,
                StaminaMax = b.StaminaMax, SprintDrain = b.SprintDrain, StaminaRegen = regen, SprintResume = b.SprintResume,
                Accel = b.Accel, SteerSpeed = b.SteerSpeed, Radius = b.Radius, Separation = b.Separation,
            };
        }

        /// <summary>Сдвиг цели толчком по x, м: дальность толчка (у бабули — сумка) / устойчивость цели.</summary>
        public static Fixed ShoveDistance(CharacterData d, CharacterDef pusher, Fixed targetStability)
        {
            Fixed dist = pusher.Perk == Perk.GrandmaBag ? pusher.PerkValue : d.ShoveDistance;
            if (targetStability <= Fixed.Zero) targetStability = Fixed.One;
            return dist / targetStability;
        }

        /// <summary>Толчок: цель сдвигается на dir (−1 влево, +1 вправо) × ShoveDistance, теряет долю скорости. Вызывает механика толчка.</summary>
        public static void ApplyShove(CharacterData d, RunnerData rd, StreetMap street, RaceState st, CharacterDef pusher, int target, int dir)
        {
            ref RunnerState r = ref st.Runners[target];
            Fixed dist = ShoveDistance(d, pusher, Fixed.FromRaw(st.Stability[target]));
            Fixed x = Fixed.FromRaw(r.X) + Fixed.FromInt(dir < 0 ? -1 : 1) * dist;
            r.X = RunnerMovement.ClampToStreet(rd, street, Fixed.FromRaw(r.S), x).Raw;
            r.Speed = (Fixed.FromRaw(r.Speed) * (Fixed.One - d.ShoveSpeedLoss)).Raw;
        }
    }
}
