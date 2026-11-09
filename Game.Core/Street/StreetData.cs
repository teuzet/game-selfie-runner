using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core.Math;

namespace Game.Core.Street
{
    /// <summary>Данные улицы: блоки и препятствия из Data/street.json. Порядок — как в файле.</summary>
    public sealed class StreetData
    {
        public BlockDef[] Blocks = new BlockDef[0];
        public ObstacleDef[] Obstacles = new ObstacleDef[0];

        public BlockDef? FindBlock(string id)
        {
            for (int i = 0; i < Blocks.Length; i++) if (Blocks[i].Id == id) return Blocks[i];
            return null;
        }

        public ObstacleDef? FindObstacle(string id)
        {
            for (int i = 0; i < Obstacles.Length; i++) if (Obstacles[i].Id == id) return Obstacles[i];
            return null;
        }

        /// <summary>
        /// Читает Game.Core/Data/street.json: ищет файл вверх от базовой папки приложения.
        /// Unity и другие хосты передают текст сами через <see cref="Parse"/>.
        /// </summary>
        public static StreetData Load(string? path = null)
        {
            if (path == null)
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    string candidate = Path.Combine(dir, "Game.Core", "Data", "street.json");
                    if (File.Exists(candidate)) { path = candidate; break; }
                    candidate = Path.Combine(dir, "Data", "street.json");
                    if (File.Exists(candidate)) { path = candidate; break; }
                    dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
                if (path == null) throw new FileNotFoundException("Game.Core/Data/street.json not found");
            }
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        public static StreetData Parse(string json)
        {
            var root = (Dictionary<string, object?>)new Json(json).ReadDocument()!;
            var d = new StreetData();
            var blocks = (List<object?>)root["blocks"]!;
            d.Blocks = new BlockDef[blocks.Count];
            for (int i = 0; i < blocks.Count; i++) d.Blocks[i] = ReadBlock((Dictionary<string, object?>)blocks[i]!);
            var obs = (List<object?>)root["obstacles"]!;
            d.Obstacles = new ObstacleDef[obs.Count];
            for (int i = 0; i < obs.Count; i++) d.Obstacles[i] = ReadObstacle((Dictionary<string, object?>)obs[i]!);
            return d;
        }

        /// <summary>Канонический текстовый дамп всех данных — для сравнения и хэшей.</summary>
        public string Dump()
        {
            var sb = new StringBuilder();
            foreach (var b in Blocks)
            {
                sb.Append("B ").Append(b.Id).Append('|').Append(b.Name).Append('|').Append(b.Tier).Append('|').Append(b.Special)
                  .Append('|').Append(b.LengthMin.Raw).Append('|').Append(b.LengthMax.Raw).Append('|').Append(b.LengthStep.Raw)
                  .Append('|').Append(b.Slope).Append('|').Append(b.Rise.Raw).Append('|').Append(b.SlotStep.Raw)
                  .Append('|').Append(b.SlotMargin.Raw).Append('|').Append(b.DensityMul.Raw).Append("|W");
                foreach (var w in b.Width) sb.Append(' ').Append(w.S.Raw).Append(':').Append(w.Width.Raw);
                sb.Append("|O");
                foreach (var o in b.Obstacles) sb.Append(' ').Append(o.Type).Append(':').Append(string.Join("", o.Lanes)).Append(':').Append(o.Weight);
                sb.Append("|F");
                foreach (var f in b.Fixed)
                    sb.Append(' ').Append(f.Kind).Append(':').Append(f.Type).Append(':').Append(f.Side).Append(':').Append(f.S.Raw)
                      .Append(':').Append(f.X.Raw).Append(':').Append(f.Size.Raw).Append(':').Append(f.S0.Raw).Append(':').Append(f.S1.Raw).Append(':').Append(f.Across.Raw);
                sb.Append("|N ").Append(string.Join(",", b.Next)).Append('\n');
            }
            foreach (var o in Obstacles)
            {
                sb.Append("O ").Append(o.Id).Append('|').Append(o.Name).Append('|').Append(o.Tier).Append('|').Append(o.Shape)
                  .Append('|').Append(o.Size.Raw).Append('|').Append(o.SizeAlong.Raw).Append('|').Append(o.SizeAcross.Raw).Append('|').Append(o.FullWidth)
                  .Append('|').Append(string.Join("", o.Lanes)).Append('|').Append(o.Effect).Append('|').Append(o.Stun.Raw)
                  .Append('|').Append(o.SlowMul.Raw).Append('|').Append(o.SlowTime.Raw).Append('|').Append(o.PushBack.Raw)
                  .Append('|').Append(o.MotionKind).Append('|').Append(o.OpenTime.Raw).Append('|').Append(o.TriggerMin.Raw)
                  .Append('|').Append(o.TriggerMax.Raw).Append('|').Append(o.Speed.Raw).Append('|').Append(o.WallGap.Raw).Append('\n');
            }
            return sb.ToString();
        }

        // ---- чтение узлов ----

        static BlockDef ReadBlock(Dictionary<string, object?> j)
        {
            var b = new BlockDef
            {
                Id = Str(j, "id"), Name = Str(j, "name"), Tier = Str(j, "tier"),
                Special = (bool)j["special"]!,
                Rise = Num(j, "rise"), SlotStep = Num(j, "slotStep"), SlotMargin = Num(j, "slotMargin"), DensityMul = Num(j, "densityMul"),
                Note = Str(j, "note"),
            };
            var len = Obj(j, "length");
            b.LengthMin = Num(len, "min"); b.LengthMax = Num(len, "max");
            b.LengthStep = len.ContainsKey("step") ? Num(len, "step") : Fixed.Zero;
            switch (Str(j, "slope"))
            {
                case "flat": b.Slope = Slope.Flat; break;
                case "down": b.Slope = Slope.Down; break;
                case "up": b.Slope = Slope.Up; break;
                default: throw new FormatException("bad slope in " + b.Id);
            }
            var wl = (List<object?>)j["width"]!;
            b.Width = new WidthPoint[wl.Count];
            for (int i = 0; i < wl.Count; i++)
            {
                var p = (List<object?>)wl[i]!;
                b.Width[i] = new WidthPoint(ToFixed(p[0]), ToFixed(p[1]));
            }
            var ol = (List<object?>)j["obstacles"]!;
            b.Obstacles = new BlockObstacleDef[ol.Count];
            for (int i = 0; i < ol.Count; i++)
            {
                var o = (Dictionary<string, object?>)ol[i]!;
                b.Obstacles[i] = new BlockObstacleDef { Type = Str(o, "type"), Lanes = StrList(o, "lanes"), Weight = (int)Num(o, "weight").ToInt() };
            }
            var fl = (List<object?>)j["fixed"]!;
            b.Fixed = new FixedElementDef[fl.Count];
            for (int i = 0; i < fl.Count; i++)
            {
                var f = (Dictionary<string, object?>)fl[i]!;
                b.Fixed[i] = new FixedElementDef
                {
                    Kind = Str(f, "kind"), Type = f.ContainsKey("type") ? Str(f, "type") : "", Side = f.ContainsKey("side") ? Str(f, "side") : "",
                    S = Opt(f, "s"), X = Opt(f, "x"), Size = Opt(f, "size"), S0 = Opt(f, "s0"), S1 = Opt(f, "s1"), Across = Opt(f, "across"),
                };
            }
            b.Next = StrList(j, "next");
            return b;
        }

        static ObstacleDef ReadObstacle(Dictionary<string, object?> j)
        {
            var o = new ObstacleDef
            {
                Id = Str(j, "id"), Name = Str(j, "name"), Tier = Str(j, "tier"), Shape = Str(j, "shape"),
                Size = Opt(j, "size"), SizeAlong = Opt(j, "sizeAlong"), SizeAcross = Opt(j, "sizeAcross"),
                FullWidth = j.ContainsKey("fullWidth") && (bool)j["fullWidth"]!,
                Lanes = StrList(j, "lanes"), Effect = Str(j, "effect"),
                Stun = Num(j, "stun"), SlowMul = Num(j, "slowMul"), SlowTime = Num(j, "slowTime"), PushBack = Opt(j, "pushBack"),
                Note = Str(j, "note"),
            };
            var m = Obj(j, "motion");
            o.MotionKind = Str(m, "kind");
            o.OpenTime = Opt(m, "openTime"); o.TriggerMin = Opt(m, "triggerMin"); o.TriggerMax = Opt(m, "triggerMax");
            o.Speed = Opt(m, "speed"); o.WallGap = Opt(m, "wallGap");
            return o;
        }

        static string Str(Dictionary<string, object?> j, string k) => (string)j[k]!;
        static Dictionary<string, object?> Obj(Dictionary<string, object?> j, string k) => (Dictionary<string, object?>)j[k]!;
        static Fixed Num(Dictionary<string, object?> j, string k) => ToFixed(j[k]);
        static Fixed Opt(Dictionary<string, object?> j, string k) => j.ContainsKey(k) ? ToFixed(j[k]) : Fixed.Zero;
        static string[] StrList(Dictionary<string, object?> j, string k)
        {
            var l = (List<object?>)j[k]!;
            var r = new string[l.Count];
            for (int i = 0; i < r.Length; i++) r[i] = (string)l[i]!;
            return r;
        }

        static Fixed ToFixed(object? n) => ParseFixed(((JNum)n!).Text);

        /// <summary>Десятичная строка → Fixed целочисленно (округление к ближайшему), без float.</summary>
        public static Fixed ParseFixed(string text)
        {
            int i = 0;
            bool neg = false;
            if (text[0] == '-') { neg = true; i = 1; }
            long ip = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') { ip = ip * 10 + (text[i] - '0'); i++; }
            long num = 0, den = 1;
            if (i < text.Length && text[i] == '.')
            {
                i++;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9' && den < 1_000_000_000L) { num = num * 10 + (text[i] - '0'); den *= 10; i++; }
            }
            if (i < text.Length && text[i] != '.' && !(text[i] >= '0' && text[i] <= '9'))
                throw new FormatException("unsupported number: " + text);
            long raw = (ip << Fixed.FractionBits) + (num * Fixed.OneRaw * 2 + den) / (den * 2);
            return Fixed.FromRaw(neg ? -raw : raw);
        }

        sealed class JNum { public readonly string Text; public JNum(string t) { Text = t; } }

        /// <summary>Минимальный JSON-парсер: объекты, массивы, строки, числа (как текст), true/false/null.</summary>
        sealed class Json
        {
            readonly string _s;
            int _p;
            public Json(string s) { _s = s; }

            public object? ReadDocument()
            {
                var v = ReadValue();
                SkipWs();
                if (_p != _s.Length) throw Err("trailing data");
                return v;
            }

            FormatException Err(string m) => new FormatException("JSON: " + m + " at " + _p.ToString(CultureInfo.InvariantCulture));

            void SkipWs() { while (_p < _s.Length && char.IsWhiteSpace(_s[_p])) _p++; }

            object? ReadValue()
            {
                SkipWs();
                if (_p >= _s.Length) throw Err("unexpected end");
                char c = _s[_p];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                if (Lit("true")) return true;
                if (Lit("false")) return false;
                if (Lit("null")) return null;
                throw Err("unexpected char");
            }

            bool Lit(string w)
            {
                if (string.CompareOrdinal(_s, _p, w, 0, w.Length) != 0) return false;
                _p += w.Length;
                return true;
            }

            Dictionary<string, object?> ReadObject()
            {
                var d = new Dictionary<string, object?>();
                _p++;
                SkipWs();
                if (_s[_p] == '}') { _p++; return d; }
                while (true)
                {
                    SkipWs();
                    string k = ReadString();
                    SkipWs();
                    if (_s[_p++] != ':') throw Err("expected ':'");
                    d[k] = ReadValue();
                    SkipWs();
                    char c = _s[_p++];
                    if (c == '}') return d;
                    if (c != ',') throw Err("expected ',' or '}'");
                }
            }

            List<object?> ReadArray()
            {
                var l = new List<object?>();
                _p++;
                SkipWs();
                if (_s[_p] == ']') { _p++; return l; }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWs();
                    char c = _s[_p++];
                    if (c == ']') return l;
                    if (c != ',') throw Err("expected ',' or ']'");
                }
            }

            string ReadString()
            {
                if (_s[_p] != '"') throw Err("expected string");
                _p++;
                var sb = new StringBuilder();
                while (true)
                {
                    char c = _s[_p++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = _s[_p++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)int.Parse(_s.Substring(_p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _p += 4; break;
                        default: sb.Append(e); break;
                    }
                }
            }

            JNum ReadNumber()
            {
                int st = _p;
                if (_s[_p] == '-') _p++;
                while (_p < _s.Length && ((_s[_p] >= '0' && _s[_p] <= '9') || _s[_p] == '.')) _p++;
                return new JNum(_s.Substring(st, _p - st));
            }
        }
    }
}
