// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using Content.Shared.Throwing;

namespace Content.Stellar.Shared.Throwing;

public sealed class SharedStellarModifyThrownSystem : EntitySystem
{

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StellarModifyThrownComponent, BeforeThrownEvent>(OnBeforeThrow);
    }

    private void OnBeforeThrow(Entity<StellarModifyThrownComponent> ent, ref BeforeThrownEvent args)
    {
        args.ThrowSpeed = ent.Comp.ThrowSpeed;
        Dirty(ent);
    }
}
