using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core.Data;
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

        public const string FileName = "street.json";

        public static StreetData Parse(string json)
        {
            var root = Json.Parse(FileName, json);
            var d = new StreetData();
            var blocks = root.Arr("blocks");
            d.Blocks = new BlockDef[blocks.Count];
            for (int i = 0; i < blocks.Count; i++) d.Blocks[i] = ReadBlock(blocks.Obj(i));
            var obs = root.Arr("obstacles");
            d.Obstacles = new ObstacleDef[obs.Count];
            for (int i = 0; i < obs.Count; i++) d.Obstacles[i] = ReadObstacle(obs.Obj(i));
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

        static BlockDef ReadBlock(JsonObject j)
        {
            var b = new BlockDef
            {
                Id = j.Str("id"), Name = j.Str("name"), Tier = j.Str("tier"),
                Special = j.Bool("special"),
                Rise = j.Num("rise"), SlotStep = j.Num("slotStep"), SlotMargin = j.Num("slotMargin"), DensityMul = j.Num("densityMul"),
                Note = j.Str("note"),
            };
            var len = j.Obj("length");
            b.LengthMin = len.Num("min"); b.LengthMax = len.Num("max");
            b.LengthStep = len.NumOr("step");
            switch (j.Str("slope"))
            {
                case "flat": b.Slope = Slope.Flat; break;
                case "down": b.Slope = Slope.Down; break;
                case "up": b.Slope = Slope.Up; break;
                default: throw new FormatException("bad slope in " + b.Id);
            }
            var wl = j.Arr("width");
            b.Width = new WidthPoint[wl.Count];
            for (int i = 0; i < wl.Count; i++)
            {
                var p = wl.Arr(i);
                b.Width[i] = new WidthPoint(p.Num(0), p.Num(1));
            }
            var ol = j.Arr("obstacles");
            b.Obstacles = new BlockObstacleDef[ol.Count];
            for (int i = 0; i < ol.Count; i++)
            {
                var o = ol.Obj(i);
                b.Obstacles[i] = new BlockObstacleDef { Type = o.Str("type"), Lanes = o.StrList("lanes"), Weight = o.Int("weight") };
            }
            var fl = j.Arr("fixed");
            b.Fixed = new FixedElementDef[fl.Count];
            for (int i = 0; i < fl.Count; i++)
            {
                var f = fl.Obj(i);
                b.Fixed[i] = new FixedElementDef
                {
                    Kind = f.Str("kind"), Type = f.StrOr("type", ""), Side = f.StrOr("side", ""),
                    S = f.NumOr("s"), X = f.NumOr("x"), Size = f.NumOr("size"), S0 = f.NumOr("s0"), S1 = f.NumOr("s1"), Across = f.NumOr("across"),
                };
            }
            b.Next = j.StrList("next");
            return b;
        }

        static ObstacleDef ReadObstacle(JsonObject j)
        {
            var o = new ObstacleDef
            {
                Id = j.Str("id"), Name = j.Str("name"), Tier = j.Str("tier"), Shape = j.Str("shape"),
                Size = j.NumOr("size"), SizeAlong = j.NumOr("sizeAlong"), SizeAcross = j.NumOr("sizeAcross"),
                FullWidth = j.BoolOr("fullWidth", false),
                Lanes = j.StrList("lanes"), Effect = j.Str("effect"),
                Stun = j.Num("stun"), SlowMul = j.Num("slowMul"), SlowTime = j.Num("slowTime"), PushBack = j.NumOr("pushBack"),
                Note = j.Str("note"),
            };
            var m = j.Obj("motion");
            o.MotionKind = m.Str("kind");
            o.OpenTime = m.NumOr("openTime"); o.TriggerMin = m.NumOr("triggerMin"); o.TriggerMax = m.NumOr("triggerMax");
            o.Speed = m.NumOr("speed"); o.WallGap = m.NumOr("wallGap");
            return o;
        }
    }
}
