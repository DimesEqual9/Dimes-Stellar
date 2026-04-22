// SPDX-FileCopyrightText: 2025 AftrLite
// SPDX-FileCopyrightText: 2025 Janet Blackquill <uhhadd@gmail.com>
//
// SPDX-License-Identifier: LicenseRef-CosmicCult

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Stellar.Shared.CosmicCult;

[Serializable, NetSerializable]
public sealed partial class EventCosmicSiphonDoAfter : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class EventAbsorbRiftDoAfter : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class EventCosmicColossusIngressDoAfter : SimpleDoAfterEvent;

//

[Serializable, NetSerializable]
public sealed partial class CosmicStigmaDoAfter : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CosmicShuntDoAfter : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CosmicChantryDoAfter : SimpleDoAfterEvent;

