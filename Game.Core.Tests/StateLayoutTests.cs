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
        // F-024: адреналинщик и тролль получили свой код и поля памяти (охота, период действия) — эталон переснят на ветке F-024.
        // F-065 (плейтест итерации 1): economy.json — зоны «холодно» 20–50 м −2/с и «эфир пустой» −6/с (playtest/iter-1.md) — эталон переснят.
        // F-070: порядок композиции хэша — каждый модуль хэширует свои структуры (RunnerState.Hash, MODULES.md), хэш забега
        // собирается по модулям. Поведение то же: оракул Game.Sim на сидах 1–40 совпал с playtest/iter-1/run-2 до переснятия.
        // F-021: модуль Hints — своё состояние в хэше места и подсказки чата ботам (design/overview.md → «Подсказки чата»): эталон переснят.
        // F-073: подброшенный бот спринтует от быка на время неуязвимости (design/F-005.md §2.5) — эталон переснят.
        // F-075: пол зрителей 100 и скорость спортсмена 1.05 (design/F-101.md §3.1, §3.2) — эталон переснят.
        // F-071 (design/F-100.md §3.1): подброс закрывает эпизод — защищённые, отступ быка 4 м, охлаждение цели 30 с, резинка без разгона у бегуна
        // впереди ближе 8 м, подъём сразу в бег; TossState.LastCause в хэше — эталон переснят.
        // F-072 (design/F-100.md §3.2, §4): рывок 2 с без доворота (chargeSteer 1), промах (BullState.NoSteerTicks в хэше), событие BullCharge — эталон переснят.
        // F-066 (плейтест итерации 2, playtest/iter-2.md → «Тюнинг»): economy.json — зона «тепло» +8/с (было +5) и донаты за подброс 20 (было 50) — эталон переснят.
        // F-081: рывок ускоряет быка, F-100 §3.2 — BullCharge ведёт s/скорость/x быка в рывке от значений до BullMovement.Step; причина «рывок» только у цели — эталон переснят.
        // F-083: F-102 §3.1 (резинка без якоря: запрет разгона 9.5 только при отставании ≤ anchorLag, «наелся» держит), §3.2 (цель рывка в досягаемости),
        // §3.3 (трус: трусца дальше jogAbove с гистерезисом) — правила и числа меняются, эталон переснят.
        (1UL, new ulong[] { 1220347544348666351UL, 4107821869796912311UL, 4834379432714903709UL, 6920591154828329382UL }),
        (42UL, new ulong[] { 11390380896233199350UL, 7019557851807123838UL, 3275187246014957689UL, 578731120942348128UL }),
        (777UL, new ulong[] { 16778350252708800675UL, 15973017313232091362UL, 1806465398111977715UL, 3623019364594147944UL }),
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
