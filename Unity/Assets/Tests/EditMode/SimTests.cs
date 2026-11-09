using Game.Core;
using Game.Core.Bots;
using Game.Core.Bull;
using Game.Core.Economy;
using Game.Core.Hints;
using Game.Core.Math;
using Game.Core.Race;
using Game.Core.Runners;
using Game.Core.Street;
using NUnit.Framework;
using SelfieRunner.Sim;
using UnityEngine;

namespace SelfieRunner.Tests
{
    public class SimTests
    {
        private sealed class FixedSource : IPlayerInputSource
        {
            public PlayerInput Value;
            public PlayerInput Read() => Value;
        }

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static RaceConfig Config() => new RaceConfig(7, new[]
        {
            new SeatConfig(SeatKind.Bot, 0, 0),
            new SeatConfig(SeatKind.Human, 0),
            new SeatConfig(SeatKind.Bot, 0, 1),
            new SeatConfig(SeatKind.Bot, 0, 2),
        });

        private SimulationDriver MakeDriver(FixedSource src)
        {
            _go = new GameObject("driver");
            var d = _go.AddComponent<SimulationDriver>();
            d.Init(Config(), CoreDataLoader.Load(), src);
            return d;
        }

        [Test]
        public void OneSecondIsThirtySteps()
        {
            // кап 8 за кадр: секунда отдаётся 8+8+8+6, ровно 30 в сумме, ничего не выброшено
            var a = new TickAccumulator();
            int total = a.Advance(1.0), frames = 1;
            while (total < 30 && frames++ < 10) total += a.Advance(0);
            Assert.AreEqual(30, total);
            Assert.AreEqual(0, a.Advance(0));
        }

        [Test]
        public void SixtyFramesAreThirtySteps()
        {
            var a = new TickAccumulator();
            int total = 0;
            for (int i = 0; i < 60; i++) total += a.Advance(1.0 / 60);
            Assert.AreEqual(30, total);
        }

        [Test]
        public void DebtIsCappedAndCarriedOver()
        {
            var a = new TickAccumulator();
            Assert.AreEqual(8, a.Advance(20.0 / 30));
            Assert.AreEqual(12.0, a.Debt, 1e-6); // 12 тиков остались долгом и не выброшены
            // кап 8 за кадр: остаток доигрывается следующими кадрами (8, затем 4), всего 20
            Assert.AreEqual(8, a.Advance(0));
            Assert.AreEqual(4, a.Advance(0));
            Assert.AreEqual(0, a.Advance(0));
        }

        [Test]
        public void PlayerInputGoesOnlyToHumanSeat()
        {
            var src = new FixedSource { Value = new PlayerInput(1, 0, false) };
            var d = MakeDriver(src);
            Assert.AreEqual(1, d.PlayerSeat);
            d.StepOnce();
            Assert.AreEqual(src.Value, d.LastInputs.Get(1));
            Assert.AreEqual(default(PlayerInput), d.LastInputs.Get(0));
            Assert.AreEqual(default(PlayerInput), d.LastInputs.Get(2));
            Assert.AreEqual(default(PlayerInput), d.LastInputs.Get(3));
        }

        [Test]
        public void PlayerMovesRightAfterThirtyTicks()
        {
            var d = MakeDriver(new FixedSource { Value = new PlayerInput(1, 0, false) });
            int guard = 0;
            while (d.Sim.State.Phase == RacePhase.Countdown && guard++ < 5000) d.StepOnce();
            Assert.AreEqual(RacePhase.Running, d.Sim.State.Phase);
            for (int i = 0; i < 30; i++) d.StepOnce();
            double x = (double)Fixed.FromRaw(d.Sim.State.Runners[1].X);
            Assert.Greater(x, 2.0);
        }

        [Test]
        public void SprintShownOnlyWhenNotExhausted()
        {
            var sprint = new PlayerInput(0, 1, false);
            Assert.AreEqual("sprint", RaceHud.PaceName(sprint, 0));
            Assert.AreEqual("run", RaceHud.PaceName(sprint, 1));
            Assert.AreEqual("jog", RaceHud.PaceName(new PlayerInput(0, -1, false), 0));
        }

        [Test]
        public void LoaderReadsAllDataFilesAndSimulationIsCreated()
        {
            var files = CoreDataLoader.ReadFiles();
            foreach (string name in new[]
            {
                BotData.FileName, HintData.FileName, BullData.FileName, StreetData.FileName,
                ActionData.FileName, CharacterData.FileName, RunnerData.FileName, EconomyData.FileName,
            })
                Assert.IsTrue(files.Has(name), name);
            var sim = new Simulation(Config(), GameData.Parse(files));
            Assert.IsNotNull(sim.State);
        }
    }
}
