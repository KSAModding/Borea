using System.Numerics;
using Borea.Core.Preview;

namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// Places the render models of the full parts of a vehicle and of their subparts in the vehicle assembly frame, and
/// puts a placeholder box where a template, mesh or material does not resolve.
/// </summary>
/// <remarks>
/// <para>
/// The walk follows the Part constructor of the game. A full part takes its matrix from vehicle.xml. For each
/// subpart instance of its template, in template order and then the instances that GameData added, it makes a Part
/// whose matrix is MatrixAsmb2ParentAsmb * PartParent.MatrixAsmb2VehicleAsmb (Part.MatrixAsmb2VehicleAsmb), and that
/// subpart does the same with the subpart instances of its own template. A subpart instance without a Transform has
/// the identity matrix. Each PartModel, PartModelDynamic and PartModelGlass of a template gives one draw with the
/// matrix of its part. The game does not draw an Internal model outside the IVA view or a ShadowProxy model at all
/// (PartModel.AddInstance), so they are only counted.
/// </para>
/// <para>
/// The game has no guard against a template that contains itself, so the preview stops a branch that repeats an Id
/// of its own path or nests deeper than <see cref="MaxSubPartDepth"/>, with a Cycle notice. The notices here are only
/// the ones of this vehicle. The caller adds the notices of the catalog, the mesh index and the vehicle file.
/// </para>
/// </remarks>
internal static class VehicleSceneResolver
{
    public const int MaxSubPartDepth = 16;

    /// <summary>The most draws and placeholders together that one vehicle gives.</summary>
    public const int MaxItems = 50_000;

    /// <summary>
    /// The most parts and subparts the walk visits. It also stops a tree of templates without draws that grows with
    /// each level, which the depth cap alone does not stop.
    /// </summary>
    public const int MaxNodes = 200_000;

    /// <summary>The half size of a placeholder for a template without connectors, which gives a 1 m box.</summary>
    public const float DefaultHalfSize = 0.5f;

    /// <param name="parts">The full parts from <see cref="VehicleXmlReader"/>.</param>
    /// <param name="modByMissingId">
    /// For an Id that a needed mod would give and that is not on disk, the Id of that mod. A placeholder for such an
    /// Id gets <see cref="PlaceholderReason.ModNotLoaded"/>. The keys should use <see cref="KsaIdComparer.Registry"/>.
    /// </param>
    public static ResolvedVehicle Resolve(
        IReadOnlyList<VehiclePartRef> parts,
        PartCatalog catalog,
        MeshIndex meshes,
        IReadOnlyDictionary<string, string>? modByMissingId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(meshes);

        var walk = new Walk(catalog, meshes, modByMissingId);
        foreach (var part in parts)
        {
            if (walk.OverBudget)
                break;
            walk.AddPart(part);
        }

        return walk.Finish(parts.Count);
    }

    private sealed class Walk(PartCatalog catalog, MeshIndex meshIndex, IReadOnlyDictionary<string, string>? modByMissingId)
    {
        private readonly List<PreviewMeshRef> _meshes = [];
        private readonly Dictionary<PreviewMeshRef, int> _meshIndices = [];
        private readonly List<PreviewMaterialRef> _materials = [];
        private readonly Dictionary<(MaterialData Material, bool Glass), int> _materialIndices = [];
        private readonly List<ResolvedDraw> _draws = [];
        private readonly List<ResolvedPlaceholder> _placeholders = [];
        private readonly List<PreviewNotice> _notices = [];
        private readonly HashSet<(PreviewNoticeKind, string)> _noticed = new(new NoticeKeyComparer());
        private readonly List<string> _path = [];
        private int _nodes;
        private int _skipped;

        public bool OverBudget { get; private set; }

        public void AddPart(VehiclePartRef part)
        {
            if (!catalog.TryGetTemplate(part.InstanceOf, out var template))
            {
                AddPlaceholder(part.Transform, null, part.InstanceOf, PlaceholderReason.UnknownPart);
                return;
            }

            _path.Add(template.Id);
            AddTemplate(template, part.Transform, part.InstanceOf);
            _path.Clear();
        }

        public ResolvedVehicle Finish(int partCount) =>
            new(_meshes, _materials, _draws, _placeholders, _notices, partCount, _skipped);

        /// <param name="partId">The InstanceOf of the full part that the template belongs to.</param>
        private void AddTemplate(PartTemplateData template, Matrix4x4 matrix, string partId)
        {
            if (++_nodes > MaxNodes)
            {
                StopForBudget($"The vehicle has more than {MaxNodes} parts and subparts.");
                return;
            }

            // One box is enough for each Id that fails on this part, also when several models name it.
            var failed = new HashSet<string>(KsaIdComparer.Registry);
            foreach (var model in template.RenderModels)
            {
                if (OverBudget)
                    return;
                AddModel(model, template, matrix, partId, failed);
            }

            foreach (var instance in template.SubParts)
            {
                if (OverBudget)
                    return;

                var subMatrix = (instance.Transform ?? KsaTransform.Identity).ToMatrix() * matrix;
                if (!catalog.TryGetTemplate(instance.InstanceOf, out var subTemplate))
                {
                    AddPlaceholder(subMatrix, null, instance.InstanceOf, PlaceholderReason.UnknownSubPart);
                    continue;
                }

                if (_path.Contains(subTemplate.Id, KsaIdComparer.Registry))
                {
                    AddNotice(PreviewNoticeKind.Cycle, subTemplate.Id, "The template contains itself, so the preview stops there.");
                    continue;
                }

                if (_path.Count > MaxSubPartDepth)
                {
                    AddNotice(PreviewNoticeKind.Cycle, subTemplate.Id, $"The subparts nest deeper than {MaxSubPartDepth} levels, so the preview stops there.");
                    continue;
                }

                _path.Add(subTemplate.Id);
                AddTemplate(subTemplate, subMatrix, partId);
                _path.RemoveAt(_path.Count - 1);
            }
        }

        private void AddModel(RenderModelData model, PartTemplateData template, Matrix4x4 matrix, string partId, HashSet<string> failed)
        {
            if (model.Internal || model.ShadowProxy)
            {
                _skipped++;
                return;
            }

            if (model.MeshId is not { } meshId || !meshIndex.TryFind(meshId, out var mesh))
            {
                var missing = model.MeshId ?? string.Empty;
                if (failed.Add(missing))
                    AddPlaceholder(matrix, template, missing, PlaceholderReason.UnknownMesh);
                return;
            }

            var material = -1;
            if (model.MaterialKey is { } key)
            {
                if (!catalog.TryGetMaterial(key, out var data))
                {
                    if (failed.Add(key))
                        AddPlaceholder(matrix, template, key, PlaceholderReason.UnknownMaterial);
                    return;
                }

                material = IndexOfMaterial(data, model.Kind == PreviewModelKind.Glass);
            }

            if (meshIndex.DuplicateNames.Contains(mesh.Name))
                AddNotice(PreviewNoticeKind.DuplicateId, mesh.Name, $"More than one mesh file has a mesh with this name, and the preview uses the one in {mesh.File.Source}/{mesh.File.Path}.");

            if (TakeItem())
                _draws.Add(new ResolvedDraw(IndexOfMesh(mesh), material, matrix, partId, model.Kind));
        }

        private int IndexOfMesh(PreviewMeshRef mesh)
        {
            if (!_meshIndices.TryGetValue(mesh, out var index))
            {
                index = _meshes.Count;
                _meshes.Add(mesh);
                _meshIndices.Add(mesh, index);
            }

            return index;
        }

        private int IndexOfMaterial(MaterialData material, bool glass)
        {
            if (!_materialIndices.TryGetValue((material, glass), out var index))
            {
                index = _materials.Count;
                _materials.Add(new PreviewMaterialRef(material.Key, material.Diffuse, material.AoRoughMetal, glass));
                _materialIndices.Add((material, glass), index);
            }

            return index;
        }

        /// <param name="template">The template whose connectors size the box, or null when the template is the missing item.</param>
        private void AddPlaceholder(Matrix4x4 matrix, PartTemplateData? template, string missingId, PlaceholderReason reason)
        {
            if (!TakeItem())
                return;

            string? modId = null;
            if (modByMissingId is not null && modByMissingId.TryGetValue(missingId, out var mod))
                (reason, modId) = (PlaceholderReason.ModNotLoaded, mod);

            var (center, halfSize) = Size(template?.ConnectorExtents);
            _placeholders.Add(new ResolvedPlaceholder(Matrix4x4.CreateTranslation(center) * matrix, halfSize, missingId, reason, modId));
        }

        /// <summary>
        /// The box around the connectors in the frame of the part. Connectors often lie on one line, for example the
        /// top and bottom node of a tank, so an axis that is shorter than half the longest one is widened to that,
        /// which gives a box in proportion to the part. Without at least two connectors apart, the box is 1 m.
        /// </summary>
        private static (Vector3 Center, Vector3 HalfSize) Size(PartExtents? extents)
        {
            if (extents is not { } box)
                return (Vector3.Zero, new Vector3(DefaultHalfSize));

            var half = (box.Max - box.Min) / 2f;
            var longest = MathF.Max(half.X, MathF.Max(half.Y, half.Z));
            if (!float.IsFinite(longest) || longest < 0.01f)
                return (Vector3.Zero, new Vector3(DefaultHalfSize));

            return ((box.Min + box.Max) / 2f, Vector3.Max(half, new Vector3(longest / 2f)));
        }

        private bool TakeItem()
        {
            if (OverBudget)
                return false;

            if (_draws.Count + _placeholders.Count < MaxItems)
                return true;

            StopForBudget($"The vehicle has more than {MaxItems} models, so the rest is left out.");
            return false;
        }

        private void StopForBudget(string detail)
        {
            OverBudget = true;
            AddNotice(PreviewNoticeKind.Budget, VehicleXmlReader.FileName, detail);
        }

        private void AddNotice(PreviewNoticeKind kind, string subject, string detail)
        {
            if (_noticed.Add((kind, subject)))
                _notices.Add(new PreviewNotice(kind, subject, detail));
        }
    }

    private sealed class NoticeKeyComparer : IEqualityComparer<(PreviewNoticeKind Kind, string Subject)>
    {
        public bool Equals((PreviewNoticeKind Kind, string Subject) x, (PreviewNoticeKind Kind, string Subject) y) =>
            x.Kind == y.Kind && KsaIdComparer.Registry.Equals(x.Subject, y.Subject);

        public int GetHashCode((PreviewNoticeKind Kind, string Subject) obj) =>
            HashCode.Combine(obj.Kind, KsaIdComparer.Registry.GetHashCode(obj.Subject));
    }
}
