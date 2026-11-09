using System;
using System.IO;
using System.Text;
using Game.Core.Data;
using Game.Core.Math;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Runners
{
    /// <summary>Числа движения бегуна из Data/runners.json.</summary>
    public sealed class RunnerData
    {
        public Fixed JogSpeed, RunSpeed, SprintSpeed, StaminaMax, SprintDrain, StaminaRegen, SprintResume,
            Accel, SteerSpeed, Radius, Separation;

        public static RunnerData Parse(string json)
        {
            var j = Json.Parse("runners.json", json, truncate: true);
            return new RunnerData
            {
                JogSpeed = j.Num("jogSpeed"), RunSpeed = j.Num("runSpeed"), SprintSpeed = j.Num("sprintSpeed"),
                StaminaMax = j.Num("staminaMax"), SprintDrain = j.Num("sprintDrain"),
                StaminaRegen = j.Num("staminaRegen"), SprintResume = j.Num("sprintResume"),
                Accel = j.Num("accel"), SteerSpeed = j.Num("steerSpeed"), Radius = j.Num("radius"),
                Separation = j.Num("separation"),
            };
        }

        public static RunnerData Load(string? path = null)
        {
            if (path == null)
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    string c = Path.Combine(dir, "Game.Core", "Data", "runners.json");
                    if (File.Exists(c)) { path = c; break; }
                    c = Path.Combine(dir, "Data", "runners.json");
                    if (File.Exists(c)) { path = c; break; }
                    dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
                if (path == null) throw new FileNotFoundException("Game.Core/Data/runners.json not found");
            }
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }
    }

    /// <summary>Движение бегунов: темп, выносливость, руль, уклон, стены, расталкивание. Без аллокаций.</summary>
    public static class RunnerMovement
    {
        private static readonly Fixed Dt = Fixed.FromFraction(1, Simulation.TicksPerSecond);

        public static void InitRunner(RunnerData d, ref RunnerState r)
        {
            r.Stamina = d.StaminaMax.Raw;
            r.Exhausted = 0;
        }

        /// <summary>Один тик одного бегуна (без взаимодействия с другими).</summary>
        public static void StepRunner(RunnerData d, StreetMap street, ref RunnerState r, PlayerInput inp)
        {
            Fixed s = Fixed.FromRaw(r.S), x = Fixed.FromRaw(r.X), speed = Fixed.FromRaw(r.Speed);
            Fixed stamina = Fixed.FromRaw(r.Stamina);

            bool stunned = r.StunTicks > 0;
            if (stunned) r.StunTicks--;

            // Выносливость и допустимость спринта.
            bool sprint = inp.Pace > 0 && r.Exhausted == 0 && stamina > Fixed.Zero && !stunned;
            if (sprint)
            {
                stamina = stamina - d.SprintDrain * Dt;
                if (stamina <= Fixed.Zero) { stamina = Fixed.Zero; r.Exhausted = 1; sprint = false; }
            }
            else
            {
                stamina = Fixed.Min(d.StaminaMax, stamina + d.StaminaRegen * Dt);
                if (r.Exhausted != 0 && stamina >= d.SprintResume) r.Exhausted = 0;
            }

            // Скорость: цель по темпу × уклон, разгон/торможение с постоянным ускорением.
            Fixed target;
            if (stunned) target = Fixed.Zero;
            else
            {
                Fixed pace = sprint ? d.SprintSpeed : inp.Pace < 0 ? d.JogSpeed : d.RunSpeed;
                target = pace * street.SlopeAt(s);
            }
            Fixed dv = d.Accel * Dt;
            if (speed < target) speed = Fixed.Min(speed + dv, target);
            else speed = Fixed.Max(speed - dv, target);

            s = s + speed * Dt;
            if (!stunned) x = x + Fixed.FromInt(inp.Steer) * d.SteerSpeed * Dt;
            x = ClampToStreet(d, street, s, x);

            r.S = s.Raw; r.X = x.Raw; r.Speed = speed.Raw; r.Stamina = stamina.Raw;
        }

        public static Fixed ClampToStreet(RunnerData d, StreetMap street, Fixed s, Fixed x)
        {
            Fixed lim = street.WidthAt(s) / Fixed.FromInt(2) - d.Radius;
            if (lim < Fixed.Zero) lim = Fixed.Zero;
            return Fixed.Clamp(x, -lim, lim);
        }

        /// <summary>Мягкое расталкивание кругов радиуса R: за тик снимается доля перекрытия. Порядок пар фиксирован.</summary>
        public static void Separate(RunnerData d, StreetMap street, RunnerState[] runners)
        {
            Fixed minDist = d.Radius * Fixed.FromInt(2);
            for (int i = 0; i < runners.Length; i++)
            for (int j = i + 1; j < runners.Length; j++)
            {
                Fixed dx = Fixed.FromRaw(runners[j].X - runners[i].X);
                Fixed ds = Fixed.FromRaw(runners[j].S - runners[i].S);
                if (Fixed.Abs(dx) >= minDist || Fixed.Abs(ds) >= minDist) continue;
                Fixed dist = Fixed.Sqrt(dx * dx + ds * ds);
                if (dist >= minDist) continue;
                Fixed nx, ns;
                if (dist.Raw < 16) { nx = Fixed.One; ns = Fixed.Zero; } // совпали: разводим по x, меньший номер места — левее
                else { nx = dx / dist; ns = ds / dist; }
                Fixed push = (minDist - dist) * d.Separation * Fixed.Half; // каждому половина
                runners[i].X = (Fixed.FromRaw(runners[i].X) - nx * push).Raw;
                runners[j].X = (Fixed.FromRaw(runners[j].X) + nx * push).Raw;
                runners[i].S = (Fixed.FromRaw(runners[i].S) - ns * push).Raw;
                runners[j].S = (Fixed.FromRaw(runners[j].S) + ns * push).Raw;
            }
            for (int i = 0; i < runners.Length; i++)
                runners[i].X = ClampToStreet(d, street, Fixed.FromRaw(runners[i].S), Fixed.FromRaw(runners[i].X)).Raw;
        }
    }
}
