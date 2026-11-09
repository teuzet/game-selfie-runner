// Собирает design/chat/*.json из brainstorm/raw/*.txt и проверяет минимумы F-004.
// Запуск из корня репозитория (.NET 10, file-based app):
//   dotnet run brainstorm/build_chat.cs            — собрать и проверить
//   dotnet run brainstorm/build_chat.cs -- --check — только проверить готовые JSON
//
// Формат сырья (brainstorm/raw/<повод>.txt):
//   # комментарий
//   [ru role=actor quest=crash obstacle=pole side=left]  — секция: язык + фильтры
//   s2|текст фразы                                       — mood (s/g/t/h) + вес (по умолчанию 1)
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string Raw = Path.Combine("brainstorm", "raw");
string Out = Path.Combine("design", "chat");
if (!Directory.Exists(Raw)) { Console.Error.WriteLine("запускать из корня репозитория"); return 2; }

var moods = new Dictionary<char, string> { ['s'] = "support", ['g'] = "gloat", ['t'] = "troll", ['h'] = "hint" };
string[] langs = { "ru", "en" };
string[] filterKeys = { "role", "quest", "obstacle", "side" };
var placeholders = new HashSet<string> { "name", "obstacle", "side" };
string[] banned = { "twitch", "youtube", "tiktok", "instagram", "iphone", "android", "samsung", "nike", "adidas",
    "coca", "pepsi", "mcdonald", "red bull", "redbull", "discord", "твич", "ютуб", "тикток", "инстаграм", "айфон" };

// Таблица «Поводы» из design/F-004.md. Доли: support, gloat, troll, emoji, spam (%).
var waves = new (string occ, string? role, double peak, double hold, double decay, int[] sh, string spam, string[] tags, int prio, string trig)[] {
    ("collision", null, 4, 1.0, 2.0, new[]{20,30,20,20,10}, "😂", new[]{"laugh","pain","shock"}, 65, "бегун врезался в препятствие или упал в пруд"),
    ("tossed", null, 6, 1.5, 3.0, new[]{30,20,10,15,25}, "💀", new[]{"pain","shock","laugh","fear"}, 90, "бык подбросил бегуна, старт «нет сигнала»"),
    ("got_up", null, 3, 0.5, 1.5, new[]{50,10,15,20,5}, "💪", new[]{"win","love"}, 50, "«нет сигнала» кончилось, управление вернулось"),
    ("push", "actor", 3, 0.5, 1.5, new[]{15,40,20,15,10}, "😈", new[]{"laugh","win"}, 60, "бегун толкнул соседа"),
    ("push", "victim", 2, 0.5, 1.0, new[]{40,20,20,20,0}, "", new[]{"shock","pain"}, 60, "бегуна толкнули"),
    ("trip", "victim", 4, 1.0, 2.0, new[]{35,25,20,10,10}, "😵", new[]{"pain","laugh","shock"}, 70, "бегун споткнулся о подножку"),
    ("trip", "actor", 4, 1.0, 1.5, new[]{15,45,20,10,10}, "😈", new[]{"laugh","win"}, 70, "сосед упал от подножки бегуна"),
    ("frame", "victim", 2.5, 1.0, 2.0, new[]{30,15,35,10,10}, "👋", new[]{"wave","shock"}, 55, "сосед ≥1 с в 2 м позади бегуна, начал тянуть зрителей; повтор 1 фразой каждые 5 с"),
    ("frame", "actor", 2, 0.5, 1.0, new[]{20,40,20,10,10}, "📸", new[]{"wave","laugh"}, 55, "бегун начал тянуть зрителей у соседа"),
    ("zone_hot", null, 3, 1.0, 2.0, new[]{30,10,20,20,20}, "🐂", new[]{"fear","bull","shock"}, 30, "бык ближе 8 м (выход — дальше 9 м), не чаще раза в 10 с"),
    ("zone_cold", null, 1.5, 1.0, 2.0, new[]{10,10,50,20,10}, "💤", new[]{"bored"}, 30, "бык дальше 20 м (возврат — ближе 19 м), не чаще раза в 10 с; при >40 м повтор каждые 8 с"),
    ("quest_given", null, 3, 1.0, 1.5, new[]{20,0,50,15,15}, "🔥", new[]{"shock","money","bull"}, 45, "событие «квест выдан»"),
    ("quest_expiring", null, 2, 1.0, 1.0, new[]{30,10,40,10,10}, "⏰", new[]{"time"}, 45, "до конца квеста 5 с"),
    ("quest_done", null, 5, 1.0, 2.0, new[]{50,10,10,15,15}, "🎉", new[]{"win","money","love"}, 80, "событие «квест выполнен»"),
    ("quest_failed", null, 3, 1.0, 1.5, new[]{25,35,25,10,5}, "📉", new[]{"laugh","bored","pain"}, 80, "событие «квест провален»"),
    ("viewer_record", null, 3, 1.0, 2.0, new[]{50,0,20,15,15}, "👀", new[]{"win","shock","love"}, 40, "зрители впервые за забег перешли 200, 300, 500, 750, 1000, 1500, 2000"),
    ("finish_soon", null, 3, 1.5, 2.0, new[]{30,10,30,15,15}, "⏳", new[]{"time","fear"}, 35, "первый бегун вбежал на арену, пошли 15 с"),
    ("finish", null, 6, 2.0, 4.0, new[]{45,10,10,15,20}, "👏", new[]{"win","love"}, 85, "бегун финишировал (вбежал на арену или вышли 15 с)"),
};
string[] shareNames = { "support", "gloat", "troll", "emoji", "spam" };
var bgShares = new[] { 30, 10, 25, 25, 10 };
var bgTags = new[] { "laugh", "love", "bull", "wave" };

// 18 пар подсказок: тип -> стороны (полосы из design/F-003.md)
var hintPairs = new (string type, string[] sides)[] {
    ("pole", new[]{"left","center","right"}), ("stall", new[]{"left","right"}),
    ("barrels", new[]{"left","center","right"}), ("door", new[]{"left","right"}),
    ("pots", new[]{"left","center","right"}), ("gawker", new[]{"left","center","right"}),
    ("laundry", new[]{"full"}), ("stairs_zone", new[]{"zone"}),
};

if (!args.Contains("--check")) Build();
return Check();

// ---------- сборка ----------
IEnumerable<string> Lines(string name) =>
    File.ReadLines(Path.Combine(Raw, name), Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#"));

Dictionary<string, string> Header(string line)
{
    var parts = line[1..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var h = new Dictionary<string, string> { [langs.Contains(parts[0]) ? "lang" : "group"] = parts[0] };
    foreach (var p in parts.Skip(1)) { var kv = p.Split('=', 2); h[kv[0]] = kv[1]; }
    return h;
}

string S(string s)
{
    var sb = new StringBuilder("\"");
    foreach (var c in s)
        sb.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", _ when c < ' ' => $"\\u{(int)c:x4}", _ => c.ToString() });
    return sb.Append('"').ToString();
}
string Obj(IEnumerable<(string k, string v)> kv) => "{" + string.Join(", ", kv.Select(p => S(p.k) + ": " + p.v)) + "}";
string Arr(IEnumerable<string> items) => "[" + string.Join(", ", items) + "]";
string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

// Одна запись на строку: читается глазами и даёт чистый diff.
void Dump(string file, string head, string listKey, IEnumerable<string> items) =>
    File.WriteAllText(Path.Combine(Out, file),
        "{" + head + S(listKey) + ": [\n" + string.Join(",\n", items.Select(i => "    " + i)) + "\n]}\n", new UTF8Encoding(false));

void Build()
{
    Directory.CreateDirectory(Out);
    var occs = waves.Select(w => w.occ).Distinct().Append("background").Append("hint");
    foreach (var occ in occs)
    {
        var items = new List<string>();
        Dictionary<string, string>? h = null;
        foreach (var line in Lines(occ + ".txt"))
        {
            if (line.StartsWith("[")) { h = Header(line); continue; }
            var m = Regex.Match(line, @"^([sgth])(\d*)\|(.+)$");
            if (!m.Success || h == null) throw new Exception($"плохая строка в {occ}.txt: {line}");
            var kv = new List<(string, string)> { ("lang", S(h["lang"])), ("mood", S(moods[m.Groups[1].Value[0]])),
                ("w", m.Groups[2].Value == "" ? "1" : m.Groups[2].Value) };
            foreach (var k in filterKeys) if (h.TryGetValue(k, out var v)) kv.Add((k, S(v)));
            kv.Add(("text", S(m.Groups[3].Value.Trim())));
            items.Add(Obj(kv));
        }
        Dump(occ + ".json", S("occasion") + ": " + S(occ) + ", ", "phrases", items);
    }

    Dump("emoji.json", "", "reactions", Lines("emoji.txt").Select(l => l.Split('|'))
        .Select(f => Obj(new[] { ("text", S(f[0])), ("tags", Arr(f[1].Split(',').Select(S))) })));
    Dump("nicks.json", "", "nicks", Lines("nicks.txt").Select(n => Obj(new[] { ("nick", S(n)) })));

    var don = new List<string>(); Dictionary<string, string>? dh = null;
    foreach (var line in Lines("donations.txt"))
    {
        if (line.StartsWith("[")) { dh = Header(line); continue; }
        don.Add(Obj(new[] { ("lang", S(dh!["lang"])), ("reason", S(dh["reason"])), ("text", S(line)) }));
    }
    Dump("donations.json", "", "donations", don);

    var obst = new List<string>(); var sides = new List<string>(); string group = "";
    foreach (var line in Lines("words.txt"))
    {
        if (line.StartsWith("[")) { group = Header(line)["group"]; continue; }
        var f = line.Split('|');
        if (group == "obstacles")
            obst.Add("  " + S(f[0]) + ": " + Obj(new[] { ("icon", S(f[1])),
                ("ru", Obj(new[] { ("nom", S(f[2])), ("acc", S(f[3])) })), ("en", Obj(new[] { ("nom", S(f[4])) })) }));
        else sides.Add("  " + S(f[0]) + ": " + Obj(new[] { ("ru", S(f[1])), ("en", S(f[2])) }));
    }
    File.WriteAllText(Path.Combine(Out, "words.json"), "{\"obstacles\": {\n" + string.Join(",\n", obst) +
        "\n}, \"sides\": {\n" + string.Join(",\n", sides) + "\n}}\n", new UTF8Encoding(false));

    string Shares(int[] sh) => Obj(shareNames.Select((n, i) => (n, sh[i].ToString())));
    var w = waves.Select(x => Obj(new[] {
        ("occasion", S(x.occ)), ("role", x.role == null ? "null" : S(x.role)), ("trigger", S(x.trig)),
        ("peak_per_s", Num(x.peak)), ("hold_s", Num(x.hold)), ("decay_s", Num(x.decay)),
        ("shares", Shares(x.sh)), ("spam", S(x.spam)), ("emoji_tags", Arr(x.tags.Select(S))), ("priority", x.prio.ToString()) }));
    var sb = new StringBuilder("{\n");
    sb.Append("  \"source\": \"design/F-004.md, таблица «Поводы»\",\n");
    sb.Append("  \"global\": {\"viewer_k\": {\"divisor\": 500, \"min\": 0.5, \"max\": 1.5}, \"cap_per_s\": 10, \"spam_len\": [3, 6], \"no_repeat_last\": 15, \"frame_repeat_s\": 5},\n");
    sb.Append("  \"hint\": {\"priority\": 100, \"echo_count\": 2, \"echo_window_s\": 0.8, \"echo_shares\": {\"hint\": 50, \"emoji\": 50}, \"emoji_tags\": [\"warn\"], \"never_dropped\": true},\n");
    sb.Append("  \"background\": {\"divisor\": 250, \"min_per_s\": 0.2, \"max_per_s\": 3.0, \"jitter\": 0.3, \"during_wave_mul\": 0.5, \"during_no_signal_mul\": 0, " +
              "\"shares\": " + Shares(bgShares) + ", \"spam\": \"🙂\", \"emoji_tags\": " + Arr(bgTags.Select(S)) + ", \"priority\": 0},\n");
    sb.Append("  \"waves\": [\n" + string.Join(",\n", w.Select(x => "    " + x)) + "\n  ]\n}\n");
    File.WriteAllText(Path.Combine(Out, "waves.json"), sb.ToString(), new UTF8Encoding(false));
}

// ---------- проверка ----------
JsonElement Load(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, file), Encoding.UTF8)).RootElement;
string? Opt(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

int Check()
{
    var errors = new List<string>(); var report = new List<string>(); var texts = new List<string>();
    void Text(string where, string t)
    {
        texts.Add(t);
        foreach (Match m in Regex.Matches(t, @"\{([^}]*)\}"))
        {
            var parts = m.Groups[1].Value.Split(':');
            if (!placeholders.Contains(parts[0].ToLowerInvariant()) || (parts.Length > 1 && parts[1] != "acc"))
                errors.Add($"{where}: неизвестная подстановка {{{m.Groups[1].Value}}} в «{t}»");
        }
    }

    var wv = Load("waves.json"); var words = Load("words.json");
    var emoji = Load("emoji.json").GetProperty("reactions").EnumerateArray().ToList();
    var emojiTags = emoji.SelectMany(e => e.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!)).ToHashSet();

    var specs = wv.GetProperty("waves").EnumerateArray()
        .Select(x => (occ: x.GetProperty("occasion").GetString()!, role: Opt(x, "role"), sh: x.GetProperty("shares"), tags: x.GetProperty("emoji_tags")))
        .Append(("background", null, wv.GetProperty("background").GetProperty("shares"), wv.GetProperty("background").GetProperty("emoji_tags")))
        .ToList();
    foreach (var (occ, role, sh, tags) in specs)
    {
        var ph = Load(occ + ".json").GetProperty("phrases").EnumerateArray().ToList();
        foreach (var t in tags.EnumerateArray()) if (!emojiTags.Contains(t.GetString()!)) errors.Add($"{occ}: нет эмодзи с тегом {t}");
        foreach (var lang in langs)
        {
            var sel = ph.Where(p => Opt(p, "lang") == lang && Opt(p, "role") == role).ToList();
            foreach (var p in sel) Text(occ, Opt(p, "text")!);
            var label = occ + (role != null ? "/" + role : "");
            var byMood = string.Join(" ", new[] { "support", "gloat", "troll" }.Select(md => $"{md[0]}{sel.Count(p => Opt(p, "mood") == md)}"));
            report.Add($"{label,-22} {lang} {sel.Count,3}  ({byMood})");
            if (sel.Count < 20) errors.Add($"{label} {lang}: фраз {sel.Count} < 20");
            foreach (var md in new[] { "support", "gloat", "troll" })
            {
                var n = sel.Count(p => Opt(p, "mood") == md);
                if (sh.GetProperty(md).GetInt32() > 0 && n < 3) errors.Add($"{label} {lang}: mood {md} — {n} фраз < 3");
            }
        }
    }

    var hints = Load("hint.json").GetProperty("phrases").EnumerateArray().ToList();
    foreach (var p in hints) Text("hint", Opt(p, "text")!);
    int pairs = 0;
    foreach (var (type, sides) in hintPairs)
    {
        if (!words.GetProperty("obstacles").TryGetProperty(type, out _)) errors.Add($"words: нет препятствия {type}");
        foreach (var side in sides)
        {
            pairs++;
            if (!words.GetProperty("sides").TryGetProperty(side, out _)) errors.Add($"words: нет стороны {side}");
            foreach (var lang in langs)
            {
                var ok = hints.Where(p => Opt(p, "lang") == lang && (Opt(p, "obstacle") == type || Opt(p, "obstacle") == "*")
                                       && (Opt(p, "side") == side || Opt(p, "side") == "*")).ToList();
                var own = ok.Count(p => Opt(p, "obstacle") == type);
                report.Add($"hint {type,-12} {side,-6} {lang} {ok.Count,3} (своих {own})");
                if (ok.Count < 20 || own < 4) errors.Add($"hint {type}/{side} {lang}: {ok.Count} фраз, своих {own}");
            }
        }
    }
    var knownTypes = hintPairs.Select(h => h.type).Append("*").ToHashSet();
    foreach (var p in hints) if (!knownTypes.Contains(Opt(p, "obstacle")!)) errors.Add($"hint: неизвестный тип {Opt(p, "obstacle")}");

    var nicks = Load("nicks.json").GetProperty("nicks").EnumerateArray().Select(n => Opt(n, "nick")!).ToList();
    var don = Load("donations.json").GetProperty("donations").EnumerateArray().ToList();
    foreach (var d in don) Text("donations", Opt(d, "text")!);
    int donRu = don.Count(d => Opt(d, "lang") == "ru"), donEn = don.Count(d => Opt(d, "lang") == "en");
    report.Add($"emoji {emoji.Count}, nicks {nicks.Count} (уникальных {nicks.Distinct().Count()}), donations ru {donRu} / en {donEn}, пар подсказок {pairs}");
    if (emoji.Count < 80) errors.Add($"эмодзи {emoji.Count} < 80");
    if (nicks.Distinct().Count() < 60) errors.Add($"уникальных ников {nicks.Distinct().Count()} < 60");
    if (donRu < 40 || donEn < 40) errors.Add($"донатов ru {donRu}, en {donEn} — нужно ≥ 40");
    if (pairs != 18) errors.Add($"пар подсказок {pairs} != 18");
    foreach (var t in texts.Concat(nicks).Select(x => x.ToLowerInvariant()))
        foreach (var b in banned) if (t.Contains(b)) errors.Add($"бренд «{b}» в «{t}»");

    Console.WriteLine(string.Join("\n", report));
    Console.WriteLine($"всего фраз и донатов: {texts.Count}");
    if (errors.Count > 0) { Console.WriteLine("\nОШИБКИ:\n" + string.Join("\n", errors)); return 1; }
    Console.WriteLine("OK");
    return 0;
}
