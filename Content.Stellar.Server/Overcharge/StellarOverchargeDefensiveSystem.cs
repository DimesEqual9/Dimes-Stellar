// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using Content.Server.Destructible;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station;
using Content.Shared.Weather;
using Content.Stellar.Shared.Overcharge;
using Content.Stellar.Shared.Overcharge.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Stellar.Server.Overcharge;

public sealed class StellarOverchargeDefensiveSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly DestructibleSystem _destructible = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedWeatherSystem _weather = default!;
    [Dependency] private readonly SharedStationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StellarDefensiveOverchargeComponent, StellarToggleOverchargeEvent>(OnOvercharged);
        SubscribeLocalEvent<StellarDefensiveOverchargeComponent, StellarDefendableMeteorCollidedEvent>(OnDefensiveCollide);
        SubscribeLocalEvent<StellarDefendableMeteorComponent, StartCollideEvent>(OnMeteorCollide);
    }

    private void OnOvercharged(Entity<StellarDefensiveOverchargeComponent> ent, ref StellarToggleOverchargeEvent args)
    {
        if (_station.GetLargestGrid(ent.Owner) is not { } grid)
            return;

        var mapID = Transform(grid).MapID;
        if (ent.Comp.PreviousWeather is { } previous)
        {
            _weather.TryRemoveWeather(mapID, previous);
            ent.Comp.PreviousWeather = null;
        }

        if (!ent.Comp.OverchargeWeathers.TryGetValue(args.State, out var weather))
            return;

        _weather.TryAddWeather(mapID, weather, out _, null);
        ent.Comp.PreviousWeather = weather;
    }

    private void OnMeteorCollide(Entity<StellarDefendableMeteorComponent> ent, ref StartCollideEvent args)
    {
        if (TerminatingOrDeleted(args.OtherEntity) || TerminatingOrDeleted(ent))
            return;
        var station = _station.GetOwningStation(args.OtherEntity);

        if (ent.Comp.HitList.Contains(args.OtherEntity) || station == null)
            return;

        var evt = new StellarDefendableMeteorCollidedEvent(false);
        RaiseLocalEvent(station.Value, ref evt);

        if (evt.Defended)
        {
            var pos = _transform.GetMapCoordinates(ent);
            var filter = Filter.Empty().AddInRange(pos, 65f);
            _audio.PlayEntity(ent.Comp.MitigationSfx, filter, args.OtherEntity, false, AudioParams.Default.WithVariation(0.3f));
            ent.Comp.HitList.Add(args.OtherEntity);
            QueueDel(ent);
        }

        FixedPoint2 threshold;
        if (_mobThreshold.TryGetDeadThreshold(args.OtherEntity, out var mobThreshold))
            threshold = mobThreshold.Value;
        else if (_destructible.TryGetDestroyedAt(args.OtherEntity, out var destroyThreshold))
            threshold = destroyThreshold.Value;
        else
            threshold = FixedPoint2.MaxValue;

        var otherEntDamage = _damageable.GetTotalDamage(args.OtherEntity);
        // account for the damage that the other entity has already taken: don't overkill
        threshold -= otherEntDamage;

        // The max amount of damage our meteor can take before breaking.
        var maxMeteorDamage = _destructible.DestroyedAt(ent.Owner) - _damageable.GetTotalDamage(ent.Owner);

        // Cap damage so we don't overkill the meteor
        var trueDamage = FixedPoint2.Min(maxMeteorDamage, threshold);

        var damage = ent.Comp.DamageTypes * trueDamage;
        _damageable.TryChangeDamage(args.OtherEntity, damage, true, origin: ent);
        _damageable.TryChangeDamage(ent.Owner, damage);

        if (!TerminatingOrDeleted(args.OtherEntity))
            ent.Comp.HitList.Add(args.OtherEntity);
    }

    private void OnDefensiveCollide(Entity<StellarDefensiveOverchargeComponent> ent,
        ref StellarDefendableMeteorCollidedEvent args)
    {
        if (!TryComp<StellarOverchargeableComponent>(ent, out var overcharge))
            return;

        if (!ent.Comp.OverchargeChances.TryGetValue(overcharge.State, out var chance) || !_random.Prob(chance))
            return;

        args.Defended = true;
    }
}
