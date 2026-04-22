// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using System.Numerics;
using Content.Shared._ES.Camera;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Effects;
using Content.Shared.Hands;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Stellar.Shared.Weapons;

public abstract partial class SharedStellarGunSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] protected readonly SharedAudioSystem Audio = default!;
    [Dependency] protected readonly SharedDoAfterSystem DoAfter = default!;
    [Dependency] protected readonly SharedPhysicsSystem Physics = default!;
    [Dependency] protected readonly SharedPopupSystem PopUp = default!;
    [Dependency] protected readonly SharedTransformSystem TransformSystem = default!;

    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly ESScreenshakeSystem _shake = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeTypes();

        SubscribeAllEvent<StellarManualReloadEvent>(OnManualReload);

        SubscribeLocalEvent<StellarGunHitscanComponent, HitscanTraceEvent>(OnHitscan);
        SubscribeLocalEvent<StellarGunHitscanComponent, HitscanRaycastFiredEvent>(OnHitscanHit);
        SubscribeLocalEvent<StellarGunHitscanComponent, HitscanDamageDealtEvent>(OnHitscanDamageDealt);

        SubscribeLocalEvent<StellarGunReloadableComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<StellarGunReloadableComponent, StellarGunShotEvent>(OnGunShot);

        SubscribeLocalEvent<StellarGunReloadableComponent, ItemUnwieldedEvent>(OnUnwielded);
        SubscribeLocalEvent<StellarGunReloadableComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<StellarGunReloadableComponent, HandDeselectedEvent>(OnDeselected);
        SubscribeLocalEvent<StellarGunReloadableComponent, StellarAmmoReloadDoAfter>(OnReload);
    }

    private void OnManualReload(StellarManualReloadEvent msg, EntitySessionEventArgs args)
    {
        var gunEnt = GetEntity(msg.Gun);
        var playerEnt = GetEntity(msg.Player);

        if (!TryComp<StellarGunReloadableComponent>(gunEnt, out var gunComp) || gunComp.AmmoReserves == 0)
            return;

        var doArgs = new DoAfterArgs(EntityManager, playerEnt, gunComp.ReloadTime, new StellarAmmoReloadDoAfter(), gunEnt, gunEnt)
        {
            Hidden = true,
            BreakOnMove = false,
            BreakOnWeightlessMove = false,
        };

        if (DoAfter.TryStartDoAfter(doArgs, out var doAfterId))
        {
            if (Audio.PlayPredicted(gunComp.SoundReload, gunEnt, playerEnt) is { } reloadEntity)
                gunComp.ReloadAudioStream = reloadEntity.Entity;
            gunComp.ReloadDoAfter = doAfterId;
        }
        Dirty(gunEnt, gunComp);
    }

    private void OnUnwielded(Entity<StellarGunReloadableComponent> ent, ref ItemUnwieldedEvent args)
    {
        if (DoAfter.IsRunning(ent.Comp.ReloadDoAfter))
            DoAfter.Cancel(ent.Comp.ReloadDoAfter);
    }

    private void OnUnequipped(Entity<StellarGunReloadableComponent> ent, ref GotUnequippedHandEvent args)
    {
        if (DoAfter.IsRunning(ent.Comp.ReloadDoAfter))
            DoAfter.Cancel(ent.Comp.ReloadDoAfter);
    }

    private void OnDeselected(Entity<StellarGunReloadableComponent> ent, ref HandDeselectedEvent args)
    {
        if (DoAfter.IsRunning(ent.Comp.ReloadDoAfter))
            DoAfter.Cancel(ent.Comp.ReloadDoAfter);
    }

    private void OnReload(Entity<StellarGunReloadableComponent> ent, ref StellarAmmoReloadDoAfter args)
    {
        if (args.Handled || ent.Comp.AmmoCount == null || ent.Comp.AmmoMagCapacity == null || ent.Comp.AmmoReserves == null)
            return;

        if (args.Cancelled)
        {
            Audio.Stop(ent.Comp.ReloadAudioStream);
            return;
        }

        var ammoNeeded = Math.Clamp(ent.Comp.AmmoMagCapacity.Value - ent.Comp.AmmoCount.Value, 0, (ent.Comp.AmmoPerReload == null) ? ent.Comp.AmmoMagCapacity.Value : ent.Comp.AmmoPerReload.Value);
        var ammoAvailable = Math.Clamp(ent.Comp.AmmoReserves.Value, 0, ammoNeeded);
        ent.Comp.AmmoReserves -= ammoAvailable;
        ent.Comp.AmmoCount += ammoAvailable;
        args.Handled = true;

        if (ent.Comp.AmmoCount != ent.Comp.AmmoMagCapacity && ent.Comp.AmmoReserves > 0)
        {
            if (Audio.PlayPredicted(ent.Comp.SoundReload, ent, args.User) is { } reloadEntity)
                ent.Comp.ReloadAudioStream = reloadEntity.Entity;
            args.Repeat = true; // Continue reloading until we're topped up.
        }
        Dirty(ent);
    }

    private void OnAttemptShoot(Entity<StellarGunReloadableComponent> ent, ref AttemptShootEvent args)
    {
        if (!TryComp<GunComponent>(ent, out var gunComp))
            return;

        if (gunComp.ShotCounter != 0 && gunComp.SelectedMode == SelectiveFire.SemiAuto)
        {
            args.Cancelled = true;
            return;
        }

        if (gunComp.ShotCounter >= gunComp.ShotsPerBurst && gunComp.SelectedMode == SelectiveFire.Burst)
        {
            args.Cancelled = true;
            return;
        }

        if (ent.Comp.AmmoCount <= 0)
        {
            args.Cancelled = true;
            gunComp.NextFire = TimeSpan.FromSeconds(Math.Max(gunComp.NextFire.TotalSeconds + 0.5f, gunComp.NextFire.TotalSeconds));
            PopUp.PopupCursor((ent.Comp.AmmoReserves <= 0) ? Loc.GetString("stellar-ammo-reserves-empty") : Loc.GetString("stellar-ammo-magazine-empty"), PopupType.Medium);
            Audio.PlayPredicted(ent.Comp.SoundEmpty, ent, args.User);
            Dirty(ent, gunComp);
            return;
        }

        if (DoAfter.IsRunning(ent.Comp.ReloadDoAfter) && ent.Comp.AmmoCount > 0)
        {
            DoAfter.Cancel(ent.Comp.ReloadDoAfter);
            Dirty(ent);
        }
        else if (DoAfter.IsRunning(ent.Comp.ReloadDoAfter))
            args.Cancelled = true;
    }

    private void OnGunShot(Entity<StellarGunReloadableComponent> ent, ref StellarGunShotEvent args)
    {
        if (args.Gun is null || args.To is null || ent.Comp.AmmoMagCapacity is null || ent.Comp.AmmoCount is null)
            return;

        var fromMap = TransformSystem.ToMapCoordinates(args.From).Position;
        var toMap = TransformSystem.ToMapCoordinates(args.To.Value).Position;
        var mapDirection = toMap - fromMap;
        var gunShakeTranslate = new ESScreenshakeParameters() { Trauma = 0.9f * args.Gun.CameraRecoilScalarModified, DecayRate = 15f, Frequency = 0.008f, Direction = mapDirection.Normalized()};
        var gunShakeRotate = new ESScreenshakeParameters() { Trauma = 0.075f * args.Gun.CameraRecoilScalarModified, DecayRate = 25f, Frequency = 0.012f};
        var audioLerp = Math.Clamp(1.8f - (float)ent.Comp.AmmoCount.Value / (float)ent.Comp.AmmoMagCapacity.Value * 1.8f, 1f, 1.8f); // Slightly magic numbers. Used for audio pitch-up for the latter portion of a gun's ammo reserves, building as ammo depletes.
        var shotParams = AudioParams.Default
            .WithPitchScale(audioLerp)
            .WithVariation(0.066f);

        _shake.Screenshake(args.UserUid, gunShakeTranslate, gunShakeRotate);
        Audio.PlayPredicted(args.Gun.SoundGunshotModified, args.GunUid, args.UserUid, ent.Comp.ModulatePitch ? shotParams : null);
        if (ent.Comp.AmmoCount == 0)
            Audio.PlayPredicted(ent.Comp.SoundLast, args.GunUid, args.UserUid);

        var angle = GetRecoilAngle(Timing.CurTime, args.GunUid, args.Gun, mapDirection.ToAngle());
        toMap = fromMap + angle.ToVec() * mapDirection.Length();
        mapDirection = toMap - fromMap;


        // Ramping firerate for guns that do that!
        if (ent.Comp.RampingFireRate is not null)
        {
            var lerp = Math.Clamp(args.Gun.ShotCounter / ent.Comp.RampingBulletsNeeded, 0, 1);
            args.Gun.FireRateModified = MathHelper.Lerp(args.Gun.FireRate, ent.Comp.RampingFireRate.Value, lerp);
        }

        if (ent.Comp.ShootingMethod == StellarGunMethod.Hitscan && Timing.IsFirstTimePredicted)
            ShootHitscan(ent, args.From, mapDirection, args.UserUid, args.Gun.Target, Spawn(ent.Comp.Shootable));

        if (ent.Comp.ShootingMethod == StellarGunMethod.Projectile && Timing.IsFirstTimePredicted)
            ShootProjectile(ent, mapDirection, args.UserUid);

        if (ent.Comp.MuzzleFlash != null && Timing.IsFirstTimePredicted)
        {
            var ev = new StellarMuzzleFlashEvent(GetNetEntity(ent), ent.Comp.MuzzleFlash, mapDirection.ToAngle());
            StellarMuzzleFlash(args.GunUid, ev, args.UserUid);
        }
        Dirty(args.GunUid, args.Gun);
    }

    private void ShootHitscan(Entity<StellarGunReloadableComponent> ent, NetCoordinates from, Vector2 direction, EntityUid user, EntityUid? target, EntityUid ammo)
    {
        if (ent.Comp.MultiShotAmount > 1)
        {
            var angles = LinearSpread(direction.ToAngle() - ent.Comp.MultiShotSpread / 2, direction.ToAngle() + ent.Comp.MultiShotSpread / 2, ent.Comp.MultiShotAmount);

            for (var i = 0; i < ent.Comp.MultiShotAmount; i++)
            {
                var angleWiggle = _random.NextAngle(ent.Comp.MultiShotWiggleMin, ent.Comp.MultiShotWiggleMax) + angles[i];
                var hitscanEv = new HitscanTraceEvent
                {
                    FromCoordinates = GetCoordinates(from), ShotDirection = angleWiggle.ToVec(), Gun = ent, Shooter = user, Target = target,
                };
                RaiseLocalEvent(ammo, ref hitscanEv);
            }
        }
        else
        {
            var hitscanEv = new HitscanTraceEvent
            {
                FromCoordinates = GetCoordinates(from), ShotDirection = direction.Normalized(), Gun = ent, Shooter = user, Target = target,
            };
            RaiseLocalEvent(ammo, ref hitscanEv);
        }
    }

    private void ShootProjectile(Entity<StellarGunReloadableComponent> ent, Vector2 direction, EntityUid user)
    {
        var initialSpeed = Physics.GetMapLinearVelocity(user);
        if (ent.Comp.MultiShotAmount > 1)
        {
            var angles = LinearSpread(direction.ToAngle() - ent.Comp.MultiShotSpread / 2, direction.ToAngle() + ent.Comp.MultiShotSpread / 2, ent.Comp.MultiShotAmount);

            for (var i = 0; i < ent.Comp.MultiShotAmount; i++)
            {
                var angleWiggle = _random.NextAngle(ent.Comp.MultiShotWiggleMin, ent.Comp.MultiShotWiggleMax) + angles[i];
                var projectileEv = new StellarProjectileEvent(angleWiggle.ToVec(), initialSpeed, ent, user);
                RaiseLocalEvent(ent, ref projectileEv);
            }
        }
        else
        {
            var projectileEv = new StellarProjectileEvent(direction, initialSpeed, ent, user);
            RaiseLocalEvent(ent, ref projectileEv);
        }
    }

    private void OnHitscan(Entity<StellarGunHitscanComponent> ent, ref HitscanTraceEvent args)
    {
        var ev = new StellarHitscanEvent(
            ent.Comp.CollisionMask,
            GetNetCoordinates(args.FromCoordinates),
            args.ShotDirection,
            GetNetEntity(args.Gun),
            GetNetEntity(args.Shooter),
            GetNetEntity(args.Target),
            ent.Comp.Unshaded,
            ent.Comp.LightColor,
            ent.Comp.MaxDistance,
            ent.Comp.Ray);
        StellarHitscan(args.Gun, ev, args.Shooter);
    }

    private void OnHitscanHit(Entity<StellarGunHitscanComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Data.HitEntity == null)
            return;

        var originPos = TransformSystem.ToMapCoordinates(Transform(args.Data.Gun).Coordinates).Position;
        var targetPos = TransformSystem.ToMapCoordinates(Transform(args.Data.HitEntity.Value).Coordinates).Position;
        var distance = Math.Clamp((targetPos - originPos).Length(), ent.Comp.MinDistance, ent.Comp.MaxDistance);
        var dmg = (distance > ent.Comp.MinDistance) ? ent.Comp.Damage * Math.Pow(ent.Comp.FalloffModifier, distance / ent.Comp.MaxDistance) : ent.Comp.Damage;

        if(!_damage.TryChangeDamage(args.Data.HitEntity.Value, dmg, out var damageDealt, origin: args.Data.Shooter))
            return;

        var damageEvent = new HitscanDamageDealtEvent { Target = args.Data.HitEntity.Value, DamageDealt = damageDealt };
        RaiseLocalEvent(ent, ref damageEvent);
    }

    private void OnHitscanDamageDealt(Entity<StellarGunHitscanComponent> ent, ref HitscanDamageDealtEvent args)
    {
        if (Deleted(args.Target))
            return;

        if (ent.Comp.HitColor != null && args.DamageDealt.GetTotal() != 0 && _netManager.IsServer)
        {
            _color.RaiseEffect(ent.Comp.HitColor.Value,
                new List<EntityUid> { args.Target },
                Filter.Pvs(args.Target, entityManager: EntityManager));
        }
    }

    private Angle GetRecoilAngle(TimeSpan curTime, EntityUid uid, GunComponent comp, Angle direction)
    {
        var timeSinceLastFire = (curTime - comp.LastFire).TotalSeconds;
        var newTheta = MathHelper.Clamp(comp.CurrentAngle.Theta + comp.AngleIncreaseModified.Theta - comp.AngleDecayModified.Theta * timeSinceLastFire, comp.MinAngleModified.Theta, comp.MaxAngleModified.Theta);
        comp.CurrentAngle = new Angle(newTheta);
        comp.LastFire = comp.NextFire;
        DirtyFields(uid, comp, null, nameof(GunComponent.CurrentAngle), nameof(GunComponent.LastFire));

        var random = _random.NextFloat(-0.5f, 0.5f);
        var angle = new Angle(direction.Theta + comp.CurrentAngle.Theta * random);
        return angle;
    }

    private Angle[] LinearSpread(Angle start, Angle end, int intervals)
    {
        var angles = new Angle[intervals];
        for (var i = 0; i <= intervals - 1; i++)
        {
            angles[i] = new Angle(start + (end - start) * i / (intervals - 1));
        }

        return angles;
    }

    protected abstract void StellarHitscan(EntityUid gunUid, StellarHitscanEvent message, EntityUid? user = null);

    protected abstract void StellarMuzzleFlash(EntityUid gunUid, StellarMuzzleFlashEvent message, EntityUid? user = null);
}

public enum StellarHitscanLayers : byte
{
    Unshaded,
    Shaded,
}

[Serializable, NetSerializable]
public sealed class StellarMuzzleFlashEvent : EntityEventArgs
{
    public NetEntity Gun;
    public string Prototype;
    public Angle Angle;

    public StellarMuzzleFlashEvent(NetEntity gun, string prototype, Angle angle)
    {
        Gun = gun;
        Prototype = prototype;
        Angle = angle;
    }
}

[Serializable, NetSerializable]
public sealed class StellarHitscanEvent : EntityEventArgs
{
    public CollisionGroup CollisionMask;

    public NetCoordinates FromCoordinates;

    public Vector2 ShotDirection;

    public NetEntity Gun;

    public NetEntity? Shooter;

    public NetEntity? Target;

    public bool Unshaded;

    public Color LightColor;

    public float MaxDistance;

    /// <summary>
    /// RSI containing the appropriate sprites for the hitscan- expecting "start", "middle", "end", and "bullet" states.
    /// </summary>
    public SpriteSpecifier.Rsi RayVisuals;

    public EntProtoId? MuzzleFlash;

    public StellarHitscanEvent(CollisionGroup collisionMask, NetCoordinates fromCoords, Vector2 shotDirection, NetEntity gun, NetEntity? shooter, NetEntity? target, bool unshaded, Color lightColor, float maxDist, SpriteSpecifier.Rsi rayVisuals)
    {
        CollisionMask = collisionMask;
        FromCoordinates = fromCoords;
        ShotDirection = shotDirection;
        Gun = gun;
        Shooter = shooter;
        Target = target;
        Unshaded = unshaded;
        LightColor = lightColor;
        MaxDistance = maxDist;
        RayVisuals = rayVisuals;
    }
}

[ByRefEvent]
public record struct StellarProjectileEvent(Vector2 Direction, Vector2 InitialSpeed, Entity<StellarGunReloadableComponent> Gun, EntityUid User);

[Serializable, NetSerializable]
public sealed partial class StellarAmmoReloadDoAfter : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed class StellarManualReloadEvent : EntityEventArgs
{
    public NetEntity Gun;
    public NetEntity Player;

    public StellarManualReloadEvent(NetEntity gun, NetEntity player)
    {
        Gun = gun;
        Player = player;
    }
}
