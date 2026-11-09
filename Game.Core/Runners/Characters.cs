using System;
using Game.Core.Data;
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
        GrandmaBag = 3,          // дальность её толчка PerkValue м (F-015); сдвиг цели не меняет
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
            var j = Json.Parse("characters.json", json, truncate: true);
            var d = new CharacterData { ShoveDistance = j.Num("shoveDistance"), ShoveSpeedLoss = j.Num("shoveSpeedLoss") };
            var arr = j.Arr("characters");
            if (arr.Count < RaceConfig.SeatCount) throw new FormatException("characters.json: меньше 4 персонажей");
            d.Characters = new CharacterDef[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                var o = arr.Obj(i);
                d.Characters[i] = new CharacterDef
                {
                    Id = o.Str("id"), Speed = o.Num("speed"), Stability = o.Num("stability"),
                    Viewers = o.Num("viewers"), Tastiness = o.Num("tastiness"), NoSignal = o.Num("noSignal"),
                    PerkValue = o.Num("perkValue"), Perk = ParsePerk(o.Str("perk")),
                };
            }
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

        /// <summary>Сдвиг цели толчком по x, м: shoveDistance / устойчивость цели, от толкающего не зависит (сумка бабули — дальность, F-015).</summary>
        public static Fixed ShoveDistance(CharacterData d, Fixed targetStability)
        {
            if (targetStability <= Fixed.Zero) targetStability = Fixed.One;
            return d.ShoveDistance / targetStability;
        }

        /// <summary>Толчок: цель сдвигается на dir (−1 влево, +1 вправо) × ShoveDistance, теряет долю скорости. Вызывает механика толчка.</summary>
        public static void ApplyShove(CharacterData d, RunnerData rd, StreetMap street, RaceState st, CharacterDef pusher, int target, int dir)
        {
            ref RunnerState r = ref st.Runners[target];
            Fixed dist = ShoveDistance(d, Fixed.FromRaw(st.Stability[target]));
            Fixed x = Fixed.FromRaw(r.X) + Fixed.FromInt(dir < 0 ? -1 : 1) * dist;
            r.X = RunnerMovement.ClampToStreet(rd, street, Fixed.FromRaw(r.S), x).Raw;
            r.Speed = (Fixed.FromRaw(r.Speed) * (Fixed.One - d.ShoveSpeedLoss)).Raw;
        }
    }
}
