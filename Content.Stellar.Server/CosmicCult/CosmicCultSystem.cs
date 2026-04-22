// SPDX-FileCopyrightText: 2025 AftrLite
// SPDX-FileCopyrightText: 2025 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: LicenseRef-CosmicCult

using System.Linq;
using Content.Server.Antag;
using Content.Shared._ES.Camera;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Systems;
using Content.Shared.Random.Helpers;
using Content.Shared.StatusEffectNew;
using Content.Stellar.Shared._ES.Core.Timer;
using Content.Stellar.Shared.CosmicCult.Components;
using Content.Stellar.Shared.CosmicCult;
using Content.Stellar.Shared.CosmicCult.Prototypes;

namespace Content.Stellar.Server.CosmicCult;

public sealed partial class CosmicCultSystem : SharedCosmicCultSystem
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly ESEntityTimerSystem _entityTimer = default!;
    [Dependency] private readonly ESScreenshakeSystem _shake = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CosmicCultComponent, ComponentInit>(OnStartCultist);

        SubscribeLocalEvent<CosmicImposingComponent, ComponentInit>(OnStartImposition);
        SubscribeLocalEvent<CosmicImposingComponent, ComponentRemove>(OnEndImposition);
        SubscribeLocalEvent<InfluenceStrideComponent, ComponentInit>(OnStartInfluenceStride);
        SubscribeLocalEvent<InfluenceStrideComponent, ComponentRemove>(OnEndInfluenceStride);

        SubscribeLocalEvent<InfluenceStrideComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMoveSpeed);
        SubscribeLocalEvent<CosmicImposingComponent, RefreshMovementSpeedModifiersEvent>(OnImpositionMoveSpeed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var finaleQuery = EntityQueryEnumerator<InfluenceVitalityComponent>();
        while (finaleQuery.MoveNext(out var uid, out var comp))
        {
            if (Timing.CurTime >= comp.CheckTimer)
            {

                _damage.TryChangeDamage(uid, comp.Healing * -1);
                comp.CheckTimer = Timing.CurTime + comp.CheckWait;
            }
        }
    }
    private void GiveInfluence(Entity<CosmicCultComponent> ent, InfluencePrototype proto)
    {
        if (proto.InfluenceType == "influence-type-active")
        {
            var actionEnt = Actions.AddAction(ent, proto.Action);
            ent.Comp.ActionEntities.Add(actionEnt);
        }
        else if (proto.InfluenceType == "influence-type-passive")
        {
            if (proto.Add != null)
            {
                foreach (var reg in proto.Add.Values)
                {
                    var compType = reg.Component.GetType();
                    if (HasComp(ent, compType))
                        continue;
                    AddComp(ent, _componentFactory.GetComponent(compType));
                }
            }

            if (proto.Remove != null)
            {
                foreach (var reg in proto.Remove.Values)
                {
                    RemComp(ent, reg.Component.GetType());
                }
            }
        }
        else if (proto.InfluenceType == "influence-type-aegis")
        {
            ent.Comp.AstralAegisStacks += 2;
        }
        _antag.SendBriefing(ent, Loc.GetString(proto.Name), Color.FromHex("#cae8e8"), null);
        _antag.SendBriefing(ent, Loc.GetString(proto.Description), Color.FromHex("#4cabb3"), null);
        Dirty(ent);
    }

    protected override void OnMonumentInteracted(Entity<CosmicMonumentComponent> ent, ref InteractHandEvent args)
    {
        var target = args.User;

        if (args.Handled || !TryComp<CosmicCultComponent>(target, out var cultComp) || _status.HasStatusEffect(target, StunId))
            return;

        if (cultComp.MonumentVisits <= 0 || cultComp.UnlockedInfluences.Count <= 0)
            return;

        var influenceToGain = Random.PickAndTake(cultComp.UnlockedInfluences);
        if (influenceToGain.Id == "InfluenceAstralAegis")
            cultComp.UnlockedInfluences.Add(influenceToGain, 10); // If we rolled Aegis, add it back to the pool.
        else
            cultComp.OwnedInfluences.Add(influenceToGain);

        if (!ProtoMan.TryIndex(influenceToGain, out var proto))
            return;

        cultComp.MonumentVisits--;
        if (TryComp<CosmicShiftedComponent>(target, out var shiftComp))
        {
            shiftComp.Occupied = true;
            _entityTimer.SpawnMethodTimer(TimeSpan.FromSeconds(2.5f), () => shiftComp.Occupied = false); // I hate doing this
        }

        var shakeData = new ESScreenshakeParameters() { Trauma = 0.3f, DecayRate = 0.075f, Frequency = 0.008f };
        RaiseNetworkEvent(new InfluenceVisualsEvent(GetNetEntity(target), GetNetEntity(ent.Owner), proto.Icon, cultComp.MonumentGachaSfx));
        _shake.Screenshake(target, shakeData, null);
        _status.TryAddStatusEffectDuration(target, StunId, TimeSpan.FromSeconds(2.5f));
        _entityTimer.SpawnMethodTimer(TimeSpan.FromSeconds(1.5f), () => GiveInfluence((target, cultComp), proto)); // I hate doing this
        args.Handled = true;
        Dirty(target, cultComp);
    }

    private void OnStartCultist(Entity<CosmicCultComponent> ent, ref ComponentInit args)
    {
        Actions.AddAction(ent, ref ent.Comp.CosmicShiftActionActionEntity, ent.Comp.CosmicShiftAction, ent);

        foreach (var actionId in ent.Comp.CosmicCultActions)
        {
            var actionEnt = Actions.AddAction(ent, actionId);
            ent.Comp.ActionEntities.Add(actionEnt);
        }

        foreach (var influence in ProtoMan.EnumeratePrototypes<InfluencePrototype>().Where(influence => influence.Tier == 1))
        {
            if (ent.Comp.UnlockedInfluences.ContainsKey(influence))
                continue;
            ent.Comp.UnlockedInfluences.Add(influence, influence.Weight);
        }
        ent.Comp.UnlockedInfluences.Add("InfluenceAstralAegis", 5);
        Dirty(ent);
    }

    #region Movespeed
    private void OnStartInfluenceStride(Entity<InfluenceStrideComponent> uid, ref ComponentInit args) // i wish movespeed was easier to work with
    {
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }
    private void OnEndInfluenceStride(Entity<InfluenceStrideComponent> uid, ref ComponentRemove args) // these functions just make sure
    {
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }
    private void OnStartImposition(Entity<CosmicImposingComponent> uid, ref ComponentInit args) // that movespeed applies more-or-less correctly
    {
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }
    private void OnEndImposition(Entity<CosmicImposingComponent> uid, ref ComponentRemove args) // as various cosmic cult effects get added and removed
    {
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefreshMoveSpeed(EntityUid uid, InfluenceStrideComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(1.1f, 1.1f);
    }
    private void OnImpositionMoveSpeed(EntityUid uid, CosmicImposingComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(0.65f, 0.65f);
    }
    #endregion

}
