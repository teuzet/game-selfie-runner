using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Math;

namespace Game.Core.Data
{
    /// <summary>Единственный читатель Data/*.json: объекты, массивы, строки, числа (как текст → Fixed), bool, null.
    /// Нет ключа или не тот тип — FormatException с именем файла и ключа, а не ноль.</summary>
    public static class Json
    {
        /// <summary>Разбирает документ-объект. <paramref name="file"/> — имя файла для сообщений об ошибках.</summary>
        public static JsonObject Parse(string file, string json, bool truncate = false)
        {
            var v = new Reader(file, json).ReadDocument();
            if (v is Dictionary<string, object?> d) return new JsonObject(file, "", d, truncate);
            throw new FormatException(file + ": корень не объект");
        }

        /// <summary>Десятичная строка → Fixed целочисленно, без float. По умолчанию округление к ближайшему;
        /// <paramref name="truncate"/> — усечение дроби вниз (так исторически читают bull/runners/actions/characters.json,
        /// сменить режим = сдвинуть raw-значения на 1 LSB и перетюнить баланс).</summary>
        public static Fixed ParseFixed(string text, bool truncate = false)
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
            long frac = truncate ? num * Fixed.OneRaw / den : (num * Fixed.OneRaw * 2 + den) / (den * 2);
            long raw = (ip << Fixed.FractionBits) + frac;
            return Fixed.FromRaw(neg ? -raw : raw);
        }

        internal sealed class JNum { public readonly string Text; public JNum(string t) { Text = t; } }

        sealed class Reader
        {
            readonly string _file;
            readonly string _s;
            int _p;
            public Reader(string file, string s) { _file = file; _s = s; }

            public object? ReadDocument()
            {
                var v = ReadValue();
                SkipWs();
                if (_p != _s.Length) throw Err("trailing data");
                return v;
            }

            FormatException Err(string m) => new FormatException(_file + ": JSON: " + m + " at " + _p.ToString(CultureInfo.InvariantCulture));

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

    /// <summary>Объект JSON с типизированным доступом. Путь (файл + ключи) попадает в каждое исключение.</summary>
    public sealed class JsonObject
    {
        readonly string _file, _path;
        readonly Dictionary<string, object?> _d;
        readonly bool _truncate;

        internal JsonObject(string file, string path, Dictionary<string, object?> d, bool truncate)
        { _file = file; _path = path; _d = d; _truncate = truncate; }

        public bool Has(string key) => _d.ContainsKey(key);

        object Req(string key)
        {
            if (!_d.TryGetValue(key, out var v)) throw new FormatException(_file + ": нет ключа " + _path + key);
            if (v == null) throw new FormatException(_file + ": ключ " + _path + key + " = null");
            return v;
        }

        FormatException Bad(string key, string want) => new FormatException(_file + ": ключ " + _path + key + " не " + want);

        public Fixed Num(string key)
        {
            if (Req(key) is Json.JNum n)
            {
                try { return Json.ParseFixed(n.Text, _truncate); }
                catch (FormatException) { throw Bad(key, "число (" + n.Text + ")"); }
            }
            throw Bad(key, "число");
        }

        /// <summary>Необязательное число: ключа нет — <paramref name="fallback"/> (по умолчанию 0). Есть, но не число — исключение.</summary>
        public Fixed NumOr(string key, Fixed fallback = default) => _d.ContainsKey(key) ? Num(key) : fallback;

        public int Int(string key) => Num(key).ToInt();

        public string Str(string key) => Req(key) as string ?? throw Bad(key, "строка");

        public string StrOr(string key, string fallback) => _d.ContainsKey(key) ? Str(key) : fallback;

        public bool Bool(string key) => Req(key) is bool b ? b : throw Bad(key, "bool");

        public bool BoolOr(string key, bool fallback) => _d.ContainsKey(key) ? Bool(key) : fallback;

        public JsonObject Obj(string key) =>
            Req(key) is Dictionary<string, object?> o ? new JsonObject(_file, _path + key + ".", o, _truncate) : throw Bad(key, "объект");

        public JsonArray Arr(string key) =>
            Req(key) is List<object?> l ? new JsonArray(_file, _path + key, l, _truncate) : throw Bad(key, "массив");

        public string[] StrList(string key) => Arr(key).Strs();
    }

    /// <summary>Массив JSON; элементы адресуются по индексу, ошибки несут путь «ключ[i]».</summary>
    public sealed class JsonArray
    {
        readonly string _file, _path;
        readonly List<object?> _l;
        readonly bool _truncate;

        internal JsonArray(string file, string path, List<object?> l, bool truncate)
        { _file = file; _path = path; _l = l; _truncate = truncate; }

        public int Count => _l.Count;

        FormatException Bad(int i, string want) => new FormatException(_file + ": " + _path + "[" + i.ToString(CultureInfo.InvariantCulture) + "] не " + want);

        public JsonObject Obj(int i) =>
            _l[i] is Dictionary<string, object?> o ? new JsonObject(_file, _path + "[" + i.ToString(CultureInfo.InvariantCulture) + "].", o, _truncate) : throw Bad(i, "объект");

        public JsonArray Arr(int i) =>
            _l[i] is List<object?> l ? new JsonArray(_file, _path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", l, _truncate) : throw Bad(i, "массив");

        public Fixed Num(int i)
        {
            if (_l[i] is Json.JNum n)
            {
                try { return Json.ParseFixed(n.Text, _truncate); }
                catch (FormatException) { throw Bad(i, "число (" + n.Text + ")"); }
            }
            throw Bad(i, "число");
        }

        public string Str(int i) => _l[i] as string ?? throw Bad(i, "строка");

        public string[] Strs()
        {
            var r = new string[_l.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Str(i);
            return r;
        }
    }
}
