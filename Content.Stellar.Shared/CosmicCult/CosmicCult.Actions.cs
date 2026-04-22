// SPDX-FileCopyrightText: 2025 AftrLite
// SPDX-FileCopyrightText: 2025 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: LicenseRef-CosmicCult

using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Stellar.Shared.CosmicCult;

[RegisterComponent, NetworkedComponent]
public sealed partial class CosmicCultActionComponent : Component;
public sealed partial class EventCosmicSiphon : EntityTargetActionEvent;
public sealed partial class EventCosmicShunt : EntityTargetActionEvent;
public sealed partial class EventCosmicReturn : InstantActionEvent;
public sealed partial class EventCosmicLapse : EntityTargetActionEvent;
public sealed partial class EventCosmicGlare : InstantActionEvent;
public sealed partial class EventCosmicIngress : EntityTargetActionEvent;
public sealed partial class EventCosmicImposition : InstantActionEvent;
public sealed partial class EventCosmicNova : WorldTargetActionEvent;
public sealed partial class EventCosmicFragmentation : EntityTargetActionEvent;

// WIP
public sealed partial class EventCosmicShift : InstantActionEvent;


// COLOSSUS ACTIONS
public sealed partial class EventCosmicColossusSunder : WorldTargetActionEvent;
public sealed partial class EventCosmicColossusIngress : EntityTargetActionEvent;
public sealed partial class EventCosmicColossusHibernate : InstantActionEvent;
