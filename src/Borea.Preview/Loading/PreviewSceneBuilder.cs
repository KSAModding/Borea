using System.Numerics;
using Borea.Core.Preview;
using Borea.Preview.Gltf;
using Borea.Preview.Scene;
using Borea.Preview.Textures;

namespace Borea.Preview.Loading;

/// <summary>
/// Turns a <see cref="ResolvedVehicle"/> into a <see cref="PreviewScene"/> in two stages. The geometry stage decodes
/// the meshes and gives each material a flat color, so the player can turn the vehicle early, and the texture stage
/// then adds the Diffuse and AoRoughMetal levels. Each mesh and each texture fails by itself: a mesh that cannot be
/// read becomes a box, and a texture that cannot be read keeps the flat color. Each failure gives a notice. An
/// optional third stage, <see cref="AddNormalsAsync"/>, adds the Normal levels and gives no notice.
/// </summary>
/// <remarks>
/// The notices of each stage are only those of the stage, so a caller shows them together with
/// <see cref="ResolvedVehicle.Notices"/>. A cancellation, or an <see cref="ObjectDisposedException"/> from a disposed
/// source, stops the stage and reaches the caller, as the contract of <see cref="IPreviewAssetSource"/> asks.
/// </remarks>
public sealed class PreviewSceneBuilder
{
    private static readonly Vector3 DefaultBaseColor = new(0.45f);
    private static readonly Vector3 UnknownHalfSize = new(0.5f);

    /// <summary>The largest hose mesh that is decoded only to find its part of the atlas. The hose needs few vertices.</summary>
    private const long MaxHoseMeshWeight = 65_536;

    private readonly PreviewTextureLoader _textures;
    private readonly PreviewMeshCache _meshes;

    /// <param name="textures">The texture loader, whose cache the builds share.</param>
    public PreviewSceneBuilder(PreviewTextureLoader textures)
    {
        ArgumentNullException.ThrowIfNull(textures);
        _textures = textures;
        _meshes = new PreviewMeshCache();
    }

    public PreviewMeshCache MeshCache => _meshes;

    /// <summary>
    /// Decodes the meshes that the draws use and gives a scene with flat colors. The mesh decodes are grouped by GLB
    /// file, and each file is opened once and its container read once. When the draws have more triangles together
    /// than <see cref="PreviewLoadOptions.MaxTriangles"/>, the largest draws become <see cref="PreviewPlaceholderReason.Budget"/>
    /// boxes until the rest fit, and these meshes are not decoded. The distinct meshes that are left must also fit
    /// <see cref="PreviewLoadOptions.MaxMeshBytes"/>, and the largest of them become boxes until they do. Each fuel line
    /// becomes a draw of its own, with a mesh that <see cref="HoseMeshBuilder"/> makes in the vehicle frame.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The source was disposed, which the caller treats as a cancellation.</exception>
    public Task<(PreviewScene Scene, IReadOnlyList<PreviewNotice> Notices)> BuildGeometryAsync(
        ResolvedVehicle vehicle,
        IPreviewAssetSource source,
        PreviewLoadOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => BuildGeometry(vehicle, source, options, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Gives a copy of <paramref name="scene"/> whose materials have their Diffuse and AoRoughMetal levels. The scene
    /// must come from <see cref="BuildGeometryAsync"/> for the same <paramref name="vehicle"/>. When the textures at
    /// <see cref="PreviewLoadOptions.TextureTarget"/> could need more than <see cref="PreviewLoadOptions.MaxRgbaBytes"/>,
    /// the target drops to <see cref="PreviewLoadOptions.ReducedTextureTarget"/>, and the textures that still do not
    /// fit keep the flat color. Diffuse textures come first, because they change the look most. The Normal textures
    /// do not load here, see <see cref="AddNormalsAsync"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The source was disposed, which the caller treats as a cancellation.</exception>
    public async Task<(PreviewScene Scene, IReadOnlyList<PreviewNotice> Notices)> AddTexturesAsync(
        PreviewScene scene,
        ResolvedVehicle vehicle,
        IPreviewAssetSource source,
        PreviewLoadOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var (used, textures) = ColorTextures(scene, vehicle);
        var notices = new List<PreviewNotice>();
        var (target, allowed) = FitTextureBudget(textures.Count, options, notices);

        var loads = new Task<(PreviewImage? Image, PreviewNotice? Notice)>[allowed];
        for (var i = 0; i < allowed; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            loads[i] = _textures.LoadAsync(source, textures[i], target, cancellationToken);
        }

        var results = await Task.WhenAll(loads).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var images = new Dictionary<PreviewAssetRef, PreviewImage>();
        for (var i = 0; i < results.Length; i++)
        {
            if (results[i].Image is { } image)
            {
                images[textures[i]] = image;
            }

            if (results[i].Notice is { } notice)
            {
                notices.Add(notice);
            }
        }

        var textured = new Dictionary<PreviewMaterial, PreviewMaterial>(ReferenceEqualityComparer.Instance);
        foreach (var material in used)
        {
            var reference = vehicle.Materials[material.SourceMaterial];
            textured[material] = material with
            {
                Diffuse = reference.Diffuse is { } diffuse ? images.GetValueOrDefault(diffuse) : null,
                AoRoughMetal = reference.AoRoughMetal is { } aoRoughMetal ? images.GetValueOrDefault(aoRoughMetal) : null,
            };
        }

        var draws = scene.Draws
            .Select(draw => textured.TryGetValue(draw.Material, out var material) ? draw with { Material = material } : draw)
            .ToArray();
        return (new PreviewScene(draws, scene.Placeholders), notices);
    }

    /// <summary>
    /// Gives a copy of <paramref name="scene"/> whose materials also have their Normal levels, or the same scene when
    /// no Normal texture loads. The scene must come from <see cref="AddTexturesAsync"/> with the same
    /// <paramref name="vehicle"/> and <paramref name="options"/>, because the normals load at the target of that stage
    /// and only into the room that its Diffuse and AoRoughMetal textures left in <see cref="PreviewLoadOptions.MaxRgbaBytes"/>.
    /// A Normal path that is also a Diffuse or AoRoughMetal path is not loaded a second time.
    /// </summary>
    /// <remarks>
    /// This is a stage of its own, because the Standard quality does not read a normal map. A caller runs it only for
    /// a quality that reads one, after the textured scene shows and its still is stored, so the Standard quality keeps
    /// the timing and the cancel window that it has without normal maps. A normal map only adds detail, so it never takes room from a Diffuse or
    /// AoRoughMetal texture and never lowers the target. A Normal texture that fails keeps no image and gives no
    /// notice, so the notices and the partial state that the caller derives from the texture stage stay those of the
    /// Diffuse and AoRoughMetal textures. The texture loads as data, because the gamma curve belongs to the sampler of
    /// the Diffuse texture and not to the loader. Most Normal textures of the game are BC5, but T_LightPack_2_Normal is
    /// BC1 with an sRGB format. The preview decodes its bytes as they are stored, and how the game reads it through an
    /// sRGB view is not verified in the decompiled source.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The source was disposed, which the caller treats as a cancellation.</exception>
    public async Task<PreviewScene> AddNormalsAsync(
        PreviewScene scene,
        ResolvedVehicle vehicle,
        IPreviewAssetSource source,
        PreviewLoadOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        // The target and the color textures are computed again as the texture stage computed them. That stage already
        // gave the budget notice, so the copy here is dropped.
        var (used, colorTextures) = ColorTextures(scene, vehicle);
        var (target, _) = FitTextureBudget(colorTextures.Count, options, []);
        var color = new HashSet<PreviewAssetRef>(colorTextures);
        var room = Math.Max(0L, (options.MaxRgbaBytes / LevelBytes(target)) - colorTextures.Count);
        var normals = new List<PreviewAssetRef>();
        foreach (var material in used)
        {
            if (normals.Count >= room)
            {
                break;
            }

            if (vehicle.Materials[material.SourceMaterial].Normal is { } normal && !color.Contains(normal) && !normals.Contains(normal))
            {
                normals.Add(normal);
            }
        }

        var loads = new Task<(PreviewImage? Image, PreviewNotice? Notice)>[normals.Count];
        for (var i = 0; i < loads.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            loads[i] = _textures.LoadAsync(source, normals[i], target, cancellationToken);
        }

        var results = await Task.WhenAll(loads).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var images = new Dictionary<PreviewAssetRef, PreviewImage>();
        for (var i = 0; i < results.Length; i++)
        {
            if (results[i].Image is { } image)
            {
                images[normals[i]] = image;
            }
        }

        if (images.Count == 0)
        {
            return scene;
        }

        var mapped = new Dictionary<PreviewMaterial, PreviewMaterial>(ReferenceEqualityComparer.Instance);
        foreach (var material in used)
        {
            if (vehicle.Materials[material.SourceMaterial].Normal is { } normal && images.TryGetValue(normal, out var image))
            {
                mapped[material] = material with { Normal = image };
            }
        }

        var draws = scene.Draws
            .Select(draw => mapped.TryGetValue(draw.Material, out var material) ? draw with { Material = material } : draw)
            .ToArray();
        return new PreviewScene(draws, scene.Placeholders);
    }

    /// <summary>
    /// The materials of <paramref name="scene"/> that can show a texture, in draw order, and their distinct Diffuse
    /// paths followed by their distinct AoRoughMetal paths.
    /// </summary>
    private static (List<PreviewMaterial> Used, List<PreviewAssetRef> Textures) ColorTextures(PreviewScene scene, ResolvedVehicle vehicle)
    {
        // A mesh without UVs cannot show a texture, so only the materials of textured meshes load one.
        var used = new List<PreviewMaterial>();
        var seen = new HashSet<PreviewMaterial>(ReferenceEqualityComparer.Instance);
        foreach (var draw in scene.Draws)
        {
            if (draw.Mesh.Uvs is not null && (uint)draw.Material.SourceMaterial < (uint)vehicle.Materials.Count && seen.Add(draw.Material))
            {
                used.Add(draw.Material);
            }
        }

        var textures = new List<PreviewAssetRef>();
        var distinct = new HashSet<PreviewAssetRef>();
        foreach (var material in used)
        {
            if (vehicle.Materials[material.SourceMaterial].Diffuse is { } diffuse && distinct.Add(diffuse))
            {
                textures.Add(diffuse);
            }
        }

        foreach (var material in used)
        {
            if (vehicle.Materials[material.SourceMaterial].AoRoughMetal is { } aoRoughMetal && distinct.Add(aoRoughMetal))
            {
                textures.Add(aoRoughMetal);
            }
        }

        return (used, textures);
    }

    /// <summary>
    /// The base color of a material without a texture. It is a stable hash of the key with case ignored, because the
    /// game compares Ids without case, so the same material has the same color in every build and on every machine.
    /// </summary>
    internal static Vector3 BaseColorOf(string key)
    {
        var hash = 2166136261u;
        foreach (var character in key.ToUpperInvariant())
        {
            hash = (hash ^ character) * 16777619u;
        }

        // A muted color from the hue circle, given in sRGB and made linear with the pow 2.2 of the shading.
        const float Value = 0.78f;
        const float Saturation = 0.3f;
        var hue = (hash % 3600) / 600f;
        var chroma = Value * Saturation;
        var second = chroma * (1f - MathF.Abs((hue % 2f) - 1f));
        var (r, g, b) = (int)hue switch
        {
            0 => (chroma, second, 0f),
            1 => (second, chroma, 0f),
            2 => (0f, chroma, second),
            3 => (0f, second, chroma),
            4 => (second, 0f, chroma),
            _ => (chroma, 0f, second),
        };
        var offset = Value - chroma;
        return new Vector3(MathF.Pow(r + offset, 2.2f), MathF.Pow(g + offset, 2.2f), MathF.Pow(b + offset, 2.2f));
    }

    private (PreviewScene Scene, IReadOnlyList<PreviewNotice> Notices) BuildGeometry(
        ResolvedVehicle vehicle,
        IPreviewAssetSource source,
        PreviewLoadOptions options,
        CancellationToken cancellationToken)
    {
        var notices = new List<PreviewNotice>();
        var meshes = new MeshState[vehicle.Meshes.Count];
        var files = new List<FileJob>();
        var filesByKey = new Dictionary<string, FileJob>(StringComparer.Ordinal);
        for (var i = 0; i < meshes.Length; i++)
        {
            var state = meshes[i] = new MeshState(vehicle.Meshes[i]);
            try
            {
                state.FileKey = source.GetCacheKey(state.Ref.File);
            }
            catch (Exception exception) when (PreviewTextureLoader.IsAssetFailure(exception))
            {
                state.Failed = true;
                notices.Add(Failed(state.Ref, exception.Message));
                continue;
            }

            if (_meshes.TryGet(state.FileKey, state.Ref.MeshIndex, out var cached))
            {
                state.Mesh = cached;
                state.Triangles = cached.TriangleCount;
                state.Vertices = cached.VertexCount;
                state.Bytes = PreviewMeshCache.SizeOf(cached);
                state.Bounds = cached.LocalBounds;
                continue;
            }

            if (!filesByKey.TryGetValue(state.FileKey, out var file))
            {
                file = new FileJob(state.Ref.File, state.FileKey);
                filesByKey.Add(state.FileKey, file);
                files.Add(file);
            }

            file.Meshes.Add(state);
        }

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OpenAndMeasure(file, source, notices);
            }

            var placeholders = new List<PreviewPlaceholder>();
            foreach (var placeholder in vehicle.Placeholders)
            {
                placeholders.Add(new PreviewPlaceholder(
                    placeholder.Transform,
                    ValidHalfSize(placeholder.HalfSize),
                    placeholder.MissingId,
                    Map(placeholder.Reason),
                    placeholder.ModId));
            }

            var kept = new List<int>();
            for (var i = 0; i < vehicle.Draws.Count; i++)
            {
                var draw = vehicle.Draws[i];
                if ((uint)draw.Mesh >= (uint)meshes.Length)
                {
                    placeholders.Add(Box(draw.Transform, null, draw.PartId, PreviewPlaceholderReason.AssetFailed));
                    notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, draw.PartId, $"A model of the part names mesh {draw.Mesh}, which the scene does not list."));
                }
                else if (meshes[draw.Mesh].Failed)
                {
                    placeholders.Add(FailedBox(draw, meshes[draw.Mesh]));
                }
                else
                {
                    kept.Add(i);
                }
            }

            var hoseTriangles = HoseTriangleAllowance(vehicle, options);
            ApplyTriangleBudget(vehicle, meshes, kept, placeholders, notices, options.MaxTriangles - hoseTriangles);
            ApplyMeshByteBudget(vehicle, meshes, kept, placeholders, notices, options.MaxMeshBytes);

            var needed = new HashSet<MeshState>(kept.Select(i => meshes[vehicle.Draws[i].Mesh]));
            if (HoseMeshState(vehicle, meshes) is { Failed: false, Weight: <= MaxHoseMeshWeight } hoseMesh)
                needed.Add(hoseMesh);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Decode(file, needed, notices);
            }

            var materials = new Dictionary<(int Index, bool Glass), PreviewMaterial>();
            var draws = new List<PreviewDraw>(kept.Count);
            foreach (var i in kept)
            {
                var draw = vehicle.Draws[i];
                var state = meshes[draw.Mesh];
                if (state.Mesh is { } mesh)
                {
                    draws.Add(new PreviewDraw(mesh, MaterialFor(vehicle, draw, materials), draw.Transform));
                }
                else
                {
                    placeholders.Add(FailedBox(draw, state));
                }
            }

            AddHoses(vehicle, meshes, materials, draws, hoseTriangles, notices, cancellationToken);
            return (new PreviewScene(draws, placeholders), notices);
        }
        finally
        {
            foreach (var file in files)
            {
                file.Stream?.Dispose();
            }
        }
    }

    /// <summary>
    /// The triangles that the budget keeps for the fuel lines, so they cannot push parts out for more than a tenth of
    /// the budget, and a vehicle without fuel lines keeps all of it.
    /// </summary>
    private static int HoseTriangleAllowance(ResolvedVehicle vehicle, PreviewLoadOptions options) =>
        (int)Math.Min(vehicle.HoseRoutes.Count * (long)HoseMeshBuilder.MaxTriangles, options.MaxTriangles / 10);

    private static MeshState? HoseMeshState(ResolvedVehicle vehicle, MeshState[] meshes) =>
        vehicle.HoseRoutes.Count > 0 && (uint)vehicle.HoseMesh < (uint)meshes.Length ? meshes[vehicle.HoseMesh] : null;

    /// <summary>
    /// Adds one draw for each fuel line that has a length. The hose wears its part of the texture atlas when the hose
    /// mesh gives one and the material resolves, and has a plain color otherwise. A fuel line over the allowance is left
    /// out with a notice, because a box would not look like a hose.
    /// </summary>
    private static void AddHoses(
        ResolvedVehicle vehicle,
        MeshState[] meshes,
        Dictionary<(int Index, bool Glass), PreviewMaterial> materials,
        List<PreviewDraw> draws,
        int allowance,
        List<PreviewNotice> notices,
        CancellationToken cancellationToken)
    {
        if (vehicle.HoseRoutes.Count == 0)
            return;

        var patch = HoseMeshState(vehicle, meshes)?.Mesh is { } hoseMesh ? HoseUvPatch.Find(hoseMesh) : null;
        var material = patch is not null && (uint)vehicle.HoseMaterial < (uint)vehicle.Materials.Count
            ? MaterialFor(vehicle, new ResolvedDraw(vehicle.HoseMesh, vehicle.HoseMaterial, Matrix4x4.Identity, string.Empty, PreviewModelKind.Model), materials)
            : PreviewMaterial.Flat(DefaultBaseColor);

        var triangles = 0;
        var leftOut = 0;
        foreach (var route in vehicle.HoseRoutes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HoseMeshBuilder.Build(route, patch) is not { } mesh)
                continue;

            if (triangles + mesh.TriangleCount > allowance)
            {
                leftOut++;
                continue;
            }

            triangles += mesh.TriangleCount;
            draws.Add(new PreviewDraw(mesh, material, Matrix4x4.Identity));
        }

        if (leftOut > 0)
            notices.Add(new PreviewNotice(PreviewNoticeKind.Budget, "FuelLines", $"{leftOut} fuel lines are left out to stay inside the triangle budget."));
    }

    private static void OpenAndMeasure(FileJob file, IPreviewAssetSource source, List<PreviewNotice> notices)
    {
        GlbContainer container;
        try
        {
            file.Stream = source.TryOpenRead(file.File);
            if (file.Stream is null)
            {
                FailFile(file, notices, "The file is missing or too large.");
                return;
            }

            container = file.Container = GlbContainer.Read(file.Stream).WithCheckedBin(file.Stream);
        }
        catch (Exception exception) when (PreviewTextureLoader.IsAssetFailure(exception))
        {
            file.Stream?.Dispose();
            file.Stream = null;
            FailFile(file, notices, exception.Message);
            return;
        }

        foreach (var state in file.Meshes)
        {
            try
            {
                var size = GlbMeshDecoder.Measure(container, state.Ref.MeshIndex);
                state.Triangles = size.TriangleCount;
                state.Vertices = size.VertexCount;
                state.Bytes = size.DecodedBytes;
                state.Bounds = size.Bounds;

                // A mesh without triangles draws nothing, so it is not decoded, and its vertices can then cost nothing.
                if (size.TriangleCount == 0)
                {
                    state.Failed = true;
                    notices.Add(Failed(state.Ref, "The mesh has no triangles."));
                }
            }
            catch (Exception exception) when (PreviewTextureLoader.IsAssetFailure(exception))
            {
                state.Failed = true;
                notices.Add(Failed(state.Ref, exception.Message));
            }
        }
    }

    private void Decode(FileJob file, HashSet<MeshState> needed, List<PreviewNotice> notices)
    {
        if (file.Stream is null || file.Container is null)
        {
            return;
        }

        // Two mesh references can open the same bytes, for example through paths that differ only by case, and they
        // then share one decoded mesh.
        var decoded = new Dictionary<int, PreviewMesh>();
        foreach (var state in file.Meshes)
        {
            if (state.Failed || !needed.Contains(state))
            {
                continue;
            }

            if (decoded.TryGetValue(state.Ref.MeshIndex, out var shared))
            {
                state.Mesh = shared;
                continue;
            }

            try
            {
                // The limit is the weight that the budget counted, so the decode can never allocate more than the
                // budget allowed for this mesh, even when the file would claim other counts on a second read.
                var limit = (int)Math.Clamp(state.Weight, 1, int.MaxValue);
                var mesh = GlbMeshDecoder.Decode(file.Stream, file.Container, state.Ref.MeshIndex, limit);
                state.Mesh = mesh;
                decoded.Add(state.Ref.MeshIndex, mesh);
                _meshes.Add(file.Key, state.Ref.MeshIndex, mesh);
            }
            catch (Exception exception) when (PreviewTextureLoader.IsAssetFailure(exception))
            {
                state.Failed = true;
                notices.Add(Failed(state.Ref, exception.Message));
            }
        }

        file.Stream.Dispose();
        file.Stream = null;
    }

    private static void ApplyTriangleBudget(
        ResolvedVehicle vehicle,
        MeshState[] meshes,
        List<int> kept,
        List<PreviewPlaceholder> placeholders,
        List<PreviewNotice> notices,
        int maxTriangles)
    {
        long Weight(int draw) => meshes[vehicle.Draws[draw].Mesh].Weight;

        var total = kept.Sum(Weight);
        if (total <= maxTriangles)
        {
            return;
        }

        var sceneTotal = total;
        var dropped = new HashSet<int>();
        foreach (var draw in kept.OrderByDescending(Weight).ThenBy(draw => draw))
        {
            if (total <= maxTriangles)
            {
                break;
            }

            dropped.Add(draw);
            total -= Weight(draw);
        }

        foreach (var draw in kept.Where(dropped.Contains))
        {
            var resolved = vehicle.Draws[draw];
            var state = meshes[resolved.Mesh];
            placeholders.Add(Box(resolved.Transform, state.Bounds, state.Ref.Name, PreviewPlaceholderReason.Budget));
        }

        kept.RemoveAll(dropped.Contains);
        notices.Add(new PreviewNotice(
            PreviewNoticeKind.Budget,
            "Triangles",
            $"The models have {sceneTotal} triangles and the budget is {maxTriangles}, so the {dropped.Count} largest show as boxes."));
    }

    /// <summary>
    /// Keeps the decoded arrays of the distinct meshes inside <paramref name="maxBytes"/>. The triangle budget counts
    /// draws, so many meshes that each pass it can still need much memory together, for example when each mesh has
    /// its own copy of one large vertex array. The largest meshes become boxes for all their draws until the rest fit.
    /// </summary>
    private static void ApplyMeshByteBudget(
        ResolvedVehicle vehicle,
        MeshState[] meshes,
        List<int> kept,
        List<PreviewPlaceholder> placeholders,
        List<PreviewNotice> notices,
        long maxBytes)
    {
        var distinct = kept.Select(draw => meshes[vehicle.Draws[draw].Mesh]).Distinct().ToList();
        var total = distinct.Sum(state => state.Bytes);
        if (total <= maxBytes)
        {
            return;
        }

        var sceneTotal = total;
        var dropped = new HashSet<MeshState>();
        foreach (var state in distinct.OrderByDescending(state => state.Bytes))
        {
            if (total <= maxBytes)
            {
                break;
            }

            dropped.Add(state);
            total -= state.Bytes;
        }

        foreach (var draw in kept)
        {
            var resolved = vehicle.Draws[draw];
            var state = meshes[resolved.Mesh];
            if (dropped.Contains(state))
            {
                placeholders.Add(Box(resolved.Transform, state.Bounds, state.Ref.Name, PreviewPlaceholderReason.Budget));
            }
        }

        kept.RemoveAll(draw => dropped.Contains(meshes[vehicle.Draws[draw].Mesh]));
        notices.Add(new PreviewNotice(
            PreviewNoticeKind.Budget,
            "Meshes",
            $"The models need {sceneTotal} bytes of mesh data and the budget is {maxBytes}, so the {dropped.Count} largest meshes show as boxes."));
    }

    /// <summary>
    /// The bytes that the budget counts for one texture, those of the largest level that the target allows, a square
    /// one, so the decoded bytes can only be smaller than this estimate.
    /// </summary>
    private static long LevelBytes(int target) => 4L * target * target;

    private static (int Target, int Allowed) FitTextureBudget(int count, PreviewLoadOptions options, List<PreviewNotice> notices)
    {
        var target = options.TextureTarget;
        if (count * LevelBytes(target) <= options.MaxRgbaBytes)
        {
            return (target, count);
        }

        target = Math.Min(target, PreviewLoadOptions.ReducedTextureTarget);
        var allowed = (int)Math.Min(count, options.MaxRgbaBytes / LevelBytes(target));
        notices.Add(new PreviewNotice(
            PreviewNoticeKind.Budget,
            "Textures",
            allowed == count
                ? $"The {count} textures load at {target} pixels to stay inside {options.MaxRgbaBytes} bytes."
                : $"{count - allowed} of {count} textures keep a flat color to stay inside {options.MaxRgbaBytes} bytes."));
        return (target, allowed);
    }

    private static PreviewMaterial MaterialFor(ResolvedVehicle vehicle, ResolvedDraw draw, Dictionary<(int Index, bool Glass), PreviewMaterial> materials)
    {
        var index = (uint)draw.Material < (uint)vehicle.Materials.Count ? draw.Material : -1;
        var glass = draw.Kind == PreviewModelKind.Glass || (index >= 0 && vehicle.Materials[index].Glass);
        if (!materials.TryGetValue((index, glass), out var material))
        {
            var color = index < 0 ? DefaultBaseColor : BaseColorOf(vehicle.Materials[index].Key);
            material = new PreviewMaterial(color, null, null, glass) { SourceMaterial = index };
            materials.Add((index, glass), material);
        }

        return material;
    }

    private static void FailFile(FileJob file, List<PreviewNotice> notices, string detail)
    {
        foreach (var state in file.Meshes)
        {
            state.Failed = true;
        }

        notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, Subject(file.File), detail));
    }

    private static PreviewPlaceholder FailedBox(ResolvedDraw draw, MeshState state) =>
        Box(draw.Transform, state.Bounds, state.Ref.Name, PreviewPlaceholderReason.AssetFailed);

    /// <summary>
    /// A box at the declared bounds of the mesh when the file gives them, so a left-out tank keeps its size, and else
    /// a box of 1 m around the origin of the model.
    /// </summary>
    private static PreviewPlaceholder Box(Matrix4x4 transform, PreviewBounds? bounds, string missingId, PreviewPlaceholderReason reason) =>
        bounds is { IsFinite: true } box
            ? new PreviewPlaceholder(Matrix4x4.CreateTranslation(box.Center) * transform, (box.Max - box.Min) * 0.5f, missingId, reason)
            : new PreviewPlaceholder(transform, UnknownHalfSize, missingId, reason);

    private static Vector3 ValidHalfSize(Vector3 halfSize) =>
        float.IsFinite(halfSize.X) && float.IsFinite(halfSize.Y) && float.IsFinite(halfSize.Z)
            && halfSize.X >= 0f && halfSize.Y >= 0f && halfSize.Z >= 0f
            ? halfSize
            : UnknownHalfSize;

    private static PreviewPlaceholderReason Map(PlaceholderReason reason) => reason switch
    {
        PlaceholderReason.UnknownPart => PreviewPlaceholderReason.UnknownPart,
        PlaceholderReason.UnknownSubPart => PreviewPlaceholderReason.UnknownSubPart,
        PlaceholderReason.UnknownMesh => PreviewPlaceholderReason.UnknownMesh,
        PlaceholderReason.UnknownMaterial => PreviewPlaceholderReason.UnknownMaterial,
        PlaceholderReason.ModNotLoaded => PreviewPlaceholderReason.ModNotLoaded,
        PlaceholderReason.Budget => PreviewPlaceholderReason.Budget,
        _ => PreviewPlaceholderReason.AssetFailed,
    };

    private static PreviewNotice Failed(PreviewMeshRef mesh, string detail) =>
        new(PreviewNoticeKind.AssetFailed, Subject(mesh.File), $"Mesh {mesh.Name}: {detail}");

    private static string Subject(PreviewAssetRef asset) => $"{asset.Source}/{asset.Path}";

    private sealed class MeshState(PreviewMeshRef reference)
    {
        public PreviewMeshRef Ref { get; } = reference;

        public string? FileKey { get; set; }

        public PreviewMesh? Mesh { get; set; }

        public long Triangles { get; set; }

        public long Vertices { get; set; }

        /// <summary>The bytes of the decoded arrays, as <see cref="GlbMeshSize.DecodedBytes"/> estimates them or as the cached mesh has them.</summary>
        public long Bytes { get; set; }

        /// <summary>
        /// The triangles that the budget counts for one draw. The decoded arrays grow with the vertices too, so a mesh
        /// with more than three vertices per triangle counts a third of its vertices.
        /// </summary>
        public long Weight => Math.Max(Triangles, (Vertices + 2) / 3);

        public PreviewBounds? Bounds { get; set; }

        public bool Failed { get; set; }
    }

    private sealed class FileJob(PreviewAssetRef file, string key)
    {
        public PreviewAssetRef File { get; } = file;

        public string Key { get; } = key;

        public List<MeshState> Meshes { get; } = [];

        public Stream? Stream { get; set; }

        public GlbContainer? Container { get; set; }
    }
}
