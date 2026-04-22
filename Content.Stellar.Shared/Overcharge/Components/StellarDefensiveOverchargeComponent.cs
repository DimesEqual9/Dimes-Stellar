// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using Content.Shared.Weather;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Stellar.Shared.Overcharge.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class StellarDefensiveOverchargeComponent : Component
{
    /// <summary>
    /// The chances per overcharge state for likelihood that a meteor will be nullified
    /// </summary>
    [DataField]
    public Dictionary<OverchargeState, float> OverchargeChances = new()
    {
        { OverchargeState.Disabled, 0f },
        { OverchargeState.Overcharged, 0.5f },
        { OverchargeState.Hypercharged, 0.99f },
    };

    /// <summary>
    /// The weather that will be set per overcharge state
    /// </summary>
    [DataField]
    public Dictionary<OverchargeState, EntProtoId<WeatherStatusEffectComponent>> OverchargeWeathers = new()
    {
        { OverchargeState.Overcharged, "StellarWeatherStationShield" },
        { OverchargeState.Hypercharged, "StellarWeatherStationHyperShield" },
    };

    [DataField]
    public EntProtoId<WeatherStatusEffectComponent>? PreviousWeather;
}
