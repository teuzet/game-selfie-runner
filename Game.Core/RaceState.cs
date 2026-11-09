using Game.Core.Math;

namespace Game.Core
{
    /// <summary>Всё состояние забега. Только структуры и массивы фиксированного размера.</summary>
    public sealed class RaceState
    {
        public int Tick;
        public DetRandom Rng;
        public readonly RunnerState[] Runners = new RunnerState[RaceConfig.SeatCount];
        public BullState Bull;
        /// <summary>Факты и эффекты столкновений по местам (F-013); события F-019 читают Count/LastKind/LastS/LastX/LastTick.</summary>
        public readonly Game.Core.Runners.CollisionState[] Collisions = new Game.Core.Runners.CollisionState[RaceConfig.SeatCount];
        /// <summary>Устойчивость по местам (Fixed raw), из персонажа места (CharacterId), F-014.</summary>
        public readonly long[] Stability = new long[RaceConfig.SeatCount];
        /// <summary>Перезарядки и факты толчка/подножки по местам (F-015); события F-019 читают Shoved/Tripped/GotShoved/GotTripped и Last*.</summary>
        public readonly Game.Core.Runners.ActionState[] Actions = new Game.Core.Runners.ActionState[RaceConfig.SeatCount];
        /// <summary>Рывки быка (F-017): таймеры, цель, счётчики.</summary>
        public Game.Core.Bull.BullChargeState Charge;
        /// <summary>Подброс по местам (F-017): «нет сигнала», неуязвимость, факты «подброшен»/«встал» для F-019.</summary>
        public readonly Game.Core.Bull.TossState[] Tosses = new Game.Core.Bull.TossState[RaceConfig.SeatCount];
        /// <summary>Динамика подвижных препятствий, индекс = индекс в Street.Obstacles.</summary>
        public readonly Game.Core.Street.ObstacleDyn[] Dyn;
        /// <summary>«Препятствие уже действовало на бегуна»: seat * stride + индекс (препятствия, затем фонтан/вода по блокам).</summary>
        public readonly byte[] Hit;

        public RaceState(ulong seed, Game.Core.Runners.RunnerData runnerData, Game.Core.Bull.BullData bullData, Fixed lineS,
            Game.Core.Street.Street street, RaceConfig config, Game.Core.Runners.CharacterData characters)
        {
            Dyn = new Game.Core.Street.ObstacleDyn[street.Obstacles.Length];
            Hit = new byte[RaceConfig.SeatCount * Game.Core.Runners.Collisions.HitStride(street)];
            Game.Core.Street.MovingObstacles.Init(street, Dyn);
            for (int i = 0; i < Collisions.Length; i++) { Game.Core.Runners.Collisions.InitState(ref Collisions[i]); Game.Core.Runners.Actions.InitState(ref Actions[i]); Stability[i] = characters.Get(config.GetSeat(i).CharacterId).Stability.Raw; }
            Rng = new DetRandom(seed);
            Game.Core.Bull.BullCharge.InitState(seed, ref Charge);
            for (int i = 0; i < Tosses.Length; i++) Game.Core.Bull.BullCharge.InitToss(ref Tosses[i]);
            Game.Core.Bull.BullMovement.Init(bullData, lineS, ref Bull);
            for (int i = 0; i < Runners.Length; i++)
            {
                Game.Core.Runners.RunnerMovement.InitRunner(runnerData, ref Runners[i]);
                Runners[i].S = lineS.Raw; // бегуны на линии старта, бык — 25 м позади
            }
        }

        private RaceState(int dynLen, int hitLen)
        {
            Dyn = new Game.Core.Street.ObstacleDyn[dynLen];
            Hit = new byte[hitLen];
        }

        public RaceState Clone()
        {
            var c = new RaceState(Dyn.Length, Hit.Length);
            c.CopyFrom(this);
            return c;
        }

        public void CopyFrom(RaceState o)
        {
            Tick = o.Tick;
            Rng = o.Rng;
            Bull = o.Bull;
            Charge = o.Charge;
            System.Array.Copy(o.Tosses, Tosses, Tosses.Length);
            System.Array.Copy(o.Runners, Runners, Runners.Length);
            System.Array.Copy(o.Collisions, Collisions, Collisions.Length);
            System.Array.Copy(o.Stability, Stability, Stability.Length);
            System.Array.Copy(o.Actions, Actions, Actions.Length);
            System.Array.Copy(o.Dyn, Dyn, Dyn.Length);
            System.Array.Copy(o.Hit, Hit, Hit.Length);
        }

        /// <summary>FNV-1a (64 бита) по всем полям.</summary>
        public ulong StateHash()
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (ulong)Tick);
            h = Mix(h, Rng.State);
            h = Mix(h, Rng.Inc);
            for (int i = 0; i < Runners.Length; i++)
            {
                h = Mix(h, (ulong)Runners[i].S);
                h = Mix(h, (ulong)Runners[i].X);
                h = Mix(h, (ulong)Runners[i].Speed);
                h = Mix(h, (ulong)Runners[i].Stamina);
                h = Mix(h, (ulong)Runners[i].Exhausted);
                h = Mix(h, (ulong)Runners[i].StunTicks);
            }
            for (int i = 0; i < Collisions.Length; i++)
            {
                h = Mix(h, (ulong)Collisions[i].Count);
                h = Mix(h, (ulong)Collisions[i].WaterCount);
                h = Mix(h, (ulong)Collisions[i].LastKind);
                h = Mix(h, (ulong)Collisions[i].LastS);
                h = Mix(h, (ulong)Collisions[i].LastX);
                h = Mix(h, (ulong)Collisions[i].LastTick);
                h = Mix(h, (ulong)Collisions[i].SlowTicks);
                h = Mix(h, (ulong)Collisions[i].SlowCap);
                h = Mix(h, (ulong)Collisions[i].WaterPending);
                h = Mix(h, (ulong)Collisions[i].ZoneActive);
                h = Mix(h, (ulong)Collisions[i].ZoneRaw);
                h = Mix(h, (ulong)Stability[i]);
            }
            for (int i = 0; i < Actions.Length; i++)
            {
                h = Mix(h, (ulong)(uint)Actions[i].ShoveCd);
                h = Mix(h, (ulong)(uint)Actions[i].TripCd);
                h = Mix(h, (ulong)(uint)Actions[i].Shoved);
                h = Mix(h, (ulong)(uint)Actions[i].Tripped);
                h = Mix(h, (ulong)(uint)Actions[i].GotShoved);
                h = Mix(h, (ulong)(uint)Actions[i].GotTripped);
                h = Mix(h, (ulong)(uint)Actions[i].LastShoveTick);
                h = Mix(h, (ulong)(uint)Actions[i].LastTripTick);
                h = Mix(h, (ulong)(uint)Actions[i].LastGotShovedTick);
                h = Mix(h, (ulong)(uint)Actions[i].LastGotTrippedTick);
            }
            for (int i = 0; i < Dyn.Length; i++)
            {
                h = Mix(h, (ulong)Dyn[i].X);
                h = Mix(h, (ulong)(uint)Dyn[i].Dir);
                h = Mix(h, (ulong)(uint)Dyn[i].OpenTick);
            }
            for (int i = 0; i < Hit.Length; i++) h = Mix(h, Hit[i]);
            h = Mix(h, Charge.Rng.State);
            h = Mix(h, Charge.Rng.Inc);
            h = Mix(h, (ulong)(uint)Charge.Cooldown);
            h = Mix(h, (ulong)(uint)Charge.ChargeLeft);
            h = Mix(h, (ulong)(uint)Charge.Target);
            h = Mix(h, (ulong)(uint)Charge.Count);
            h = Mix(h, (ulong)(uint)Charge.Tosses);
            h = Mix(h, (ulong)(uint)Charge.LastChargeTick);
            h = Mix(h, (ulong)(uint)Charge.LastChargeTarget);
            for (int i = 0; i < Tosses.Length; i++)
            {
                h = Mix(h, (ulong)(uint)Tosses[i].NoSignalTicks);
                h = Mix(h, (ulong)(uint)Tosses[i].InvulnTicks);
                h = Mix(h, (ulong)(uint)Tosses[i].Tossed);
                h = Mix(h, (ulong)(uint)Tosses[i].Rose);
                h = Mix(h, (ulong)(uint)Tosses[i].LastTossTick);
                h = Mix(h, (ulong)(uint)Tosses[i].LastRoseTick);
                h = Mix(h, (ulong)Tosses[i].LastTossFromS);
            }
            h = Mix(h, (ulong)Bull.S);
            h = Mix(h, (ulong)Bull.X);
            h = Mix(h, (ulong)Bull.Speed);
            h = Mix(h, (ulong)Bull.Phase);
            h = Mix(h, (ulong)Bull.Timer);
            h = Mix(h, (ulong)Bull.SlowTicks);
            h = Mix(h, (ulong)Bull.Mode);
            return h;
        }

        private static ulong Mix(ulong h, ulong v)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (v >> (i * 8)) & 0xFF;
                    h *= 1099511628211UL;
                }
            }
            return h;
        }
    }
}
