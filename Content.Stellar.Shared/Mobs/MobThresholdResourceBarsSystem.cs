// SPDX-FileCopyrightText: 2025 Janet Blackquill
//
// SPDX-License-Identifier: MIT

using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mobs;
using Content.Stellar.Shared.ResourceBars;

namespace Content.Stellar.Shared.Mobs;

public sealed class MobThresholdResourceBarsSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly SharedResourceBarsSystem _resourceBars = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobThresholdResourceBarsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MobThresholdResourceBarsComponent, MobThresholdChecked>(OnMobThresholdChecked);
    }

    private void OnShutdown(Entity<MobThresholdResourceBarsComponent> ent, ref ComponentShutdown args)
    {
        _resourceBars.ClearResourceBarCategory(ent.Owner, ent.Comp.ResourceBarCategory);
    }

    private void OnMobThresholdChecked(Entity<MobThresholdResourceBarsComponent> ent, ref MobThresholdChecked args)
    {
        if (!ent.Comp.ResourceBars.TryGetValue(args.MobState.CurrentState, out var resourceBar))
        {
            _resourceBars.ClearResourceBarCategory(ent.Owner, ent.Comp.ResourceBarCategory);
            return;
        }

        if (!_mobThreshold.TryGetNextState(ent, args.MobState.CurrentState, out var nextState, args.Threshold) ||
            !_mobThreshold.TryGetThresholdForState(ent, args.MobState.CurrentState, out var currentThreshold, args.Threshold) ||
            !_mobThreshold.TryGetThresholdForState(ent, nextState.Value, out var nextThreshold, args.Threshold))
        {
            _resourceBars.ShowResourceBar(ent.Owner, resourceBar, 0f);
            return;
        }

#pragma warning disable CA2200
        var totalDamage = _damageable.GetTotalDamage((ent, args.Damageable));
#pragma warning restore CA2200
        var percentage = (totalDamage - currentThreshold.Value) / (nextThreshold.Value - currentThreshold.Value);

        _resourceBars.ShowResourceBar(ent.Owner, resourceBar, 1f - percentage.Float());
    }
}
