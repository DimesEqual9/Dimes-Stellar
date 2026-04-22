// SPDX-FileCopyrightText: 2026 AftrLite
//
// SPDX-License-Identifier: LicenseRef-Wallening

using System.Numerics;
using Content.Shared._ST.Shockwave;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST.Shockwave;

public sealed class StellarShockwaveOverlay : Overlay, IEntityEventSubscriber
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private static readonly ProtoId<ShaderPrototype> StellarShockwave = "StellarShockwave";

    private SharedTransformSystem? _xformSystem = null;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    public StellarShockwaveOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(StellarShockwave).Instance().Duplicate();
    }

    private Vector2 _position;
    private float _expiryTime;
    private float _lifeTime;
    private float _range;
    private float _force;
    private float _width;
    private float _abberation;

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null || _xformSystem is null && !_entMan.TrySystem(out _xformSystem))
            return false;

        var query = _entMan.EntityQueryEnumerator<StellarShockwaveComponent, TransformComponent>();

        if (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.MapID != args.MapId)
                return false;

            var mapPos = _xformSystem.GetWorldPosition(uid);
            var tempCoords = args.Viewport.WorldToLocal(mapPos);

            tempCoords.Y = 1 - (tempCoords.Y / args.Viewport.Size.Y);
            tempCoords.X /= args.Viewport.Size.X;

            _position = tempCoords;
            _expiryTime = (float)(comp.StartTime.TotalSeconds + comp.Duration) - (float) _timing.CurTime.TotalSeconds;
            _lifeTime = comp.Duration;
            _range = comp.VisualRange;
            _force = comp.VisualForce;
            _width = comp.VisualWidth;
            _abberation = comp.VisualAberration;

            return true;
        }

        return false;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || args.Viewport.Eye == null)
            return;

        _shader?.SetParameter("renderScale", args.Viewport.RenderScale * args.Viewport.Eye.Scale);
        _shader?.SetParameter("aberration", _abberation);
        _shader?.SetParameter("width", _width);
        _shader?.SetParameter("force", _force);
        _shader?.SetParameter("range", _range);
        _shader?.SetParameter("position", _position);
        _shader?.SetParameter("lifeTime", _lifeTime);
        _shader?.SetParameter("expiryTime", _expiryTime);
        _shader?.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_shader);
        worldHandle.DrawRect(args.WorldBounds, Color.White);
        worldHandle.UseShader(null);
    }
}
