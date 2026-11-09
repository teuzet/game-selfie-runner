using System;
using Game.Core.Data;
using Game.Core.Math;

namespace Game.Core.Bots
{
    /// <summary>Один характер бота — строка таблицы design/F-005.md §3 (Data/bots.json).</summary>
    public sealed class BotDef
    {
        public string Id = "";
        public Fixed BandMin, BandMax, SprintBelow, Hysteresis, SprintUntilStamina, ReserveStamina, HuntRadius;
        public int TempoPeriod, ActionPeriod, ReactionMin, ReactionMax, WaitNeighbor, PushMin, PushMax;
        public Fixed TrustHints, TrustNeighbors, Aggression;
        public bool DodgeRush;
        public string[] Quests = new string[0];

        /// <summary>Берёт ли бот квест этого типа (F-005 §3, строка «квесты»).</summary>
        public bool Takes(string quest)
        {
            for (int i = 0; i < Quests.Length; i++) if (Quests[i] == quest) return true;
            return false;
        }
    }

    /// <summary>Data/bots.json: характеры и общие числа решения бота. Пустой набор (BotData.None) — боты стоят на месте без мозга (старые тесты без bots.json).</summary>
    public sealed class BotData
    {
        public BotDef[] Temperaments = new BotDef[0];
        public Fixed DeadZone, WallMargin, DodgeDiv, CenterDodgeMargin, SideFrame, RearFrame, NeighborReact,
            NeighborSeparation, RushSidestep, PassMargin, MarketAxis, PlazaExtra, PondEdge, LaneSwitch, ShiftEps;
        public int AheadMemory, NeighborHold;

        public static BotData None() => new BotData();

        public BotDef Get(int temperament)
        {
            if (temperament < 0 || temperament >= Temperaments.Length)
                throw new ArgumentOutOfRangeException(nameof(temperament), "bots.json: нет характера " + temperament);
            return Temperaments[temperament];
        }

        public static BotData Parse(string json)
        {
            var j = Json.Parse("bots.json", json);
            var c = j.Obj("common");
            var t = j.Arr("temperaments");
            var d = new BotData
            {
                DeadZone = c.Num("deadZone"), WallMargin = c.Num("wallMargin"), DodgeDiv = c.Num("dodgeDiv"),
                CenterDodgeMargin = c.Num("centerDodgeMargin"), SideFrame = c.Num("sideFrame"), RearFrame = c.Num("rearFrame"),
                NeighborReact = c.Num("neighborReact"), NeighborSeparation = c.Num("neighborSeparation"),
                RushSidestep = c.Num("rushSidestep"), PassMargin = c.Num("passMargin"), MarketAxis = c.Num("marketAxis"),
                PlazaExtra = c.Num("plazaExtra"), PondEdge = c.Num("pondEdge"), LaneSwitch = c.Num("laneSwitch"),
                ShiftEps = c.Num("shiftEps"), AheadMemory = c.Int("aheadMemory"), NeighborHold = c.Int("neighborHold"),
                Temperaments = new BotDef[t.Count],
            };
            for (int i = 0; i < t.Count; i++)
            {
                var o = t.Obj(i);
                var band = o.Arr("band");
                var react = o.Arr("reaction");
                var push = o.Arr("pushWindow");
                d.Temperaments[i] = new BotDef
                {
                    Id = o.Str("id"),
                    BandMin = band.Num(0), BandMax = band.Num(1),
                    SprintBelow = o.Num("sprintBelow"), Hysteresis = o.Num("hysteresis"),
                    SprintUntilStamina = o.Num("sprintUntilStamina"), ReserveStamina = o.Num("reserveStamina"),
                    TempoPeriod = o.Int("tempoPeriod"), ActionPeriod = o.Int("actionPeriod"),
                    ReactionMin = react.Num(0).ToInt(), ReactionMax = react.Num(1).ToInt(),
                    TrustHints = o.Num("trustHints"), TrustNeighbors = o.Num("trustNeighbors"),
                    WaitNeighbor = o.Int("waitNeighbor"), Aggression = o.Num("aggression"), HuntRadius = o.Num("huntRadius"),
                    PushMin = push.Num(0).ToInt(), PushMax = push.Num(1).ToInt(),
                    DodgeRush = o.Bool("dodgeRush"), Quests = o.StrList("quests"),
                };
            }
            return d;
        }
    }
}
