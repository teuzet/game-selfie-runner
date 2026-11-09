using System;
using Game.Core;
using Xunit;

/// <summary>F-063: сведение состояния бегуна в RunnerState не меняет поведение. Хэши зафиксированы до рефакторинга.</summary>
public class StateLayoutTests
{
    static RaceConfig Cfg(ulong seed) => new RaceConfig(seed, new[]
    {
        new SeatConfig(SeatKind.Bot, 0, 0), new SeatConfig(SeatKind.Bot, 1, 1),
        new SeatConfig(SeatKind.Bot, 2, 2), new SeatConfig(SeatKind.Bot, 3, 1),
    });

    // Хэши на тиках 1500, 3000, 4500, 6000.
    static ulong[] Run(ulong seed)
    {
        var sim = new Simulation(Cfg(seed));
        var h = new ulong[4];
        for (int t = 1; t <= 6000; t++)
        {
            sim.Step(TickInputs.Empty);
            if (t % 1500 == 0) h[t / 1500 - 1] = sim.StateHash();
        }
        return h;
    }

    static readonly (ulong seed, ulong[] hashes)[] Golden =
    {
        // Сняты на main 65593b7 (до сведения состояния, параллельные массивы) с добавкой в StateHash
        // LastShoveTarget/LastTripTarget/LastShovedBy/LastTrippedBy на то же место, что и в RunnerState; ветка даёт те же значения.
        // F-023: с мозгами ботов места ведёт решение по наблюдениям, а не пустой вход — эталон переснят на ветке F-023.
        (1UL, new ulong[] { 11454769332209456970UL, 14414084834001472698UL, 3289484543905694968UL, 3060389421449194665UL }),
        (42UL, new ulong[] { 13916581429250618722UL, 809112750229290743UL, 17802629154398781415UL, 9026907172215114509UL }),
        (777UL, new ulong[] { 15636462576193198619UL, 4913075236699060631UL, 13051121272764866300UL, 872301307734368437UL }),
    };

    [Fact]
    public void RaceHashesMatchPreRefactorGolden()
    {
        var bad = "";
        foreach (var (seed, expected) in Golden)
        {
            var got = Run(seed);
            if (expected[0] != got[0] || expected[1] != got[1] || expected[2] != got[2] || expected[3] != got[3])
                bad += $"seed {seed}: {string.Join("UL, ", got)}UL\n";
        }
        Assert.True(bad.Length == 0, bad);
    }

    [Fact]
    public void CloneCoversAllRunnerFields()
    {
        var sim = new Simulation(Cfg(5));
        for (int t = 0; t < 3000; t++) sim.Step(TickInputs.Empty);
        var c = sim.State.Clone();
        Assert.Equal(sim.State.StateHash(), c.StateHash());
        for (int i = 0; i < 20; i++) sim.Step(TickInputs.Empty);
        var d = sim.State.Clone();
        Assert.Equal(sim.State.StateHash(), d.StateHash());
        Assert.NotEqual(c.StateHash(), d.StateHash());
    }

    // Листовые поля RunnerState (с вложенными структурами) как цепочки FieldInfo.
    static void Leaves(Type t, System.Collections.Generic.List<System.Reflection.FieldInfo> path,
        System.Collections.Generic.List<System.Reflection.FieldInfo[]> acc)
    {
        foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            path.Add(f);
            if (f.FieldType.IsValueType && !f.FieldType.IsPrimitive) Leaves(f.FieldType, path, acc);
            else acc.Add(path.ToArray());
            path.RemoveAt(path.Count - 1);
        }
    }

    static object SetLeaf(object box, System.Reflection.FieldInfo[] path, int i)
    {
        var f = path[i];
        if (i == path.Length - 1)
            f.SetValue(box, Convert.ChangeType(12345, f.FieldType));
        else
            f.SetValue(box, SetLeaf(f.GetValue(box), path, i + 1));
        return box;
    }

    [Fact]
    public void EveryRunnerFieldChangesStateHash()
    {
        var leaves = new System.Collections.Generic.List<System.Reflection.FieldInfo[]>();
        Leaves(typeof(RunnerState), new System.Collections.Generic.List<System.Reflection.FieldInfo>(), leaves);
        Assert.True(leaves.Count > 40, "рефлексия не нашла поля RunnerState");
        var sim = new Simulation(Cfg(5));
        for (int t = 0; t < 600; t++) sim.Step(TickInputs.Empty);
        var baseHash = sim.State.StateHash();
        var missing = "";
        foreach (var path in leaves)
        {
            var c = sim.State.Clone();
            object box = c.Runners[1];
            c.Runners[1] = (RunnerState)SetLeaf(box, path, 0);
            if (c.StateHash() == baseHash)
                missing += string.Join(".", Array.ConvertAll(path, p => p.Name)) + " ";
            // клон копирует поле целиком
            Assert.Equal(c.StateHash(), c.Clone().StateHash());
        }
        Assert.True(missing.Length == 0, "поля вне StateHash: " + missing);
    }
}
