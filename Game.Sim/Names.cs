using Game.Core.Runners;

namespace Game.Sim
{
    /// <summary>Русские подписи для хроники. Только вывод, на игру не влияют; ключ — id из characters.json и bots.json.</summary>
    public static class Names
    {
        public static string Character(string id) => id switch
        {
            "glamour" => "Гламур",
            "athlete" => "Спортсмен",
            "grandma" => "Бабуля",
            "nerd" => "Ботаник",
            _ => id,
        };

        /// <summary>Женский род глагола: Гламур и Бабуля.</summary>
        public static bool Feminine(string characterId) => characterId == "glamour" || characterId == "grandma";

        public static string Temperament(string id) => id switch
        {
            "coward" => "трус",
            "adrenaline" => "адреналинщик",
            "troll" => "тролль",
            _ => id,
        };

        /// <summary>Винительный падеж препятствия: «врезался в …».</summary>
        public static string Obstacle(int kind) => (CollisionKind)kind switch
        {
            CollisionKind.Pole => "столб",
            CollisionKind.Stall => "лоток",
            CollisionKind.Barrels => "бочки",
            CollisionKind.Laundry => "бельё",
            CollisionKind.Door => "дверь",
            CollisionKind.Pots => "горшки",
            CollisionKind.Gawker => "зеваку",
            CollisionKind.Fountain => "фонтан",
            CollisionKind.Water => "пруд",
            _ => "препятствие",
        };

        /// <summary>Родительный падеж: «бык в 6 м от Бабули».</summary>
        public static string CharacterGen(string id) => id switch
        {
            "glamour" => "Гламур",
            "athlete" => "Спортсмена",
            "grandma" => "Бабули",
            "nerd" => "Ботаника",
            _ => id,
        };

        /// <summary>Винительный падеж персонажа: «толкнул Спортсмена».</summary>
        public static string CharacterAcc(string id) => id switch
        {
            "glamour" => "Гламур",
            "athlete" => "Спортсмена",
            "grandma" => "Бабулю",
            "nerd" => "Ботаника",
            _ => id,
        };
    }
}
