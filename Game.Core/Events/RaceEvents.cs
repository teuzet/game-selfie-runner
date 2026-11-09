using Game.Core.Math;

namespace Game.Core.Events
{
    /// <summary>Тип события тика. Param1/Param2 — см. комментарий у каждого типа.</summary>
    public enum RaceEventType : byte
    {
        None = 0,
        Collision,     // Seat; Param1 = CollisionKind (тип препятствия; Water — пруд)
        Tossed,        // Seat подброшен быком; Param1 = TossCause (1 — рывок, 2 — контакт без рывка; F-100)
        Rose,          // Seat встал (кончилось «нет сигнала»)
        Shoved,        // Seat толкнул Target
        GotShoved,     // Seat толкнули; Target — кто
        Tripped,       // Seat сбил подножкой Target
        GotTripped,    // Seat упал от подножки; Target — кто
        LaidOut,       // Seat уложил Target (подножка или толчок в препятствие)
        FramedIn,      // Seat влез в кадр Target; Param1 = зрителей/с (Fixed raw)
        InYourFrame,   // у Seat в кадре Target; Param1 = зрителей/с (Fixed raw), с учётом перка жертвы
        ZoneChanged,   // Seat: Param1 = прежняя зона, Param2 = новая (ViewerZone)
        ViewerThreshold, // Seat: Param1 = порог (250, 500, ...), каждый порог один раз — рекорд
        RaceStart,     // «0» отсчёта: бегуны побежали (Seat = -1)
        FinishSoon,    // первый вбежал на арену, пошли 15 с; Seat — он
        RaceFinish,    // забег закрыт; Seat — победитель (итоги в RaceState)
        Hint,          // подсказка чата Seat (F-021): Param1 = CollisionKind препятствия | тиков до << 8 (ChatHints.TypeOf/TicksOf), Param2 = сторона −1/0/+1 с учётом шума
        BullCharge,    // бык рвётся на Seat: тик начала рывка, ровно одно событие на рывок (F-072; F-100 §3.2 п.1)
    }

    public struct RaceEvent
    {
        public RaceEventType Type;
        public int Tick;    // тик, в котором событие произошло (Simulation.Tick до инкремента внутри Step)
        public int Seat;    // главное место
        public int Target;  // второе место, -1 — нет
        public int Param1, Param2;
    }

    /// <summary>События последнего Step. Буфер выделен заранее, Add не аллоцирует; очищается в начале каждого Step.</summary>
    public sealed class RaceEvents
    {
        public const int Capacity = 128;
        private readonly RaceEvent[] _items = new RaceEvent[Capacity];

        public int Count { get; private set; }
        /// <summary>Событий не поместилось в буфер за последний Step (должно быть 0).</summary>
        public int Dropped { get; private set; }

        public RaceEvent this[int i] => _items[i];

        public void Clear() { Count = 0; Dropped = 0; }

        public void Add(RaceEventType type, int tick, int seat, int target = -1, int p1 = 0, int p2 = 0)
        {
            if (Count >= Capacity) { Dropped++; return; }
            ref RaceEvent e = ref _items[Count++];
            e.Type = type; e.Tick = tick; e.Seat = seat; e.Target = target; e.Param1 = p1; e.Param2 = p2;
        }

        public int CountOf(RaceEventType type)
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (_items[i].Type == type) n++;
            return n;
        }

        public void CopyFrom(RaceEvents o)
        {
            System.Array.Copy(o._items, _items, Capacity);
            Count = o.Count; Dropped = o.Dropped;
        }
    }

    /// <summary>Что событийный сканер уже видел по месту (счётчики фактов). Входит в RaceState и хэш.</summary>
    public struct EventState
    {
        public int SeenCollisions, SeenWater, SeenTossed, SeenRose, SeenShoved, SeenTripped,
            SeenGotShoved, SeenGotTripped, SeenZoneChanges;
        public int LaidStamp;   // LastGotShovedTick+1 толчка, за который уже засчитано «уложил»; 0 — нет
        public int Level;       // максимальный достигнутый порог зрителей в шагах по 250 (0 — ни одного)

        public ulong Hash(ulong h)
        {
            h = Math.Hashing.Mix(h, (ulong)(uint)SeenCollisions); h = Math.Hashing.Mix(h, (ulong)(uint)SeenWater);
            h = Math.Hashing.Mix(h, (ulong)(uint)SeenTossed); h = Math.Hashing.Mix(h, (ulong)(uint)SeenRose);
            h = Math.Hashing.Mix(h, (ulong)(uint)SeenShoved); h = Math.Hashing.Mix(h, (ulong)(uint)SeenTripped);
            h = Math.Hashing.Mix(h, (ulong)(uint)SeenGotShoved); h = Math.Hashing.Mix(h, (ulong)(uint)SeenGotTripped);
            h = Math.Hashing.Mix(h, (ulong)(uint)SeenZoneChanges); h = Math.Hashing.Mix(h, (ulong)(uint)LaidStamp);
            return Math.Hashing.Mix(h, (ulong)(uint)Level);
        }
    }
}
