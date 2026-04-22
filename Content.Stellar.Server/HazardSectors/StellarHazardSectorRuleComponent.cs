// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using Content.Shared.Weather;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Stellar.Server.HazardSectors;

/// <summary>
/// Gamerule Component for use in Hazard Sectors.
/// </summary>
[RegisterComponent, Access(typeof(StellarHazardSectorRule))]
public sealed partial class StellarHazardSectorRuleComponent : Component
{
    [DataField, AlwaysPushInheritance]
    public List<StellarHazardSectorAmbienceConfig> AmbiencePhases = new();

    /// <summary>
    /// What parallax to use for the hazard sector's background.
    /// </summary>
    [DataField(required: true)]
    public string Parallax;

    /// <summary>
    /// Weather that plays on the station during this hazard sector. Passed along to StellarHazardSectorStationComponent.
    /// </summary>
    [DataField]
    public EntProtoId<WeatherStatusEffectComponent>? Weather;

    /// <summary>
    /// What light color to use for the hazard sector's environment.
    /// </summary>
    [DataField(required: true)]
    public Color MapLight;

    /// <summary>
    /// The currently active Hazard Sector Map EntityUid.
    /// </summary>
    public EntityUid SectorMap;

    /// <summary>
    /// The currently active Station Grid EntityUid.
    /// </summary>
    public EntityUid SectorStation;

    /// <summary>
    /// Sound played on loop during bluespace travel.
    /// </summary>
    [DataField]
    public SoundSpecifier? TravelAmbience = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_progress.ogg")
    {
        Params = AudioParams.Default.WithVolume(-3f).WithLoop(true)
    };

    /// <summary>
    /// Entity used for Audio Streaming.
    /// </summary>
    public EntityUid? AudioStream;
}

[DataDefinition]
public sealed partial class StellarHazardSectorAmbienceConfig
{
    public bool Completed = false;

    [DataField]
    public TimeSpan? TimeAfterStart;

    [DataField]
    public TimeSpan? TimeBeforeEnd;

    [DataField]
    public SoundSpecifier? StageMusic;

    [DataField]
    public bool? FlickerLights;

    [DataField]
    public string? ApplyLut;
}
