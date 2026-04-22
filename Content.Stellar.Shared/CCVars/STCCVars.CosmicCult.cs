// SPDX-FileCopyrightText: 2025 AftrLite
//
// SPDX-License-Identifier: LicenseRef-CosmicCult

using Robust.Shared.Configuration;

namespace Content.Stellar.Shared.CCVars;

[CVarDefs]
public sealed partial class STCCVars
{
    /// <summary>
    /// IN MINUTES, the time upon which the Finale always activates. Default is 90 minutes.
    /// If your rounds are less than 2 hours long, i recommend matching this to your Shuttle's auto-call time.
    /// </summary>
    public static readonly CVarDef<int> CosmicCultFinaleTargetTime =
        CVarDef.Create("cosmiccult.target_finale_time_target", 90, CVar.SERVER);
}
