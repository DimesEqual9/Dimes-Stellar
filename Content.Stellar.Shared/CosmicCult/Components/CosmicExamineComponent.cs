// SPDX-FileCopyrightText: 2025 AftrLite
// SPDX-FileCopyrightText: 2025 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: LicenseRef-CosmicCult

namespace Content.Stellar.Shared.CosmicCult.Components;

[RegisterComponent]
public sealed partial class CosmicExamineComponent : Component
{
    [DataField(required: true)]
    public LocId CultistText;

    [DataField]
    public LocId OthersText = "cosmic-examine-text-structures";
}
