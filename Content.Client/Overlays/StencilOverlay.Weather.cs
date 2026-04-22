using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Weather;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;

namespace Content.Client.Overlays;

public sealed partial class StencilOverlay
{
    private List<Entity<MapGridComponent>> _grids = new();
    private static readonly Vector2 TileOffset = new(0f, 0.5f); // Stellar - wallening
    private void DrawWeather(
        in OverlayDrawArgs args,
        CachedResources res,
        HashSet<Entity<WeatherStatusEffectComponent, StatusEffectComponent>> weathers,
        Matrix3x2 invMatrix)
    {
        var worldHandle = args.WorldHandle;
        var mapId = args.MapId;
        var worldAABB = args.WorldAABB;
        var worldBounds = args.WorldBounds;
        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;

        foreach (var (uid, weather, status) in weathers)
        {
            // Begin Stellar Relocation - the block above was moved here for per-weather stencils
            // Cut out the irrelevant bits via stencil
            // This is why we don't just use parallax; we might want specific tiles to get drawn over
            // particularly for planet maps or stations.
            worldHandle.RenderInRenderTarget(res.Blep!,
                () =>
                {
                    var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
                    _grids.Clear();

                    // idk if this is safe to cache in a field and clear sloth help
                    _mapManager.FindGridsIntersecting(mapId, worldAABB, ref _grids);

                    foreach (var grid in _grids)
                    {
                        var matrix = _transform.GetWorldMatrix(grid, xformQuery);
                        var matty = Matrix3x2.Multiply(matrix, invMatrix);
                        worldHandle.SetTransform(matty);
                        _entManager.TryGetComponent(grid.Owner, out RoofComponent? roofComp);

                        foreach (var tile in _map.GetTilesIntersecting(grid.Owner, grid, worldAABB))
                        {
                            // Ignored tiles for stencil
                            if (_weather.CanWeatherAffect((grid.Owner, grid, roofComp), tile) || weather.Override) // Stellar - weather that always shows
                                continue;

                            var gridTile = new Box2(tile.GridIndices * grid.Comp.TileSize,
                                (tile.GridIndices + Vector2i.One) * grid.Comp.TileSize);

                            worldHandle.DrawRect(gridTile, Color.White);
                        }
                    }
                },
                Color.Transparent);

            worldHandle.SetTransform(Matrix3x2.Identity);
            worldHandle.UseShader(_protoManager.Index(StencilMask).Instance());
            worldHandle.DrawTextureRect(res.Blep!.Texture, worldBounds);
            var curTime = _timing.RealTime;
            // End Stellar Relocation - the block above was moved here for per-weather stencils
            var alpha = _weather.GetWeatherPercent((uid, status));
            var sprite = _sprite.GetFrame(weather.Sprite, curTime);

            // Draw the rain
            worldHandle.UseShader(_protoManager.Index(StencilDraw).Instance());
            _parallax.DrawParallax(worldHandle,
                worldAABB,
                sprite,
                curTime,
                position + TileOffset, // Stellar - wallening tile offset
                weather.Scrolling ?? Vector2.Zero,
                modulate: (weather.Color ?? Color.White).WithAlpha(alpha * weather.Opacity)); // Stellar - weather opacity
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
