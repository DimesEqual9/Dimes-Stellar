// SPDX-FileCopyrightText: 2026 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: MIT

using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.NUnit.Constraints;
using Content.Server.GameTicking;
using Content.Shared.Players;
using Content.Stellar.Shared.Goals;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST.Goals;

[TestFixture]
public sealed class GoalTickerTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: GoalTickerTestsBlankGoal
  components:
  - type: StellarGoal
";

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        Connected = true,
        DummyTicker = false,
    };

    private const string GoalTickerTestsBlankGoal = "GoalTickerTestsBlankGoal";

    [SidedDependency(Side.Server)] private readonly StellarGoalsSystem _sGoals = default!;
    [SidedDependency(Side.Server)] private readonly GameTicker _sTicker = default!;

    [Test]
    public async Task Cleanup()
    {
        await Pair.CreateTestMap();

        _ = await AssignPlayerBody(ServerSession!);
        var sMind = ServerSession!.ContentData()!.Mind!.Value;

        await Server.WaitAssertion(() =>
        {
            var container = _sGoals.SpawnContainer("Test container");
            _sGoals.ObserveContainer(sMind, container);
            _sGoals.TryAddGoal(container, GoalTickerTestsBlankGoal, out _);

            Assert.That(_sTicker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            Assert.DoesNotThrow(() =>
            {
                _sTicker.RestartRound();
            });
        });
    }
}
