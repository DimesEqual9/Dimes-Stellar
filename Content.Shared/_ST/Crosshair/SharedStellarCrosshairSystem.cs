// SPDX-FileCopyrightText: 2025 DrSmugLeaf | RMC-14
//
// SPDX-License-Identifier: MIT

using Content.Shared.Wieldable.Components;
using Robust.Shared.Utility;

namespace Content.Shared._ST.Crosshair;

public sealed class SharedStellarCrosshairSystem : EntitySystem
{
    public SpriteSpecifier.Rsi? GetCrosshair(Entity<StellarCrosshairComponent?> crosshair)
    {
        // Require the held item to be wielded (this keeps existing behavior).
        if (!Resolve(crosshair, ref crosshair.Comp, false))
            return null;

        if (TryComp(crosshair.Owner, out WieldableComponent? wieldable))
        {
            if (!crosshair.Comp.MustWield || wieldable.Wielded)
                return crosshair.Comp?.Rsi;
            return null;
        }

        return crosshair.Comp?.Rsi;
    }
}
