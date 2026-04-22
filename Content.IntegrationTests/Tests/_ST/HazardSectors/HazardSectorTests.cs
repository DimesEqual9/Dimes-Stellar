using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.NUnit.Constraints;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Content.Stellar.Server.HazardSectors;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST.HazardSectors;

[TestFixture]
public sealed class HazardSectorTests : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        DummyTicker = false,
        Connected = true,
        InLobby = true,
    };

    public static readonly EntProtoId<StellarHazardSectorRuleComponent>[] Sectors =
    [
        "HazardSectorApocryphalRule",
        "HazardSectorAzureRule",
        "HazardSectorCorporateRule",
        "HazardSectorCrystallineRule",
        "HazardSectorMagnetarRule",
        "HazardSectorMalignRule",
        "HazardSectorPelagicRule",
        "HazardSectorSupernovaRule",
    ];

    [SidedDependency(Side.Server)] private readonly StellarRandomHazardSectorRuleSystem _randomHazardSectorRule = default!;
    [SidedDependency(Side.Server)] private readonly GameTicker _ticker = default!;

    [Test]
    public async Task TestSectorStart(
        [ValueSource(nameof(Sectors))] EntProtoId<StellarHazardSectorRuleComponent> sector)
    {
        await Server.WaitAssertion(() =>
        {
            _ticker.ToggleReadyAll(true);

            _ticker.SetGamePreset("StellarHazardSectorRandom");
            _randomHazardSectorRule.ForceSector(sector);

            _ticker.StartRound();

            Assert.That(_ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            Assert.That(_ticker.PlayerGameStatuses.Values.All(x => x == PlayerGameStatus.JoinedGame));
        });

        await SyncTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SQuerySingle<StellarHazardSectorRuleComponent>(out var sectorRule));
            Assert.That(SQuerySingle<StellarMajorThreatRuleComponent>(out var threatRule));
            Assert.That(SQuerySingle<StellarShiftIntensityRuleComponent>(out var intensityRule));
            Assert.That(SQuerySingle<FTLComponent>(out var ftl), "Station is in FTL");
            
            Assert.That(threatRule!.Value.Comp.ExpectedRoundEndTime, Is.Not.EqualTo(TimeSpan.Zero), "The threat rule should have decided a round time");
            Assert.That(intensityRule!.Value.Comp.ShiftIntensityPreset, Is.Not.EqualTo(string.Empty), "Hazard sector should have a non-null shift intensity");
            Assert.That(ftl!.Value.Owner, Is.EqualTo(sectorRule!.Value.Comp.SectorStation));

            _ticker.RestartRound();
        });
    }
}
