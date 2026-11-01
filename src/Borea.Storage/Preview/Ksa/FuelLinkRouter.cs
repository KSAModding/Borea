using System.Numerics;
using Borea.Core.Preview;

namespace Borea.Storage.Preview.Ksa;

/// <summary>The hose routes of a vehicle, and the connector poses they give.</summary>
/// <param name="Routes">One route for each fuel link that can be drawn, in the vehicle assembly frame.</param>
/// <param name="Swings">The pose of the connector subpart for each index in the part list whose connector turned.</param>
/// <param name="SkippedLinks">The links left out for a reason that the player has no other way to see.</param>
internal sealed record FuelLinkLayout(
    IReadOnlyList<IReadOnlyList<HoseWaypoint>> Routes,
    IReadOnlyDictionary<int, ConnectorSwing> Swings,
    int SkippedLinks);

/// <summary>
/// How a connector subpart turns about its pivot. A subpart matrix composes as
/// local * <paramref name="PartFrameMatrix"/> * part, so this matrix works in the frame of the full part.
/// </summary>
internal sealed record ConnectorSwing(SubPartInstanceData Connector, Matrix4x4 PartFrameMatrix);

/// <summary>
/// Finds the route of each fuel link, and the pose of each connector.
/// </summary>
/// <remarks>
/// <para>
/// An end of a link on a part without a fuel port is the origin of the part. A port without a connector gives its
/// anchor. A port with a connector gives the connector tip and, when the port has a lead-out, a point straight out of
/// the tip, and the hose direction at both is pinned to the connector axis. The nodes are in the frame of part A.
/// </para>
/// <para>
/// Each connector turns toward the first point of the hose after its lead-out, up to the largest angle of its port.
/// The two ends of a link without nodes aim at each other, so the pose settles over several passes, and
/// <see cref="AimPasses"/> passes stand in for them. A port with several links keeps the pose of its last link, and
/// every hose of the port starts at that pose.
/// </para>
/// <para>
/// A link is left out without a notice when its two ends are the same effective endpoint, where a port without a tank
/// counts as the part it is attached to, and when the same pair of endpoints is already linked. The preview also leaves
/// out a link with a part Id that the vehicle does not have, or a route that is not finite, and counts it. It leaves out
/// a link to a part without a template and does not count it, because the part has a placeholder and the preview already
/// reports a missing mod. The flow and the enabled state of a link do not change the route, and the preview reads
/// neither. A connector turns with the whole matrix of its part, which is exact for a part of uniform scale.
/// </para>
/// </remarks>
internal static class FuelLinkRouter
{
    /// <summary>The passes that the pose needs to settle. The two ends move less on each pass, by the ratio of connector length to hose length.</summary>
    private const int AimPasses = 16;

    public static FuelLinkLayout Route(IReadOnlyList<VehiclePartRef> parts, IReadOnlyList<VehicleFuelLink> links, PartCatalog catalog)
    {
        if (links.Count == 0)
            return new FuelLinkLayout([], new Dictionary<int, ConnectorSwing>(), 0);

        // When two parts have the same Id, the last one wins.
        var indexOfLocalId = new Dictionary<uint, int>();
        for (var i = 0; i < parts.Count; i++)
            indexOfLocalId[parts[i].LocalInstanceId] = i;

        var endpoints = new Dictionary<int, Endpoint?>();
        Endpoint? EndpointOf(uint localId)
        {
            var index = indexOfLocalId[localId];
            if (!endpoints.TryGetValue(index, out var endpoint))
            {
                endpoint = catalog.TryGetTemplate(parts[index].InstanceOf, out var template) ? new Endpoint(parts[index], template, catalog) : null;
                endpoints.Add(index, endpoint);
            }

            return endpoint;
        }

        var kept = new List<Link>();
        var linksByEffectiveEnd = new Dictionary<uint, List<Link>>();
        var skipped = 0;
        foreach (var link in links)
        {
            if (!indexOfLocalId.ContainsKey(link.PartA) || !indexOfLocalId.ContainsKey(link.PartB))
            {
                skipped++;
                continue;
            }

            if (EndpointOf(link.PartA) is not { } a || EndpointOf(link.PartB) is not { } b)
                continue;

            if (a.EffectiveEnd == b.EffectiveEnd
                || (linksByEffectiveEnd.TryGetValue(a.EffectiveEnd, out var known) && known.Exists(other => other.A == b || other.B == b)))
            {
                continue;
            }

            var added = new Link(a, b, [.. link.Nodes.Select(node => Vector3.Transform(node, a.PartMatrix))]);
            kept.Add(added);
            AddToLookup(linksByEffectiveEnd, a.EffectiveEnd, added);
            AddToLookup(linksByEffectiveEnd, b.EffectiveEnd, added);
        }

        for (var pass = 0; pass < AimPasses; pass++)
        {
            foreach (var link in kept)
            {
                link.A.AimAt(link.Nodes.Count > 0 ? link.Nodes[0] : link.B.OutermostPoint);
                link.B.AimAt(link.Nodes.Count > 0 ? link.Nodes[^1] : link.A.OutermostPoint);
            }
        }

        var routes = new List<IReadOnlyList<HoseWaypoint>>(kept.Count);
        foreach (var link in kept)
        {
            var route = new List<HoseWaypoint>(link.Nodes.Count + 4);
            link.A.AddWaypoints(route, leadOutFirst: false);
            route.AddRange(link.Nodes.Select(node => new HoseWaypoint(node, Vector3.Zero)));
            link.B.AddWaypoints(route, leadOutFirst: true);
            if (route.TrueForAll(IsFinite))
                routes.Add(route);
            else
                skipped++;
        }

        var swings = new Dictionary<int, ConnectorSwing>();
        foreach (var (index, endpoint) in endpoints)
        {
            if (endpoint?.Swing is { } swing)
                swings.Add(index, swing);
        }

        return new FuelLinkLayout(routes, swings, skipped);
    }

    private static void AddToLookup(Dictionary<uint, List<Link>> lookup, uint key, Link link)
    {
        if (!lookup.TryGetValue(key, out var list))
        {
            list = [];
            lookup.Add(key, list);
        }

        list.Add(link);
    }

    private static bool IsFinite(HoseWaypoint waypoint) =>
        float.IsFinite(waypoint.Position.X) && float.IsFinite(waypoint.Position.Y) && float.IsFinite(waypoint.Position.Z)
        && float.IsFinite(waypoint.PinnedDirection.X) && float.IsFinite(waypoint.PinnedDirection.Y) && float.IsFinite(waypoint.PinnedDirection.Z);

    private sealed record Link(Endpoint A, Endpoint B, IReadOnlyList<Vector3> Nodes);

    /// <summary>One full part at the end of at least one link, with the pose of its connector when it has one.</summary>
    private sealed class Endpoint
    {
        /// <summary>A direction whose squared length is below this has no use as an axis.</summary>
        private const float MinAxisLengthSquared = 0.5f;

        private readonly FuelPortData? _port;
        private readonly SubPartInstanceData? _connector;
        private readonly Matrix4x4 _connectorRest;
        private readonly Matrix4x4 _vehicleToPart;
        private readonly bool _canAim;
        private readonly Vector3 _pivotInPart;
        private readonly Vector3 _restAxisInPart;
        private readonly Vector3 _connectorAxis;
        private readonly Vector3 _fixedPoint;
        private Quaternion _swing = Quaternion.Identity;

        public Endpoint(VehiclePartRef part, PartTemplateData template, PartCatalog catalog)
        {
            PartMatrix = part.Transform;
            _port = FirstPort(template, catalog);
            EffectiveEnd = _port is not null && !HasTankInSubtree(template, catalog)
                ? part.ParentLocalId ?? part.LocalInstanceId
                : part.LocalInstanceId;

            _fixedPoint = _port is null
                ? PartMatrix.Translation
                : PartMatrix.Translation + Vector3.TransformNormal(_port.Anchor, PartMatrix);
            if (_port is null || FindConnector(_port, template, catalog) is not { } connector)
                return;

            var rest = (connector.Transform ?? KsaTransform.Identity).ToMatrix();
            if (!KsaTransform.IsFinite(rest))
                return;

            _connector = connector;
            _connectorRest = rest;
            _pivotInPart = Vector3.Transform(_port.Pivot, rest);
            _connectorAxis = NormalizeOrZero(_port.Tip - _port.Pivot);
            _restAxisInPart = NormalizeOrZero(Vector3.TransformNormal(_port.Tip - _port.Pivot, rest));
            _canAim = float.IsFinite(_port.MaxAngleRadians) && Matrix4x4.Invert(PartMatrix, out _vehicleToPart);
        }

        public Matrix4x4 PartMatrix { get; }

        /// <summary>The LocalInstanceId of the part that this end counts as when two links are compared.</summary>
        public uint EffectiveEnd { get; }

        /// <summary>The pose to draw the connector with, or null when it did not leave its rest pose.</summary>
        public ConnectorSwing? Swing =>
            _connector is not null && _swing != Quaternion.Identity ? new ConnectorSwing(_connector, SwingMatrix) : null;

        /// <summary>The point of this end that the other end of a link without nodes aims at.</summary>
        public Vector3 OutermostPoint
        {
            get
            {
                if (_port is null || _connector is null)
                    return _fixedPoint;

                return Vector3.Transform(HasLeadOut ? LeadOutPoint : _port.Tip, ConnectorMatrix);
            }
        }

        private bool HasLeadOut => _port is { LeadOut: > 0f } && _connectorAxis.LengthSquared() > MinAxisLengthSquared;

        private Vector3 LeadOutPoint => _port!.Tip + (_connectorAxis * _port.LeadOut);

        private Matrix4x4 SwingMatrix =>
            Matrix4x4.CreateTranslation(-_pivotInPart) * Matrix4x4.CreateFromQuaternion(_swing) * Matrix4x4.CreateTranslation(_pivotInPart);

        private Matrix4x4 ConnectorMatrix => _connectorRest * SwingMatrix * PartMatrix;

        /// <summary>Turns the connector from its rest pose toward the target, up to its largest angle.</summary>
        public void AimAt(Vector3 targetInVehicle)
        {
            if (!_canAim)
                return;

            var direction = NormalizeOrZero(Vector3.Transform(targetInVehicle, _vehicleToPart) - _pivotInPart);
            if (direction.LengthSquared() < MinAxisLengthSquared)
                return;

            var turnAxis = Vector3.Cross(_restAxisInPart, direction);
            if (turnAxis.LengthSquared() <= 1e-12f)
            {
                _swing = Quaternion.Identity;
                return;
            }

            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(_restAxisInPart, direction), -1f, 1f));
            _swing = Quaternion.CreateFromAxisAngle(Vector3.Normalize(turnAxis), MathF.Min(angle, _port!.MaxAngleRadians));
        }

        /// <param name="route">The route that the waypoints of this end go on.</param>
        /// <param name="leadOutFirst">True for part B, whose hose arrives along the lead-out and against the axis of the connector.</param>
        public void AddWaypoints(List<HoseWaypoint> route, bool leadOutFirst)
        {
            if (_port is null || _connector is null)
            {
                route.Add(new HoseWaypoint(_fixedPoint, Vector3.Zero));
                return;
            }

            var matrix = ConnectorMatrix;
            var pinned = NormalizeOrZero(Vector3.TransformNormal(leadOutFirst ? -_connectorAxis : _connectorAxis, matrix));
            var tip = new HoseWaypoint(Vector3.Transform(_port.Tip, matrix), pinned);
            if (!HasLeadOut)
            {
                route.Add(tip);
                return;
            }

            var leadOut = new HoseWaypoint(Vector3.Transform(LeadOutPoint, matrix), pinned);
            route.Add(leadOutFirst ? leadOut : tip);
            route.Add(leadOutFirst ? tip : leadOut);
        }

        /// <summary>
        /// The port of a part. A port in a subpart comes before one in the part itself.
        /// </summary>
        private static FuelPortData? FirstPort(PartTemplateData template, PartCatalog catalog)
        {
            foreach (var instance in template.SubParts)
            {
                if (catalog.TryGetTemplate(instance.InstanceOf, out var subTemplate) && subTemplate.FuelPorts.Count > 0)
                    return subTemplate.FuelPorts[0];
            }

            return template.FuelPorts.Count > 0 ? template.FuelPorts[0] : null;
        }

        private static bool HasTankInSubtree(PartTemplateData template, PartCatalog catalog) =>
            template.HasTank || template.SubParts.Any(instance => catalog.TryGetTemplate(instance.InstanceOf, out var subTemplate) && subTemplate.HasTank);

        /// <summary>
        /// The connector subpart of a port. It compares the Id of the subpart, which is its instance Id or else the Id of its
        /// template, and the compare is ordinal.
        /// </summary>
        private static SubPartInstanceData? FindConnector(FuelPortData port, PartTemplateData template, PartCatalog catalog)
        {
            if (port.ConnectorSubPartId is null)
                return null;

            foreach (var instance in template.SubParts)
            {
                var runtimeId = instance.Id.Length > 0 || !catalog.TryGetTemplate(instance.InstanceOf, out var subTemplate) ? instance.Id : subTemplate.Id;
                if (string.Equals(runtimeId, port.ConnectorSubPartId, StringComparison.Ordinal))
                    return instance;
            }

            return null;
        }

        private static Vector3 NormalizeOrZero(Vector3 vector)
        {
            var length = vector.Length();
            return length > 1e-6f && float.IsFinite(length) ? vector / length : Vector3.Zero;
        }
    }
}
