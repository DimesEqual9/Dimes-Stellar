// SPDX-FileCopyrightText: 2026 TheShuEd <uhhadd@gmail.com>
//
// SPDX-License-Identifier: MIT

#nullable enable
using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST;

[TestFixture]
public sealed class StellarEntityFilteringTest
{
    public static readonly ProtoId<EntityCategoryPrototype> ForkFiltered = "ForkFiltered";

    [Test]
    public async Task CheckAllEntityHasForkFilteredCategory()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var protoManager = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                var indexedFilter = protoManager.Index(ForkFiltered);

                foreach (var proto in protoManager.EnumeratePrototypes<EntityPrototype>())
                {
                    if (!proto.ID.Contains("Stellar"))
                        continue;

                    if (proto.Abstract || proto.HideSpawnMenu)
                        continue;

                    if (!proto.Categories.Contains(indexedFilter))
                        Assert.Fail($"EntityPrototype: {proto} is not marked abstract, or does not have a HideSpawnMenu or ForkFiltered category");
                }
            });
        });
        await pair.CleanReturnAsync();
    }
}
