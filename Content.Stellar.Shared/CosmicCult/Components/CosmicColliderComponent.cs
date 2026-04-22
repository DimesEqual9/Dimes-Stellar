// SPDX-FileCopyrightText: 2025 AftrLite
// SPDX-FileCopyrightText: 2025 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: LicenseRef-CosmicCult

using Robust.Shared.GameStates;

namespace Content.Stellar.Shared.CosmicCult.Components;

/// <summary>
/// Marker component for entities that cult-related entities can walk through but are solid to others.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CosmicColliderComponent : Component;
