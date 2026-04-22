// SPDX-FileCopyrightText: 2025 AftrLite
// SPDX-FileCopyrightText: 2025 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: LicenseRef-CosmicCult

using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Stellar.Shared.CosmicCult.Components;

/// <summary>
/// Marker component for entities to interact with The Monument inside The Void.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CosmicMonumentComponent : Component;

[Serializable, NetSerializable]
public enum MonumentVisuals : byte
{
    Status,
}

[Serializable, NetSerializable]
public enum MonumentStatus : byte
{
    Idle,
    Finale,
}
