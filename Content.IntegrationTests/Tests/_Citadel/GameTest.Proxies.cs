#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Content.Server.Mind;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Fixtures;

public abstract partial class GameTest
{
    /// <summary>
    ///     Fully loads a given map on the server, optionally initializing it, and runs the pair in sync for a few ticks
    ///     to ensure both sides have fully loaded the map.
    /// </summary>
    /// <remarks>
    ///     The test map is global to the game test and is exposed through the TestMap property when ready. Cleanup is
    ///     handled automatically as well.
    /// </remarks>
    [MemberNotNull(nameof(TestMap))]
    public Task<TestMapData> LoadTestMap(ResPath mapPath, bool initialized = true)
    {
        // C# is smart, but not that smart, we need to make a promise here.
#pragma warning disable CS8774
        return Pair.LoadTestMap(mapPath, initialized);
#pragma warning restore
    }

    /// <inheritdoc cref="M:Robust.UnitTesting.Pool.TestPair`2.SyncTicks(System.Int32)"/>
    public Task SyncTicks(int targetDelta = 1)
    {
        return Pair.SyncTicks(targetDelta);
    }

    /// <inheritdoc cref="M:Robust.Shared.GameObjects.EntityManager.EntityQueryEnumerator``1"/>
    public EntityQueryEnumerator<T> SQuery<T>()
        where T : IComponent
    {
        return Server.EntMan.EntityQueryEnumerator<T>();
    }

    /// <inheritdoc cref="M:Robust.Shared.GameObjects.EntityManager.EntityQueryEnumerator``1"/>
    public EntityQueryEnumerator<T> CQuery<T>()
        where T : IComponent
    {
        return Client.EntMan.EntityQueryEnumerator<T>();
    }

    /// <summary>
    ///     Tests whether any entity exists with the given component on the server.
    /// </summary>
    public bool SAnyExists<T>()
        where T : IComponent
    {
        var query = SQuery<T>();

        return query.MoveNext(out _);
    }

    /// <summary>
    ///     Tests whether any entity exists with the given component on the client.
    /// </summary>
    public bool CAnyExists<T>()
        where T : IComponent
    {
        var query = CQuery<T>();

        return query.MoveNext(out _);
    }

    /// <summary>
    ///     Queries the number of entities with a given component on the server.
    /// </summary>
    public int SQueryCount<T>()
        where T : IComponent
    {
        return Server.EntMan.Count<T>();
    }

    /// <summary>
    ///     Queries every entity with the given component on the server and returns a list of them.
    /// </summary>
    public List<Entity<T>> SQueryList<T>()
        where T : IComponent
    {
        var list = new List<Entity<T>>(SQueryCount<T>());

        var q = SQuery<T>();

        while (q.MoveNext(out var ent, out var comp1))
        {
            list.Add((ent, comp1));
        }

        return list;
    }

    /// <summary>
    ///     Queries every entity with the given component on the server and returns a list of them.
    /// </summary>
    public List<Entity<T>> CQueryList<T>()
        where T : IComponent
    {
        var list = new List<Entity<T>>(CQueryCount<T>());

        var q = CQuery<T>();

        while (q.MoveNext(out var ent, out var comp1))
        {
            list.Add((ent, comp1));
        }

        return list;
    }

    /// <summary>
    ///     Queries the number of entities with a given component on the client.
    /// </summary>
    public int CQueryCount<T>()
        where T : IComponent
    {
        return Client.EntMan.Count<T>();
    }

    /// <summary>
    ///     Gets a single instance of an entity with the given component on the server, asserting it is the only one.
    /// </summary>
    public bool SQuerySingle<T>([NotNullWhen(true)] out Entity<T>? ent)
        where T : IComponent
    {
        var query = SQuery<T>();

        if (query.MoveNext(out var eid, out var comp))
        {
            Assert.That(query.MoveNext(out var extra, out _),
                Is.False,
                $"Expected only one entity with {typeof(T)}, found {SToPrettyString(eid)} and then {SToPrettyString(extra)}");
            ent = (eid, comp);
            return true;
        }

        ent = null;
        return false;
    }

    /// <summary>
    ///     Gets a single instance of an entity with the given component on the client, asserting it is the only one.
    /// </summary>
    public bool CQuerySingle<T>([NotNullWhen(true)] out Entity<T>? ent)
        where T : IComponent
    {
        var query = CQuery<T>();

        if (query.MoveNext(out var eid, out var comp))
        {
            Assert.That(query.MoveNext(out var extra, out _),
                Is.False,
                $"Expected only one entity with {typeof(T)}, found {CToPrettyString(eid)} and then {CToPrettyString(extra)}");
            ent = (eid, comp);
            return true;
        }

        ent = null;
        return false;
    }

    public async Task<ICommonSession[]> AddDummySessionsSync(int count = 1)
    {
        var res = await Server.AddDummySessions(count);

        await Pair.ReallyBeIdle(); // That takes a while.

        return res;
    }

    /// <summary>
    ///     Checks whether the given entity has been deleted on the server.
    /// </summary>
    public bool SDeleted(EntityUid? ent)
    {
        return Server.EntMan.Deleted(ent);
    }

    /// <summary>
    ///     Checks whether the given entity has been deleted on the client.
    /// </summary>
    public bool CDeleted(EntityUid? ent)
    {
        return Client.EntMan.Deleted(ent);
    }

    /// <summary>
    ///     Checks whether the given entity has the given component.
    /// </summary>
    public bool SHasComp<T>(EntityUid? ent)
        where T : IComponent
    {
        return Server.EntMan.HasComponent<T>(ent);
    }

    /// <summary>
    ///     Checks whether the given entity has the given component.
    /// </summary>
    public bool CHasComp<T>(EntityUid? ent)
        where T : IComponent
    {
        return Client.EntMan.HasComponent<T>(ent);
    }

    /// <summary>
    ///     Rolls <see cref="IGameTiming.CurTime"/> forward on the server by the given amount, rounding up.
    ///     Does not run any of the intermediate ticks, this teleports the server forward in time.
    /// </summary>
    /// <param name="amount">The amount of time to go forward.</param>
    public void SRollForwardCurTime(TimeSpan amount)
    {
        var ticks = checked((uint)Math.Ceiling(amount / Server.Timing.TickPeriod));

        Server.Timing.CurTick += ticks;
    }


    /// <summary>
    ///     Performs <see cref="SRollForwardCurTime"/>, then synchronizes the client.
    /// </summary>
    /// <param name="amount">The amount of time to go forward.</param>
    public async Task RollForwardCurTime(TimeSpan amount)
    {
        SRollForwardCurTime(amount);

        await Pair.SyncTicks();
    }

    /// <summary>
    ///     Assigns the player a body in the test map, ensuring they have a mind as well.
    /// </summary>
    public async Task<EntityUid> AssignPlayerBody(ICommonSession session,
        string playerPrototype = "InteractionTestMob",
        bool godMode = true)
    {
        EntityUid res = default;

        var mindSys = SEntMan.System<MindSystem>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(TestMap,
                Is.Not.Null,
                $"{nameof(AssignPlayerBody)} doesn't work without a {nameof(TestMap)}.");

            // InteractionTest cargo cult.
            mindSys.WipeMind(session.ContentData()?.Mind);

            res = SEntMan.SpawnAtPosition(playerPrototype, TestMap.GridCoords);

            mindSys.ControlMob(session.UserId, res);
        });

        // Sync them back up.
        await Pair.RunTicksSync(5);

        return res;
    }
}
