// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using Content.Server.Atmos.Piping.Components;
using Content.Shared._ES.Camera;
using Content.Shared.Atmos.Components;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Rounding;
using Content.Stellar.Shared._ES.Core.Timer;
using Content.Stellar.Shared.Overcharge;
using Content.Stellar.Shared.Overcharge.Components;
using Content.Stellar.Shared.Stellarator;

namespace Content.Stellar.Server.Stellarator;

public sealed partial class StellarEngineSystem : SharedStellarEngineSystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly ESEntityTimerSystem _esTimer = default!;
    [Dependency] private readonly ESScreenshakeSystem _shake = default!;
    [Dependency] private readonly SharedChatSystem _chat = default!;
    [Dependency] private readonly SharedPopupSystem _popUp = default!;
    [Dependency] private readonly StellarOverchargeSystem _overcharge = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StellarEngineCoreComponent, AtmosDeviceUpdateEvent>(OnCoreUpdated);
        SubscribeLocalEvent<StellarEngineInputComponent, InteractUsingEvent>(OnFuelRodInputInteracted);

        SubscribeLocalEvent<StellarEnginePartComponent, MapInitEvent>(OnPartInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var coreQuery = EntityQueryEnumerator<StellarEngineCoreComponent>();
        while (coreQuery.MoveNext(out var ent, out var comp))
        {
            if (comp.GracePeriod && Timing.CurTime > comp.GraceTime)
            {
                if (comp.CanHypercharge && comp.State == EngineCoreState.Overcharged)
                {
                    comp.State = EngineCoreState.Hypercharging;
                    comp.ChargeProgress = comp.ChargeMax*0.125f; // progress is set to 12.5% of the maximum value, to prevent instant-fail state when moving from Overcharged to Hypercharging.
                }
                if (comp.State == EngineCoreState.Hypercharged)
                    comp.GameOverTime = Timing.CurTime + comp.GameOverTimeHypercharged;
                else
                    comp.GameOverTime = Timing.CurTime + comp.GameOverTimeCharged;
                comp.FuelDecayMult = 1f; // Fuel decay resumes!
                comp.GracePeriod = false;
                comp.ChaosEnabled = true;

                var evt = new StellarEngineGracePeriodEndEvent();
                RaiseLocalEvent(ent, ref evt);
            }

            if (comp.ChaosEnabled && Timing.CurTime > comp.ChaosTime)
            {
                EngineChaosEvent((ent, comp));
            }
        }
    }

    private void OnCoreUpdated(Entity<StellarEngineCoreComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (ent.Comp.GracePeriod
            || ent.Comp.State == EngineCoreState.Idle
            || ent.Comp.State == EngineCoreState.Cooldown
            || ent.Comp.State == EngineCoreState.Unavailable)
            return; // The engine doesn't need to update anything.

        var timeDelta = args.dt;

        var lerpCalibration = 1d; // Used for scaling Tuners' ability to be calibrated. As time progresses, we lerp Tuners' IncrementCalibrationValue to 0, eventually making the players unable to calibrate the engine, as they "run out of time".
        switch (ent.Comp.State)
        {
            case EngineCoreState.Overcharged:
                lerpCalibration = Math.Round((ent.Comp.GameOverTime - Timing.CurTime).TotalSeconds) / Math.Round(ent.Comp.GameOverTimeCharged.TotalSeconds);
                break;
            case EngineCoreState.Hypercharging:
                lerpCalibration = Math.Round((ent.Comp.GameOverTime - Timing.CurTime).TotalSeconds) / Math.Round(ent.Comp.GameOverTimeCharged.TotalSeconds);
                break;
            case EngineCoreState.Hypercharged:
                lerpCalibration = Math.Round((ent.Comp.GameOverTime - Timing.CurTime).TotalSeconds) / Math.Round(ent.Comp.GameOverTimeHypercharged.TotalSeconds);
                break;
        }
        lerpCalibration = Math.Clamp(lerpCalibration, 0, 1);

        var updateEvent = new StellarEngineUpdateEvent(args.dt, (float)lerpCalibration, 0, 0, 0);
        RaiseLocalEvent(ent, ref updateEvent);


        ent.Comp.FuelValue -= ent.Comp.FuelDecayRate * ent.Comp.FuelDecayMult * timeDelta;
        ent.Comp.FuelValue = Math.Clamp(ent.Comp.FuelValue, ent.Comp.FuelMin, ent.Comp.FuelMax);
        if (ent.Comp.FuelValue < 1 && ent.Comp.HasFuel)
        {
            ent.Comp.HasFuel = false;

            var ejectEvent = new StellarEngineEjectFuelEvent();
            RaiseLocalEvent(ent, ref ejectEvent);

            _esTimer.SpawnMethodTimer(TimeSpan.FromSeconds(0.5), () => // Time based on the sprite anim speed
            {
                ent.Comp.AcceptingFuel = true;
            });
        }

        // If there's no fuel, the engine should be losing charge. This way, you lose charge even with 4 green Tuners.
        // If all the Tuners are red at this point, you're in big trouble.
        if (ent.Comp.FuelValue < 1)
            updateEvent.Calibrated = -1;

        // Calibrated and Near-Calibrated tuners cancel each other out. Uncalibrated tuners are a multiplicative penalty.
        var coreSum = updateEvent.Calibrated - updateEvent.NearCalibrated - updateEvent.NotCalibrated * updateEvent.NotCalibrated;

        ent.Comp.ChargeProgress += (coreSum >= 0) ? coreSum * ent.Comp.ChargeGainMult * timeDelta : coreSum * ent.Comp.ChargeDecayMult * timeDelta; // If we're gaining charge, use gainMult. If we're losing it, use decayMult.
        ent.Comp.ChargeProgress = Math.Clamp(ent.Comp.ChargeProgress, ent.Comp.ChargeMin, ent.Comp.ChargeMax);

        if (ent.Comp.State != EngineCoreState.Hypercharged) // We don't need to run this code if the engine's hypercharged.
            EngineVisualState(ent);

        // If an overcharge has been activated but progress drops to zero, it's game over!
        if (ent.Comp.HasActivatedOvercharge && ent.Comp.ChargeProgress < 1)
        {
            EngineShutdown(ent);
            Dirty(ent);
            return;
        }

        // If the overcharge has no progress and no fuel left, and the engine doesn't have a fuel rod, shut it down.
        if (ent.Comp.ChargeProgress < 1 && ent.Comp.FuelValue < 1 && !ent.Comp.HasFuel)
        {
            EngineShutdown(ent);
            Dirty(ent);
        }
    }

    #region Engine States

    private void EngineChaosEvent(Entity<StellarEngineCoreComponent> ent)
    {
        // What the fuck is Intensity, AftrLite? | If the engine is at 10% charge, this is "0.025". If it's at 100% charge, it's 0.25.
        // The value then gets a flat rate added to it based on the state of the engine, which is then used for deciding chaos event probability.
        var intensity = ent.Comp.ChargeProgress * 0.0025f;
        var chaosTimes = (0, 1);
        switch (ent.Comp.State)
        {
            case EngineCoreState.Overcharging:
                intensity += 0.3f;
                chaosTimes = (12, 18);
                break;
            case EngineCoreState.Overcharged:
                intensity += 0.5f;
                chaosTimes = (10, 16);
                break;
            case EngineCoreState.Hypercharging:
                intensity += 0.625f;
                chaosTimes = (8, 13);
                break;
            case EngineCoreState.Hypercharged:
                intensity += 0.85f;
                chaosTimes = (5, 11);
                break;
        }

        Log.Info("Activating Chaos Event!");
        ent.Comp.ChaosTime = Timing.CurTime + Random.Next(TimeSpan.FromSeconds(chaosTimes.Item1), TimeSpan.FromSeconds(chaosTimes.Item2));

        var chaosEvent = new StellarEngineChaosEvent(intensity);
        RaiseLocalEvent(ent, ref chaosEvent);
    }

    private void EngineShutdown(Entity<StellarEngineCoreComponent> ent)
    {
        var evt = new StellarEngineShutdownEvent(ent.Comp.HasFuel);
        RaiseLocalEvent(ent, ref evt);

        if (ent.Comp.HasActivatedOvercharge && ent.Comp.CurrentOvercharge is not null)
        {
            _chat.DispatchStationAnnouncement(ent, Loc.GetString("announcement-stellarator-shutdown"), Loc.GetString("announcement-stellarator-sender"));
            _overcharge.ToggleOvercharge(ent.Owner, null, OverchargeState.Disabled);
        }

        Lights.SetColor(ent, Color.Black);
        Appearance.SetData(ent, EngineCoreVisuals.Core, 50);
        _popUp.PopupCoordinates(Loc.GetString("popup-stellarator-shutdown"), Transform(ent).Coordinates, PopupType.Large);
        ent.Comp.State = EngineCoreState.Idle;
        ent.Comp.HasFuel = false; // Just to be safe, we'll reset all the fuel stuff too. The engine's shutting down, after all!
        ent.Comp.AcceptingFuel = true;
        ent.Comp.ChaosEnabled = false;
        ent.Comp.GracePeriod = false;
        ent.Comp.FuelValue = 0;
        ent.Comp.ChargeProgress = 0;
        ent.Comp.CurrentOvercharge = null;
        ent.Comp.HasActivatedOvercharge = false;
    }

    private void EngineStartup(Entity<StellarEngineCoreComponent> ent, Entity<StellarFuelRodComponent> fuel)
    {
        _shake.LerpedShake(ent, 0.3f, 0.05f, 0.0085f, 100f); // Magic numbers? Maybe. Stop squinting at it.
        ent.Comp.ChaosEnabled = true;
        ent.Comp.State = EngineCoreState.Overcharging;
        ent.Comp.ChaosTime = Timing.CurTime + Random.Next(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(18));

        var evt = new StellarEngineStartupEvent(fuel);
        RaiseLocalEvent(ent, ref evt);
    }

    private void EngineCharged(Entity<StellarEngineCoreComponent> ent)
    {
        if (ent.Comp.CurrentOvercharge is null)
            return; // what? how?

        _shake.LerpedShake(ent, 0.6f, 0.125f, 0.008f, 100f); // Magic numbers? Maybe. Don't squint at it.
        var sender = Loc.GetString("announcement-stellarator-sender");
        ent.Comp.FuelDecayMult = 0f;
        ent.Comp.FuelValue += 10f; // Gain a bump of fuel.
        ent.Comp.HasActivatedOvercharge = true;
        ent.Comp.AcceptingFuel = false;
        ent.Comp.HasFuel = true;
        ent.Comp.GracePeriod = true;
        ent.Comp.ChaosEnabled = false;

        var evt = new StellarEngineChargedEvent();
        RaiseLocalEvent(ent, ref evt);

        switch (ent.Comp.State)
        {
            case EngineCoreState.Hypercharged:
                ent.Comp.GraceTime = Timing.CurTime + ent.Comp.GracePeriodHypercharged;
                _chat.DispatchStationAnnouncement(ent, Loc.GetString(ent.Comp.CurrentOvercharge.AnnouncementTextHyper), sender);
                _overcharge.ToggleOvercharge(ent, ent.Comp.CurrentOvercharge, OverchargeState.Hypercharged);
                break;
            case EngineCoreState.Overcharged:
                ent.Comp.GraceTime = Timing.CurTime + ent.Comp.GracePeriodOvercharged;
                _chat.DispatchStationAnnouncement(ent, Loc.GetString(ent.Comp.CurrentOvercharge.AnnouncementText), sender);
                _overcharge.ToggleOvercharge(ent, ent.Comp.CurrentOvercharge, OverchargeState.Overcharged);
                break;
        }
    }

    private void EngineVisualState(Entity<StellarEngineCoreComponent> ent)
    {
        var lightColor = Color.FromHex("#000000");
        var lightIntensity = 0f;
        var lightRadius = 0f;
        var progress = 50; // this is "Off".

        if (ent.Comp.State == EngineCoreState.Hypercharged || ent.Comp.State == EngineCoreState.Overcharged)
            return; // We don't do visual appearance updates if we're overcharged or hypercharged.

        if (ent.Comp.State == EngineCoreState.Hypercharging && ent.Comp.ChargeProgress >= ent.Comp.ChargeMax)
        {
            progress = 20;
            lightRadius = 10;
            lightIntensity = 9;
            lightColor = Color.FromHex("#C21A66");
            ent.Comp.State = EngineCoreState.Hypercharged;
            EngineCharged(ent);
        }
        else if (ent.Comp.State == EngineCoreState.Overcharging && ent.Comp.ChargeProgress >= ent.Comp.ChargeMax)
        {
            progress = 10;
            lightRadius = 8;
            lightIntensity = 8;
            lightColor = Color.FromHex("#64D89C");
            ent.Comp.State = EngineCoreState.Overcharged;
            EngineCharged(ent);
        }
        else if (ent.Comp.State == EngineCoreState.Hypercharging)
        {
            progress = ContentHelpers.RoundToLevels(ent.Comp.ChargeProgress, ent.Comp.ChargeMax, 7);
            lightColor = Color.FromHex("#9F5FC6");
            lightIntensity = progress + 2;
            lightRadius = progress + 4;
            progress += 10; // offset for the hypercharge states
        }
        else if (ent.Comp.State == EngineCoreState.Overcharging)
        {
            progress = ContentHelpers.RoundToLevels(ent.Comp.ChargeProgress, ent.Comp.ChargeMax, 7);
            lightColor = Color.FromHex("#D4AA4B");
            lightIntensity = progress + 1;
            lightRadius = progress + 2;
        }
        if (ent.Comp.ChargeProgress < 1) // Gotta do this or the visuals don't shut it off properly.
            Appearance.SetData(ent, EngineCoreVisuals.Core, 50);
        else
            Appearance.SetData(ent, EngineCoreVisuals.Core, progress);
        Lights.SetColor(ent, lightColor);
        Lights.SetRadius(ent, lightRadius);
        Lights.SetEnergy(ent, lightIntensity);
    }
    #endregion

    #region Fuel stuff
    private void OnFuelRodInputInteracted(Entity<StellarEngineInputComponent> ent, ref InteractUsingEvent args)
    {
        if (!TryComp<StellarEnginePartComponent>(ent, out var part) || part.LinkedCore is not { } coreUid)
            return;

        if (TerminatingOrDeleted(args.Used) || !TryComp<StellarFuelRodComponent>(args.Used, out var fuel) || !TryComp<StellarEngineCoreComponent>(coreUid, out var coreComp))
            return;

        if (!coreComp.AcceptingFuel)
        {
            _popUp.PopupEntity(Loc.GetString("popup-stellarator-refueling-unavailable"), args.User, args.User, PopupType.MediumCaution);
            return;
        }

        args.Handled = AddFuelRod(ent, (coreUid, coreComp), (args.Used, fuel), args.User);
    }

    private bool AddFuelRod(Entity<StellarEngineInputComponent> input, Entity<StellarEngineCoreComponent> core, Entity<StellarFuelRodComponent> fuel, EntityUid user)
    {
        if (!Proto.TryIndex(fuel.Comp.ProvidedOvercharge, out var proto))
            return false;

        if (core.Comp.CurrentOvercharge != null && fuel.Comp.ProvidedOvercharge != core.Comp.CurrentOvercharge)
        {
            _popUp.PopupEntity(Loc.GetString("popup-stellarator-refueling-mismatch"), input, user, PopupType.LargeCaution);
            return false;
        }

        if (core.Comp.State == EngineCoreState.Idle) // The engine was idle, so this fuel rod dictates overcharge data.
        {
            EngineStartup(core, fuel);
        }

        core.Comp.CanHypercharge = fuel.Comp.CanHypercharge;
        core.Comp.FuelValue = fuel.Comp.FuelProvided;
        core.Comp.CurrentOvercharge = proto;
        core.Comp.AcceptingFuel = false;
        core.Comp.HasFuel = true;
        Dirty(core);

        Appearance.SetData(input, EngineInputVisuals.EngineDisplay, input.Comp.States[proto.ID]);
        Appearance.SetData(input, EngineInputVisuals.EngineInput, EngineInputDoorState.Closing);
        _esTimer.SpawnMethodTimer(input.Comp.DoorAnimTime, () => { Appearance.SetData(input, EngineInputVisuals.EngineInput, EngineInputDoorState.Closed); });
        _popUp.PopupEntity(Loc.GetString("popup-stellarator-refueling-inserted", ("name", Identity.Name(fuel.Owner, EntityManager))), input, user, PopupType.Medium);
        // TODO: sound effects, visual effects.
        QueueDel(fuel);
        return true;
    }
    #endregion
}


