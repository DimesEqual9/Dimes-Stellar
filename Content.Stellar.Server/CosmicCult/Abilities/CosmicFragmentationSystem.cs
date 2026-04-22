// SPDX-FileCopyrightText: 2025 AftrLite
// SPDX-FileCopyrightText: 2025 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: LicenseRef-CosmicCult

using Content.Server._ST.Silicons;
using Content.Server.Antag;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio.Components;
using Content.Shared.Radio;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.Laws.Components;
using Content.Stellar.Shared.CosmicCult.Components;
using Content.Stellar.Shared.CosmicCult;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Stellar.Server.CosmicCult.Abilities;

public sealed class CosmicFragmentationSystem : EntitySystem
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    private ProtoId<RadioChannelPrototype> _cultRadio = "CosmicRadio";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AILawUpdatedEvent>(OnLawInserted);

        SubscribeLocalEvent<BorgChassisComponent, MalignFragmentationEvent>(OnFragmentBorg);
        SubscribeLocalEvent<SiliconLawUpdaterComponent, MalignFragmentationEvent>(OnFragmentAi);

        SubscribeLocalEvent<CosmicCultComponent, EventCosmicFragmentation>(OnCosmicFragmentation);
    }

    private void UnEmpower(Entity<CosmicCultComponent> ent)
    {
        var comp = ent.Comp;
        comp.CosmicEmpowered = false;
        comp.CosmicSiphonQuantity = CosmicCultComponent.DefaultCosmicSiphonQuantity;
        comp.CosmicGlareRange = CosmicCultComponent.DefaultCosmicGlareRange;
        comp.CosmicGlareDuration = CosmicCultComponent.DefaultCosmicGlareDuration;
        comp.CosmicGlareStun = CosmicCultComponent.DefaultCosmicGlareStun;
        comp.CosmicImpositionDuration = CosmicCultComponent.DefaultCosmicImpositionDuration;
        comp.CosmicShuntDuration = CosmicCultComponent.DefaultCosmicShuntDuration;
        comp.CosmicShuntDelay = CosmicCultComponent.DefaultCosmicShuntDelay;
        comp.CosmicShiftWindup = CosmicCultComponent.DefaultCosmicShiftWindup;
    }

    private void OnCosmicFragmentation(Entity<CosmicCultComponent> ent, ref EventCosmicFragmentation args)
    {
        if (args.Handled || _mobState.IsIncapacitated(args.Target))
            return;

        if (HasComp<BorgChassisComponent>(args.Target) && !_mind.TryGetMind(ent, out _, out _))
            return; // Don't waste charges on borgs that ain't here.

        args.Handled = true;
        var evt = new MalignFragmentationEvent(ent, args.Target);
        RaiseLocalEvent(args.Target, ref evt);
    }

    private void OnFragmentBorg(Entity<BorgChassisComponent> ent, ref MalignFragmentationEvent args)
    {
        if (!_mind.TryGetMind(ent, out var mindId, out var mind))
            return;

        var wisp = Spawn("CosmicChantryWisp", Transform(ent).Coordinates);
        var chantry = Spawn("CosmicBorgChantry", Transform(ent).Coordinates);
        EnsureComp<CosmicChantryComponent>(chantry, out var chantryComponent);
        chantryComponent.InternalVictim = wisp;
        chantryComponent.VictimBody = ent;
        _mind.TransferTo(mindId, wisp, mind: mind);

        var mins = chantryComponent.EventTime.Minutes;
        var secs = chantryComponent.EventTime.Seconds;
        _antag.SendBriefing(wisp, Loc.GetString("cosmiccult-silicon-chantry-briefing", ("minutesandseconds", $"{mins} minutes and {secs} seconds")), Color.FromHex("#4cabb3"), null);
    }

    private void OnFragmentAi(Entity<SiliconLawUpdaterComponent> ent, ref MalignFragmentationEvent args)
    {
        var lawboard = Spawn("CosmicCultLawBoard", Transform(args.Target).Coordinates);
        _container.TryGetContainer(args.Target, "circuit_holder", out var container);
        if (container == null)
            return;
        _container.EmptyContainer(container, true);
        _container.Insert(lawboard, container, Transform(args.Target), true);
    }

    private void OnLawInserted(ref AILawUpdatedEvent args)
    {
        if (!TryComp<IntrinsicRadioTransmitterComponent>(args.Target, out var radio) || !TryComp<ActiveRadioComponent>(args.Target, out var transmitter))
            return;
        if (args.Lawset.Id == "CosmicCultLaws")
        {
            radio.Channels.Add(_cultRadio);
            transmitter.Channels.Add(_cultRadio);
            _antag.SendBriefing(args.Target, Loc.GetString("cosmiccult-silicon-subverted-briefing"), Color.FromHex("#4cabb3"), null);
        }
        else
        {
            radio.Channels.Remove(_cultRadio);
            transmitter.Channels.Remove(_cultRadio);
        }
    }
}

[ByRefEvent]
public record struct MalignFragmentationEvent(Entity<CosmicCultComponent> User, EntityUid Target);
