// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using Content.Shared.Throwing;
using Robust.Shared.GameStates;

namespace Content.Stellar.Shared.Throwing;

[RegisterComponent, NetworkedComponent]
public sealed partial class StellarModifyThrownComponent : Component
{
    [DataField] public float ThrowSpeed = ThrowingSystem.ESThrowSpeedDefault;
}
