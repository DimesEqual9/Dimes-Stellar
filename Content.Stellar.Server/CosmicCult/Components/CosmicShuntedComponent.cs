// SPDX-FileCopyrightText: 2025 AftrLite
// SPDX-FileCopyrightText: 2025 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: LicenseRef-CosmicCult

using Content.Stellar.Shared.CosmicCult.Components;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Stellar.Server.CosmicCult.Components;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CosmicShuntedComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] [AutoPausedField]
    public TimeSpan ExitVoidTime;

    [DataField] public bool ReadyToReturn;

    [DataField] public bool ConvertOnReturn;

    [DataField] public EntityUid OriginalBody;

    [DataField] public TimeSpan ShuntedDuration;

    public Entity<CosmicCultComponent> ShuntCaster;

    public Entity<CosmicCultComponent> WispGrabber;
}
