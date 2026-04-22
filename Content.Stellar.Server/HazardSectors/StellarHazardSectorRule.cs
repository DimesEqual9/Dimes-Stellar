// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using System.Linq;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._ES.Lighting.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Parallax;
using Content.Shared.Weather;
using Content.Stellar.Shared._ES.Core.Timer;
using Content.Stellar.Shared.CCVars;
using Content.Stellar.Shared.HazardSectors;
using Content.Stellar.Shared.PostProcess;
using Content.Stellar.Shared.PostProcess.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Stellar.Server.HazardSectors;

public sealed class StellarHazardSectorRule : StellarGameRuleSystem<StellarHazardSectorRuleComponent>
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedWeatherSystem _weather = default!;
    [Dependency] private readonly ShuttleSystem _shuttleSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ESEntityTimerSystem _esTimer = default!;

    private float _stationWakeupTime;

    public override void Initialize()
    {
        base.Initialize();
        _config.OnValueChanged(STCCVars.StationWakeupTime, (f => _stationWakeupTime = f), true);
    }


    protected override void Started(EntityUid uid, StellarHazardSectorRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);

        if (!TryGetRandomStation(out var station))
            return;

        var gridUid = _station.GetLargestGrid(station.Value);
        if (!HasComp<MapGridComponent>(gridUid) || !TryComp<ShuttleComponent>(gridUid, out var shuttleComp))
            return;

        EnsureComp<StellarHazardSectorStationComponent>(gridUid.Value); // Marks the station for convenience.
        EnsureComp<ESTileBasedRoofComponent>(gridUid.Value); // Enables light passthrough for windows, ect.

        comp.SectorStation = gridUid.Value;
        comp.SectorMap = EnsureHazardSectorMap(comp.Parallax, comp.MapLight);
        if (comp.Weather is { } weather)
            _weather.TryAddWeather(Transform(comp.SectorMap).MapID, weather, out _, null);

        _shuttleSystem.FTLToCoordinates(gridUid.Value, shuttleComp, Transform(comp.SectorMap).Coordinates, Angle.Zero, 0f, _stationWakeupTime);

        var streamEnt = _audio.PlayPvs(comp.TravelAmbience, comp.SectorStation);
        comp.AudioStream = streamEnt?.Entity;
        _audio.SetGridAudio(streamEnt);

        _esTimer.SpawnMethodTimer(TimeSpan.FromSeconds(_stationWakeupTime), () => { comp.AudioStream = _audio.Stop(comp.AudioStream); }); // Set a timer to turn the audio off again
    }

    protected override void ActiveTick(EntityUid uid, StellarHazardSectorRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        foreach (var phase in component.AmbiencePhases.AsEnumerable().Reverse())
        {
            if (phase.Completed)
                continue;

            var phaseStart = TimeSpan.Zero;
            if (phase.TimeBeforeEnd != null)
                phaseStart = ExpectedRoundEnd() - phase.TimeBeforeEnd.Value;
            if (phase.TimeAfterStart != null)
                phaseStart = Ticker.RoundStartTimeSpan + phase.TimeAfterStart.Value;

            if (_timing.CurTime < phaseStart)
                continue;

            DoPhase(component, phase);
        }
    }

    private void DoPhase(StellarHazardSectorRuleComponent comp, StellarHazardSectorAmbienceConfig phase)
    {
        // if (phase.AnnouncementNonsense != null && comp.ThreatActive)
        // {
        //  Code for sending announcement text using screen-announcing overlay goes here!
        // }

        if (phase.ApplyLut != null)
        {
            EnsureComp<StellarPostProcessComponent>(comp.SectorMap, out var postProcessComp);
            postProcessComp.UseLut = phase.ApplyLut;
            RaiseNetworkEvent(new StellarPostProcessUpdateEvent(GetNetEntity(comp.SectorMap), phase.ApplyLut));
            Dirty(comp.SectorMap, postProcessComp);
        }

        _audio.PlayGlobal(phase.StageMusic, Filter.Broadcast(), false, AudioParams.Default);
        phase.Completed = true;
    }

    private EntityUid EnsureHazardSectorMap(string parallax, Color lightColor)
    {
        var query = AllEntityQuery<StellarHazardSectorMapComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            return uid;
        }

        var mapUid = _mapSystem.CreateMap();
        var parallaxComp = EnsureComp<ParallaxComponent>(mapUid);
        var mapLight = EnsureComp<MapLightComponent>(mapUid);
        EnsureComp<StellarHazardSectorMapComponent>(mapUid);
        _metaData.SetEntityName(mapUid, "Hazard Sector");
        mapLight.AmbientLightColor = lightColor;
        parallaxComp.Parallax = parallax;

        return mapUid;
    }
}
