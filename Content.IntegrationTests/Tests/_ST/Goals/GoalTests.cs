// SPDX-FileCopyrightText: 2026 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: MIT

using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.NUnit.Constraints;
using Content.Shared.Players;
using Content.Stellar.Shared.Goals;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST.Goals;

[TestFixture]
public sealed class GoalTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: GoalTestsBlankGoal
  components:
  - type: StellarGoal
";

    private const string GoalTestsBlankGoal = "GoalTestsBlankGoal";

    [SidedDependency(Side.Server)] private readonly StellarGoalsSystem _sGoals = default!;
    [SidedDependency(Side.Client)] private readonly StellarGoalsSystem _cGoals = default!;

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        Connected = true,
    };

    [Test]
    public async Task BasicFunctionality()
    {
        await Pair.CreateTestMap();

        _ = await AssignPlayerBody(ServerSession!);
        var sMind = ServerSession!.ContentData()!.Mind!.Value;
        var cMind = ToClientUid(sMind);
        Entity<StellarGoalComponent>? sGoal = null;

        await Server.WaitAssertion(() =>
        {
            Assert.That(sMind, Has.Comp<StellarGoalContainerObserverComponent>(Server));

            var container = _sGoals.SpawnContainer("Test container");
            _sGoals.ObserveContainer(sMind, container);
            var ok = _sGoals.TryAddGoal(container, GoalTestsBlankGoal, out sGoal);
            Assert.That(ok, Is.True);

            var goals = _sGoals.GetGoals(sMind);
            Assert.That(goals, Has.Count.EqualTo(1));
        });

        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(cMind, Has.Comp<StellarGoalContainerObserverComponent>(Client));

            var goals = _cGoals.GetGoals(cMind);
            Assert.That(goals, Has.Count.EqualTo(1));
        });

        await Server.WaitAssertion(() =>
        {
            SEntMan.DeleteEntity(sGoal!.Value);

            _cGoals.GetGoals(cMind);

            var goals = _sGoals.GetGoals(sMind);
            Assert.That(goals, Has.Count.EqualTo(0));
        });

        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var goals = _cGoals.GetGoals(cMind);
            Assert.That(goals, Has.Count.EqualTo(0));
        });
    }
}
