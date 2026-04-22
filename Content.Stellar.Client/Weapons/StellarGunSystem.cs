// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using System.Numerics;
using Content.Client.Animations;
using Content.Client.Items;
using Content.Shared.Damage.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Stellar.Shared.Weapons;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Animations;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Utility;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Stellar.Client.Weapons;

public sealed partial class StellarGunSystem : SharedStellarGunSystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    [Dependency] private readonly AnimationPlayerSystem _animPlayer = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly EntProtoId BulletProto = "StellarBulletBase";
    private static readonly EntProtoId HitscanProto = "StellarHitscanBase";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<StellarHitscanEvent>(OnHitscan);
        SubscribeAllEvent<StellarMuzzleFlashEvent>(OnMuzzleFlash);

        Subs.ItemStatus<StellarGunReloadableComponent>(ent => new StellarAmmoControl(ent));
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.StellarReloadGun, new PointerInputCmdHandler(OnReloadBindPressed, outsidePrediction: true))
            .Register<SharedStellarGunSystem>();
    }

    private bool OnReloadBindPressed(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (_player.LocalEntity is not { } player)
            return false;

        var gunEnt = _hands.GetActiveItem(player);
        if (gunEnt is null || !TryComp<StellarGunReloadableComponent>(gunEnt.Value, out var gunComp))
            return false;

        if (!DoAfter.IsRunning(gunComp.ReloadDoAfter) && gunComp.AmmoReserves > 0 && gunComp.AmmoCount < gunComp.AmmoMagCapacity)
        {
            SendReloadMessage(gunEnt.Value, player);
            return true;
        }

        return false;
    }

    private void OnMuzzleFlash(StellarMuzzleFlashEvent args)
    {
        var userUid = GetEntity(args.Gun);
        RenderMuzzleFlash(userUid, args.Angle, args.Prototype); // TODO: fix muzzle flash prediction
    }

    protected override void StellarMuzzleFlash(EntityUid gunUid, StellarMuzzleFlashEvent args, EntityUid? user = null)
    {
        var shooter = user ?? gunUid;
        RenderMuzzleFlash(shooter, args.Angle, args.Prototype);
    }

    private void OnHitscan(StellarHitscanEvent args)
    {
        var gunUid = GetEntity(args.Gun);
        CalculateHitscan(gunUid, args);
    }

    protected override void StellarHitscan(EntityUid gunUid, StellarHitscanEvent args, EntityUid? tracked = null)
    {
        CalculateHitscan(gunUid, args);
    }

    private void CalculateHitscan(EntityUid gunUid, StellarHitscanEvent args)
    {
        var wiggleDist = args.MaxDistance + _random.NextFloat(-0.5f, 0.5f); // This is purely visual, and makes close-range weapons look considerably better.
        var shooter = GetEntity(args.Shooter) ?? gunUid;
        var mapCords = _transform.ToMapCoordinates(GetCoordinates(args.FromCoordinates));
        var ray = new CollisionRay(mapCords.Position, args.ShotDirection, (int)args.CollisionMask);
        var rayCastResults = _physics.IntersectRay(mapCords.MapId, ray, args.MaxDistance, shooter, false);
        var target = GetEntity(args.Target);
        var result = _container.IsEntityOrParentInContainer(shooter) ? rayCastResults.FirstOrNull() : rayCastResults.FirstOrNull(hit => hit.HitEntity == target || CompOrNull<RequireProjectileTargetComponent>(hit.HitEntity)?.Active != true);
        var distanceTried = result?.Distance ?? wiggleDist;

        CreateHitscanVisuals(args.Unshaded, args.LightColor, shooter, distanceTried, args.ShotDirection.ToAngle(), args.RayVisuals);
    }

    private void CreateHitscanVisuals(bool unshaded, Color lightColor, EntityUid shooter, float distance, Angle shotAngle, SpriteSpecifier.Rsi rayVisuals)
    {
        var mod = 75f;
        var speed = distance / mod;

        var bullet = new SpriteSpecifier.Rsi(rayVisuals.RsiPath, "bullet");
        var start = new SpriteSpecifier.Rsi(rayVisuals.RsiPath, "start");
        var middle = new SpriteSpecifier.Rsi(rayVisuals.RsiPath, "middle");
        var end = new SpriteSpecifier.Rsi(rayVisuals.RsiPath, "end");

        if (distance > 1.25f) // The order these are created in matters!
        {
            RenderMiddle(shooter, shotAngle, middle, distance, speed, mod, unshaded);
            RenderStart(shooter, shotAngle, start, speed, mod, unshaded);
            RenderEnd(shooter, shotAngle, end, distance, speed, mod, unshaded);
            RenderBullet(shooter, shotAngle, bullet, lightColor, distance, speed, mod);
        }
    }
#region Rendering
    private void RenderMuzzleFlash(EntityUid shooter, Angle shotAngle, EntProtoId muzzle)
    {
        if (shooter == EntityUid.Invalid)
            return;

        var time = 1f;
        var gunXform = Transform(shooter);
        var gridUid = gunXform.GridUid;
        EntityCoordinates coordinates;

        if (TryComp(gridUid, out MapGridComponent? mapGrid))
            coordinates = new EntityCoordinates(gridUid.Value, _maps.LocalToGrid(gridUid.Value, mapGrid, gunXform.Coordinates));
        else if (gunXform.MapUid != null)
            coordinates = new EntityCoordinates(gunXform.MapUid.Value, _transform.GetWorldPosition(gunXform));
        else
            return;

        var effectEnt = Spawn(muzzle, coordinates);
        var effectSprite = Comp<SpriteComponent>(effectEnt);
        var effectLight = Comp<PointLightComponent>(effectEnt);
        var track = EnsureComp<TrackUserComponent>(effectEnt);
        track.User = shooter;

        var lightEnergy = effectLight.Energy;
        _transform.SetWorldRotationNoLerp(effectEnt, shotAngle);

        if (TryComp<TimedDespawnComponent>(effectEnt, out var despawn))
            time = despawn.Lifetime;

        var muzzleAnim = new Animation()
        {
            Length = TimeSpan.FromSeconds(time),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.Energy),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(lightEnergy, 0),
                        new AnimationTrackProperty.KeyFrame(lightEnergy*2f, time / 2),
                        new AnimationTrackProperty.KeyFrame(0f, time / 2),
                    }
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.AnimatedEnable),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(true, 0),
                        new AnimationTrackProperty.KeyFrame(false, time),
                    }
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color, 0f),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(1f), time/2),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(0f), time/2),
                    },
                },
            },
        };

        _animPlayer.Stop(effectEnt, "muzzle-effect");
        _animPlayer.Play(effectEnt, muzzleAnim, "muzzle-effect");
    }

    private void RenderBullet(EntityUid shooter, Angle shotAngle, SpriteSpecifier.Rsi sprite, Color lightColor, float distance, float speed, float mod)
    {
        if (sprite is not { } rsi || shooter == EntityUid.Invalid)
            return;

        var time = speed + mod;
        var gunXform = Transform(shooter);
        var gridUid = gunXform.GridUid;
        EntityCoordinates coordinates;

        if (TryComp(gridUid, out MapGridComponent? mapGrid))
            coordinates = new EntityCoordinates(gridUid.Value, _maps.LocalToGrid(gridUid.Value, mapGrid, gunXform.Coordinates));
        else if (gunXform.MapUid != null)
            coordinates = new EntityCoordinates(gunXform.MapUid.Value, _transform.GetWorldPosition(gunXform));
        else
            return;

        var effectEnt = Spawn(BulletProto, coordinates);
        var effectSprite = Comp<SpriteComponent>(effectEnt);
        _light.SetColor(effectEnt, lightColor);
        _transform.SetWorldRotationNoLerp(effectEnt, shotAngle);
        _sprite.LayerSetSprite((effectEnt, effectSprite), StellarHitscanLayers.Unshaded, rsi);
        _sprite.LayerSetRsiState((effectEnt, effectSprite), StellarHitscanLayers.Unshaded, rsi.RsiState);
        _sprite.SetScale((effectEnt, effectSprite), new Vector2(1f, 1f));
        _sprite.SetDrawDepth((effectEnt, effectSprite), (int)DrawDepth.OverMobs);

        var muzzleAnim = new Animation()
        {
            Length = TimeSpan.FromSeconds(time),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = StellarHitscanLayers.Unshaded,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(rsi.RsiState, (time - mod) / 500),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(0, 0f), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0.5f, 0f), time / 1000),
                        new AnimationTrackProperty.KeyFrame(new Vector2(distance - 0.25f, 0f), time / 750),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.Energy),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(1f, 0f),
                        new AnimationTrackProperty.KeyFrame(5f, time / 500),
                        new AnimationTrackProperty.KeyFrame(0f, time / 300),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(0, 0f), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0.5f, 0f), time / 1000),
                        new AnimationTrackProperty.KeyFrame(new Vector2(distance - 0.25f, 0f), time / 750),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(1, 1f), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(1f, 1f), time / 750),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0.2f, 0.5f), time / 500),
                        new AnimationTrackProperty.KeyFrame(new Vector2(0.01f, 0.1f), time / 500),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color, 0f),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(1f), time / 500),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(0f), time / 300),
                    },
                },
            },
        };
        _animPlayer.Play(effectEnt, muzzleAnim, "bullet-effect");
    }

    private void RenderStart(EntityUid shooter, Angle shotAngle, SpriteSpecifier.Rsi sprite, float speed, float mod, bool setUnshaded)
    {
        if (sprite is not { } rsi || shooter == EntityUid.Invalid)
            return;

        var time = speed + mod;
        var gunXform = Transform(shooter);
        var gridUid = gunXform.GridUid;
        EntityCoordinates coordinates;

        if (TryComp(gridUid, out MapGridComponent? mapGrid))
            coordinates = new EntityCoordinates(gridUid.Value, _maps.LocalToGrid(gridUid.Value, mapGrid, gunXform.Coordinates));
        else if (gunXform.MapUid != null)
            coordinates = new EntityCoordinates(gunXform.MapUid.Value, _transform.GetWorldPosition(gunXform));
        else
            return;

        var effectEnt = Spawn(HitscanProto, coordinates);
        var effectSprite = Comp<SpriteComponent>(effectEnt);
        _transform.SetWorldRotationNoLerp(effectEnt, shotAngle);
        _sprite.LayerSetSprite((effectEnt, effectSprite), StellarHitscanLayers.Shaded, rsi);
        _sprite.LayerSetRsiState((effectEnt, effectSprite), StellarHitscanLayers.Shaded, rsi.RsiState);
        _sprite.SetScale((effectEnt, effectSprite), new Vector2(1f, 1f));
        _sprite.SetOffset((effectEnt, effectSprite), new Vector2(0.5f, 0f));
        if (setUnshaded)
            effectSprite.LayerSetShader(StellarHitscanLayers.Shaded, "unshaded");

        var muzzleAnim = new Animation()
        {
            Length = TimeSpan.FromSeconds(time),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = StellarHitscanLayers.Shaded,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(rsi.RsiState, (time - mod) / 500),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(0.01f, 1f), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(1f, 0.5f), time / 1000),
                        new AnimationTrackProperty.KeyFrame(new Vector2(1, 1f), time / 750),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color, 0f),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(1f), time/200),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(0f), time/1000),
                    },
                },
            },
        };

        _animPlayer.Play(effectEnt, muzzleAnim, "muzzle-effect");
    }

    private void RenderMiddle(EntityUid shooter, Angle shotAngle, SpriteSpecifier.Rsi sprite, float distance, float speed, float mod, bool setUnshaded)
    {
        if (sprite is not { } rsi || shooter == EntityUid.Invalid)
            return;

        var time = speed + mod;
        var gunXform = Transform(shooter);
        var gridUid = gunXform.GridUid;
        EntityCoordinates coordinates;

        if (TryComp(gridUid, out MapGridComponent? mapGrid))
            coordinates = new EntityCoordinates(gridUid.Value, _maps.LocalToGrid(gridUid.Value, mapGrid, gunXform.Coordinates));
        else if (gunXform.MapUid != null)
            coordinates = new EntityCoordinates(gunXform.MapUid.Value, _transform.GetWorldPosition(gunXform));
        else
            return;

        var effectEnt = Spawn(HitscanProto, coordinates);
        var effectSprite = Comp<SpriteComponent>(effectEnt);
        _transform.SetWorldRotationNoLerp(effectEnt, shotAngle);
        _sprite.LayerSetSprite((effectEnt, effectSprite), StellarHitscanLayers.Shaded, rsi);
        _sprite.LayerSetRsiState((effectEnt, effectSprite), StellarHitscanLayers.Shaded, rsi.RsiState);
        _sprite.SetScale((effectEnt, effectSprite), new Vector2(1f, 1f));
        if (setUnshaded)
            effectSprite.LayerSetShader(StellarHitscanLayers.Shaded, "unshaded");

        var spriteAnim = new Animation()
        {
            Length = TimeSpan.FromSeconds(time),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = StellarHitscanLayers.Shaded,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(rsi.RsiState, (time - mod) / 500),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(0.01f, 1f), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(1f, 0.5f), time / 1000),
                        new AnimationTrackProperty.KeyFrame(new Vector2(distance - 1.25f, 1f), time / 750),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(0, 0f), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(1f, 0f), time / 1000),
                        new AnimationTrackProperty.KeyFrame(new Vector2(distance * 0.5f, 0f), time / 750),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color, 0f),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(1f), time/200),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(0f), time/1000),
                    },
                },
            },
        };
        _animPlayer.Play(effectEnt, spriteAnim, "sprite-effect");
    }

    private void RenderEnd(EntityUid shooter, Angle shotAngle, SpriteSpecifier.Rsi sprite, float distance, float speed, float mod, bool setUnshaded)
    {
        if (sprite is not { } rsi || shooter == EntityUid.Invalid)
            return;

        var time = speed + mod;
        var gunXform = Transform(shooter);
        var gridUid = gunXform.GridUid;
        EntityCoordinates coordinates;

        if (TryComp(gridUid, out MapGridComponent? mapGrid))
            coordinates = new EntityCoordinates(gridUid.Value, _maps.LocalToGrid(gridUid.Value, mapGrid, gunXform.Coordinates));
        else if (gunXform.MapUid != null)
            coordinates = new EntityCoordinates(gunXform.MapUid.Value, _transform.GetWorldPosition(gunXform));
        else
            return;

        var effectEnt = Spawn(HitscanProto, coordinates);
        var effectSprite = Comp<SpriteComponent>(effectEnt);
        _transform.SetWorldRotationNoLerp(effectEnt, shotAngle);
        _sprite.LayerSetSprite((effectEnt, effectSprite), StellarHitscanLayers.Shaded, rsi);
        _sprite.LayerSetRsiState((effectEnt, effectSprite), StellarHitscanLayers.Shaded, rsi.RsiState);
        _sprite.SetScale((effectEnt, effectSprite), new Vector2(1f, 1f));
        if (setUnshaded)
            effectSprite.LayerSetShader(StellarHitscanLayers.Shaded, "unshaded");

        var spriteAnim = new Animation()
        {
            Length = TimeSpan.FromSeconds(time),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = StellarHitscanLayers.Shaded,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(rsi.RsiState, (time - mod) / 500),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(0.01f, 1f), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(1f, 0.5f), time / 1000),
                        new AnimationTrackProperty.KeyFrame(new Vector2(1f, 1f), time / 750),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(0, 0f), 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(1.5f, 0f), time / 1000),
                        new AnimationTrackProperty.KeyFrame(new Vector2(distance - 0.25f, 0f), time / 750),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color, 0f),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(1f), time/200),
                        new AnimationTrackProperty.KeyFrame(effectSprite.Color.WithAlpha(0f), time/1000),
                    },
                },
            },
        };
        _animPlayer.Play(effectEnt, spriteAnim, "impact-effect");
    }
    #endregion

    private void SendReloadMessage(EntityUid gun, EntityUid player)
    {
        var gunEnt = GetNetEntity(gun);
        var playerEnt = GetNetEntity(player);
        RaisePredictiveEvent(new StellarManualReloadEvent(gunEnt, playerEnt));
    }
}


