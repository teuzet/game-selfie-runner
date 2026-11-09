using Game.Core.Math;
using Game.Core.Runners;
using Game.Core.Street;
using Xunit;
using StreetMap = Game.Core.Street.Street;

namespace Game.Core.Tests
{
    public class CharacterTests
    {
        static readonly GameData Data = GameData.Load();
        static readonly StreetMap Street = StreetGenerator.Build(1, StreetData.Load(), StreetPool.Core);

        static RaceConfig Cfg(int a, int b, int c, int d) => new RaceConfig(7, new[]
        {
            new SeatConfig(SeatKind.Human, a), new SeatConfig(SeatKind.Human, b),
            new SeatConfig(SeatKind.Human, c), new SeatConfig(SeatKind.Human, d),
        });

        static double Dbl(Fixed f) => (double)f;

        [Fact]
        public void DataMatchesDesign()
        {
            var c = Data.CharacterData.Characters;
            Assert.Equal(4, c.Length);
            double[] speed = { 0.92, 1.10, 0.88, 1.00 }, stab = { 0.7, 1.2, 1.6, 0.9 }, view = { 1.4, 0.9, 1.2, 1.0 };
            double[] taste = { 1.0, 1.6, 0.6, 1.0 }, nosig = { 3, 2.5, 5, 3 };
            Perk[] perk = { Perk.GlamourLoyalViewers, Perk.AthleteEndurance, Perk.GrandmaBag, Perk.NerdModerator };
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(speed[i], Dbl(c[i].Speed), 3);
                Assert.Equal(stab[i], Dbl(c[i].Stability), 3);
                Assert.Equal(view[i], Dbl(c[i].Viewers), 3);
                Assert.Equal(taste[i], Dbl(c[i].Tastiness), 3);
                Assert.Equal(nosig[i], Dbl(c[i].NoSignal), 3);
                Assert.Equal(perk[i], c[i].Perk);
            }
        }

        [Fact]
        public void CharacterIdSetsStability()
        {
            var sim = new Simulation(Cfg(2, 0, 1, 3), Data);
            Assert.Equal(1.6, Dbl(Fixed.FromRaw(sim.State.Runners[0].Stability)), 3);
            Assert.Equal(0.7, Dbl(Fixed.FromRaw(sim.State.Runners[1].Stability)), 3);
            Assert.Equal(1.2, Dbl(Fixed.FromRaw(sim.State.Runners[2].Stability)), 3);
        }

        static int TicksTo1800(int characterId)
        {
            var rd = Characters.ForSeat(Data.RunnerData, Data.CharacterData.Get(characterId));
            var r = new RunnerState();
            RunnerMovement.InitRunner(rd, ref r);
            int t = 0;
            while (r.S < Fixed.FromInt(1800).Raw && t < 20000) { RunnerMovement.StepRunner(rd, Street, ref r, default); t++; }
            return t;
        }

        [Fact]
        public void GlamourSlowerThanAthleteByExpectedTime()
        {
            double glam = TicksTo1800(0), ath = TicksTo1800(1);
            double expected = 1.10 / 0.92;
            Assert.True(glam > ath);
            Assert.InRange(glam / ath, expected * 0.97, expected * 1.03);
        }

        [Fact]
        public void ShoveMovesGrandmaTwoOverOnePointSix()
        {
            var cd = Data.CharacterData;
            var sim = new Simulation(Cfg(0, 2, 1, 3), Data);
            Fixed x0 = Fixed.FromRaw(sim.State.Runners[1].X);
            Characters.ApplyShove(cd, sim.RunnerData, sim.Street, sim.State, sim.CharacterOf(0), 1, 1);
            Assert.Equal(2.0 / 1.6, Dbl(Fixed.FromRaw(sim.State.Runners[1].X) - x0), 2);
            // гламур (0.7): сдвиг 2/0.7 независимо от толкающего; бабуля (место 1) толкает так же
            Assert.Equal(2.0 / 0.7, Dbl(Characters.ShoveDistance(cd, Fixed.FromRaw(sim.State.Runners[0].Stability))), 2);
            Fixed g0 = Fixed.FromRaw(sim.State.Runners[0].X);
            Characters.ApplyShove(cd, sim.RunnerData, sim.Street, sim.State, sim.CharacterOf(1), 0, 1);
            Assert.Equal(2.0 / 0.7, Dbl(Fixed.FromRaw(sim.State.Runners[0].X) - g0), 2);
        }

        [Fact]
        public void AthleteStaminaRecoversIn3Point3Seconds()
        {
            int Recover(int id)
            {
                var rd = Characters.ForSeat(Data.RunnerData, Data.CharacterData.Get(id));
                var r = new RunnerState();
                RunnerMovement.InitRunner(rd, ref r);
                r.Stamina = 0;
                int t = 0;
                while (r.Stamina < rd.StaminaMax.Raw && t < 1000) { RunnerMovement.StepRunner(rd, Street, ref r, default); t++; }
                return t;
            }
            Assert.InRange(Recover(1), 98, 101);   // 100 / 30 = 3.33 с
            Assert.InRange(Recover(3), 148, 151);  // без перка 5 с
        }

        [Fact]
        public void StabilityDividesStun()
        {
            Assert.Equal(18, Collisions.StunTicks(Fixed.FromRaw(Fixed.FromFraction(3, 5).Raw), Fixed.One));
            Assert.Equal(11, Collisions.StunTicks(Fixed.FromFraction(3, 5), Data.CharacterData.Get(2).Stability));
        }

        [Fact]
        public void DeterministicWithCharacters()
        {
            var a = new Simulation(Cfg(0, 1, 2, 3), Data);
            var b = new Simulation(Cfg(0, 1, 2, 3), Data);
            for (int t = 0; t < 1500; t++)
            {
                a.Step(default); b.Step(default);
                Assert.Equal(a.StateHash(), b.StateHash());
            }
            var clone = a.Clone();
            for (int t = 0; t < 100; t++) { a.Step(default); clone.Step(default); }
            Assert.Equal(a.StateHash(), clone.StateHash());
        }
    }
}
