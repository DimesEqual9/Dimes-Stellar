// SPDX-FileCopyrightText: 2025 DrSmugLeaf | RMC-14
//
// SPDX-License-Identifier: MIT

using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._ST.Crosshair;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StellarCrosshairComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public SpriteSpecifier.Rsi? Rsi;

    [DataField, AutoNetworkedField] public bool MustWield = true;
}
