using System.Numerics;
using System.Runtime.CompilerServices;
using Borea.Preview.Scene;

namespace Borea.Preview.Rendering;

/// <summary>
/// Draws a <see cref="PreviewScene"/> into a <see cref="FrameBuffer"/> on the CPU. The shading is a simple
/// form of the game shader ThumbnailMesh.frag: a linear diffuse from the texture or the base color, a
/// Lambert sun with a Blinn-Phong highlight from roughness and metalness, an ambient that the ambient
/// occlusion scales, a Reinhard tone map and an sRGB output. There is no emissive, and only
/// <see cref="RenderQuality.High"/> applies the normal maps. The frame is the same byte for byte for any
/// worker count, because each pixel row belongs to exactly one band and every band fills its triangles in scene order with math that does not depend on the band.
/// One instance keeps the vertices of each draw in the vehicle frame while the scene stays the same, and it
/// keeps its triangle, band and scratch buffers from frame to frame up to <see cref="RenderOptions.WorkBufferBytes"/>,
/// so a frame that only moves the camera transforms no cached vertex again and, below that cap, allocates
/// nothing that grows with the scene. An instance renders one frame at a time, so the caller must not call
/// <see cref="Render"/> again before the last call returned.
/// </summary>
public sealed class SoftwareRenderer
{
    /// <summary>The pixel rows in one unit of parallel work.</summary>
    public const int BandHeight = 16;

    private const int SubPixelBits = 8;
    private const int SubPixel = 1 << SubPixelBits;
    private const int HalfSubPixel = SubPixel / 2;

    /// <summary>
    /// Triangles are clipped to this many view widths on each side as well as to the near plane, so the
    /// fixed-point screen coordinates stay inside 32-bit integers for a frame of <see cref="FrameBuffer.MaxSize"/>.
    /// </summary>
    private const float GuardBand = 256f;

    /// <summary>A triangle clipped by the near plane and the four guard planes has at most 3 + 5 corners.</summary>
    private const int MaxPolygon = 8;

    private const int NearPlaneCode = 1;
    private const int ClipCodes = 0b11111;

    private const byte PlaceholderAlpha = 115;
    private const float MinPlaceholderSide = 0.01f;

    /// <summary>The spare screen triangles that a buffer may keep above twice the count of its last frame.</summary>
    private const int SlackTriangles = 64;

    /// <summary>The reflectance at normal incidence of a dielectric, the F0 of the PBR shaders of the game.</summary>
    private const float DielectricF0 = 0.04f;

    /// <summary>
    /// The smallest GGX alpha, so a fully smooth material keeps a highlight of finite size instead of a
    /// single bright point that the sampling misses.
    /// </summary>
    private const float MinAlpha = 0.045f;

    private const float MaxSpecularPower = 1024f;

    /// <summary>The steps of the linear to sRGB table, fine enough that a step is below 0.25 of one byte also near black.</summary>
    private const int SrgbTableSteps = 16384;

    /// <summary>The linear color of every placeholder, an orange that no stock part has.</summary>
    private static readonly Vector3 PlaceholderColor = new(1f, 0.42f, 0f);

    /// <summary>The factor on the color of a PartModelGlass draw, which the preview draws opaque.</summary>
    internal static readonly Vector3 GlassTint = new(0.25f, 0.3f, 0.35f);

    /// <summary>The box from -0.5 to 0.5 that each placeholder scales to its size.</summary>
    private static readonly PreviewMesh UnitBox = PreviewTestScenes.CreateBox();

    private static readonly byte[] SrgbTable = BuildSrgbTable();

    private readonly Vector4[] _planes = new Vector4[6];
    private PreviewScene? _scene;
    private long _cacheLimit = -1;
    private DrawState[] _states = [];
    private DrawJob[] _jobs = [];
    private int[] _bandStarts = [];
    private int[] _bandCursors = [];
    private TriangleRef[] _bandItems = [];
    private int[] _nearest = [];
    private readonly Stack<Scratch> _scratch = new();
    private long _transformedVertexCount;
    private long _cachedBytes;
    private long _retainedWorkBytes;

    /// <summary>
    /// How many vertices this renderer moved from the frame of their mesh to the vehicle frame since it was
    /// made. A frame that only moves the camera adds only the vertices of the draws that are not cached.
    /// </summary>
    public long TransformedVertexCount => Interlocked.Read(ref _transformedVertexCount);

    /// <summary>
    /// The bytes of the vehicle frame positions and normals that the renderer keeps for the current scene. It
    /// stays at or below <see cref="RenderOptions.WorldCacheBytes"/>.
    /// </summary>
    public long CachedBytes => Interlocked.Read(ref _cachedBytes);

    /// <summary>
    /// The bytes of the screen triangle, band list and vertex scratch buffers that the renderer kept after the
    /// last frame. It stays at or below <see cref="RenderOptions.WorkBufferBytes"/> of that frame.
    /// </summary>
    public long RetainedWorkBytes => Interlocked.Read(ref _retainedWorkBytes);

    /// <summary>
    /// Drops the cached vertices and the buffers, for example when the viewer leaves the screen. The next
    /// frame builds them again.
    /// </summary>
    public void Release()
    {
        _scene = null;
        _cacheLimit = -1;
        _states = [];
        _jobs = [];
        _bandStarts = [];
        _bandCursors = [];
        _bandItems = [];
        _nearest = [];
        lock (_scratch)
        {
            _scratch.Clear();
        }

        Interlocked.Exchange(ref _cachedBytes, 0);
        Interlocked.Exchange(ref _retainedWorkBytes, 0);
    }

    /// <summary>
    /// Clears <paramref name="target"/> to the background and draws the opaque draws with a depth test. The
    /// placeholders follow as translucent boxes that test against the depth of the opaque draws but do not
    /// write it, so a placeholder hidden inside a part stays hidden and one in the open lets the parts behind
    /// it show through.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before or between the bands.</exception>
    public RenderStats Render(PreviewScene scene, OrbitCamera camera, FrameBuffer target, RenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        Prepare(scene, Math.Max(0, options.WorldCacheBytes));
        target.Clear(options.Background);

        // A state that is not visible in this frame holds no triangles, so the trim after the frame can free its buffer.
        foreach (var state in _states)
        {
            state.TriangleCount = 0;
        }

        var view = camera.View;
        var projection = camera.Projection((float)target.Width / target.Height);
        var frame = new FrameSetup(view, view * projection, projection, target.Width, target.Height, options);
        var normalMaps = frame.Quality == RenderQuality.High;
        FrustumPlanes(frame.ViewProjection, _planes);

        // The pixels that one meter covers at a depth of one meter, for the test of draws below one pixel.
        var pixelsPerMeter = options.SkipSubPixelDraws ? projection.M22 * target.Height * 0.5f : 0f;

        // The opaque jobs come first, so every band meets its opaque triangles before its translucent ones.
        EnsureLength(ref _jobs, scene.Draws.Count + scene.Placeholders.Count);
        var jobCount = 0;
        var drawsVisited = 0;
        for (var i = 0; i < scene.Draws.Count; i++)
        {
            var draw = scene.Draws[i];
            if (IsVisible(draw.Mesh, draw.Transform, _planes, view, pixelsPerMeter))
            {
                _jobs[jobCount++] = new DrawJob(draw.Mesh, draw.Transform, Surface.From(draw.Material, draw.Mesh.Uvs is not null, normalMaps), 255, _states[i]);
                drawsVisited++;
            }
        }

        var placeholdersVisited = 0;
        for (var i = 0; i < scene.Placeholders.Count; i++)
        {
            var placeholder = scene.Placeholders[i];
            var size = Vector3.Max(placeholder.HalfSize * 2f, new Vector3(MinPlaceholderSide));
            var model = Matrix4x4.CreateScale(size) * placeholder.Transform;
            if (IsVisible(UnitBox, model, _planes, view, 0f))
            {
                var surface = new Surface(PlaceholderColor, Vector3.One, null, null, null);
                _jobs[jobCount++] = new DrawJob(UnitBox, model, surface, PlaceholderAlpha, _states[scene.Draws.Count + i]);
                placeholdersVisited++;
            }
        }

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxWorkers),
            CancellationToken = cancellationToken,
        };

        var jobs = _jobs;
        long triangleCount;
        try
        {
            // Each worker takes one scratch from the renderer for all the draws it sets up, so the scratch count
            // stays at the worker count and the trim below can free it.
            Parallel.For(
                0,
                jobCount,
                parallel,
                TakeScratch,
                (index, _, scratch) =>
                {
                    SetupDraw(jobs[index], index, frame, scratch);
                    return scratch;
                },
                ReturnScratch);

            var bandCount = (target.Height + BandHeight - 1) / BandHeight;
            triangleCount = BinIntoBands(jobs, jobCount, bandCount);
            var bandStarts = _bandStarts;
            var bandItems = _bandItems;
            EnsureLength(ref _nearest, target.Width * target.Height);
            var nearest = _nearest;

            Parallel.For(0, bandCount, parallel, band =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    FillBand(band, bandStarts[band], bandStarts[band + 1], bandItems, jobs, frame, target, nearest);
                }
            });
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            TrimWorkBuffers(Math.Max(0, options.WorkBufferBytes));
        }

        return new RenderStats(drawsVisited, placeholdersVisited, triangleCount);
    }

    private Scratch TakeScratch()
    {
        lock (_scratch)
        {
            return _scratch.TryPop(out var scratch) ? scratch : new Scratch();
        }
    }

    private void ReturnScratch(Scratch scratch)
    {
        lock (_scratch)
        {
            _scratch.Push(scratch);
        }
    }

    /// <summary>
    /// Runs after each frame, when no worker uses the buffers any more. Each triangle buffer first gives back
    /// its slack, so a draw that is now culled or smaller on screen does not keep the buffer of its largest
    /// frame. When the rest is still above <paramref name="cap"/>, the largest buffers are freed until it
    /// fits, and the next frame allocates them again.
    /// </summary>
    private void TrimWorkBuffers(long cap)
    {
        var total = 0L;
        foreach (var state in _states)
        {
            state.TrimSlack();
            total += state.TriangleBytes;
        }

        total += BandBytes();
        lock (_scratch)
        {
            var scratchBytes = 0L;
            foreach (var scratch in _scratch)
            {
                scratchBytes += scratch.Bytes;
            }

            total += scratchBytes;
            if (total > cap)
            {
                const int BandLists = -1;
                const int AllScratch = -2;
                var buffers = new List<(long Bytes, int Index)> { (BandBytes(), BandLists), (scratchBytes, AllScratch) };
                for (var i = 0; i < _states.Length; i++)
                {
                    if (_states[i].TriangleBytes > 0)
                    {
                        buffers.Add((_states[i].TriangleBytes, i));
                    }
                }

                buffers.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
                foreach (var (bytes, index) in buffers)
                {
                    if (total <= cap)
                    {
                        break;
                    }

                    total -= bytes;
                    if (index == BandLists)
                    {
                        _bandStarts = [];
                        _bandCursors = [];
                        _bandItems = [];
                    }
                    else if (index == AllScratch)
                    {
                        _scratch.Clear();
                    }
                    else
                    {
                        _states[index].FreeTriangles();
                    }
                }
            }
        }

        Interlocked.Exchange(ref _retainedWorkBytes, total);
    }

    private long BandBytes() =>
        sizeof(int) * ((long)_bandStarts.Length + _bandCursors.Length) + (long)Unsafe.SizeOf<TriangleRef>() * _bandItems.Length;

    /// <summary>
    /// Makes one state for each draw and each placeholder of a new scene, and decides which of them may keep
    /// their vehicle frame vertices. The draws are taken in scene order, and each one that still fits under
    /// <paramref name="cacheLimit"/> gets its cache, so a large draw past the limit does not stop a small one
    /// after it. The other draws are transformed again on each frame.
    /// </summary>
    private void Prepare(PreviewScene scene, long cacheLimit)
    {
        if (ReferenceEquals(scene, _scene) && cacheLimit == _cacheLimit)
        {
            return;
        }

        var states = new DrawState[scene.Draws.Count + scene.Placeholders.Count];
        var reserved = 0L;
        for (var i = 0; i < states.Length; i++)
        {
            var mesh = i < scene.Draws.Count ? scene.Draws[i].Mesh : UnitBox;
            var bytes = WorldBytes(mesh);
            var cacheable = bytes <= cacheLimit - reserved;
            if (cacheable)
            {
                reserved += bytes;
            }

            states[i] = new DrawState(cacheable);
        }

        _states = states;
        _scene = scene;
        _cacheLimit = cacheLimit;
        Interlocked.Exchange(ref _cachedBytes, 0);
    }

    /// <summary>The bytes of the vehicle frame positions and normals of one draw of <paramref name="mesh"/>.</summary>
    private static long WorldBytes(PreviewMesh mesh) => sizeof(float) * ((long)mesh.Positions.Length + mesh.Normals.Length);

    private static void EnsureLength<T>(ref T[] array, int length)
    {
        if (array.Length < length)
        {
            array = new T[Math.Max(length, array.Length + array.Length / 2)];
        }
    }

    /// <summary>
    /// The six frustum planes of a row-vector view projection, each with a unit normal that points inside,
    /// so the plane value of a point is its signed distance in meters.
    /// </summary>
    private static void FrustumPlanes(Matrix4x4 m, Vector4[] planes)
    {
        var column0 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var column1 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var column2 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var column3 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        planes[0] = column3 + column0;
        planes[1] = column3 - column0;
        planes[2] = column3 + column1;
        planes[3] = column3 - column1;
        planes[4] = column2;
        planes[5] = column3 - column2;
        for (var i = 0; i < planes.Length; i++)
        {
            planes[i] /= new Vector3(planes[i].X, planes[i].Y, planes[i].Z).Length();
        }
    }

    /// <summary>
    /// Whether the bounding sphere of the draw reaches into the view. When <paramref name="pixelsPerMeter"/>
    /// is positive, a sphere whose nearest point shows smaller than one pixel counts as not visible too. That
    /// leaves out a whole small part at once, which costs at most about one pixel of it. Single triangles
    /// below one pixel are not left out, because a dense mesh has many of them in each pixel, and a pixel
    /// whose triangles were all left out would show the background.
    /// </summary>
    private static bool IsVisible(PreviewMesh mesh, Matrix4x4 model, Vector4[] planes, Matrix4x4 view, float pixelsPerMeter)
    {
        var center = Vector3.Transform(mesh.SphereCenter, model);
        var radius = mesh.SphereRadius * MaxStretch(model);
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(center.Z) || !float.IsFinite(radius))
        {
            return false;
        }

        foreach (var plane in planes)
        {
            if (plane.X * center.X + plane.Y * center.Y + plane.Z * center.Z + plane.W < -radius)
            {
                return false;
            }
        }

        if (pixelsPerMeter > 0f)
        {
            // The camera looks along -Z, so the depth of the nearest point of the sphere is -z minus the radius.
            var nearest = -Vector3.Transform(center, view).Z - radius;
            if (nearest > 0f && 2f * radius * pixelsPerMeter < nearest)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// An upper bound of how much <paramref name="model"/> can stretch a vector. It is the Gershgorin bound
    /// of the largest eigenvalue of the Gram matrix of the three basis rows, which is exact when the rows
    /// are orthogonal, as for a rotation with a scale, and stays safe for a sheared matrix.
    /// </summary>
    private static float MaxStretch(Matrix4x4 model)
    {
        var row1 = new Vector3(model.M11, model.M12, model.M13);
        var row2 = new Vector3(model.M21, model.M22, model.M23);
        var row3 = new Vector3(model.M31, model.M32, model.M33);
        var g12 = MathF.Abs(Vector3.Dot(row1, row2));
        var g13 = MathF.Abs(Vector3.Dot(row1, row3));
        var g23 = MathF.Abs(Vector3.Dot(row2, row3));
        var bound = MathF.Max(row1.LengthSquared() + g12 + g13, MathF.Max(row2.LengthSquared() + g12 + g23, row3.LengthSquared() + g13 + g23));
        return MathF.Sqrt(bound);
    }

    /// <summary>
    /// Turns the triangles of one job into screen triangles in the buffer of its state. The vertices go to
    /// the vehicle frame first, from the cache of the state when it has one, and then to clip space with the
    /// view projection of the frame. A cached and an uncached draw use the same two steps, so the frame does
    /// not depend on what the cache holds.
    /// </summary>
    private void SetupDraw(DrawJob job, int jobIndex, FrameSetup frame, Scratch scratch)
    {
        var state = job.State;
        state.TriangleCount = 0;

        // A singular transform flattens the mesh to zero area, so nothing of it can be seen.
        if (!Matrix4x4.Invert(job.Model, out var modelInverse))
        {
            return;
        }

        // The inverse transpose moves a normal to the vehicle frame and also keeps it pointing out of a
        // mirrored draw. The view only turns and moves, so it moves a normal on to camera space unchanged in length.
        var normalMatrix = Matrix4x4.Transpose(modelInverse);
        var mirrored = job.Model.GetDeterminant() < 0f;
        var translucent = job.Alpha < 255;

        // A translucent box must not blend its back faces too, so placeholders always cull them.
        var cull = frame.CullBackFaces || translucent;

        var mesh = job.Mesh;
        var vertexCount = mesh.VertexCount;
        var hasNormals = mesh.Normals.Length != 0;
        EnsureLength(ref scratch.Clip, vertexCount);
        EnsureLength(ref scratch.ViewNormals, hasNormals ? vertexCount : 0);
        var clip = scratch.Clip;
        var viewNormals = scratch.ViewNormals;

        var worldPositions = state.WorldPositions;
        var worldNormals = state.WorldNormals;
        if (worldPositions is null || worldNormals is null)
        {
            if (state.Cacheable)
            {
                worldPositions = new float[mesh.Positions.Length];
                worldNormals = new float[mesh.Normals.Length];
            }
            else
            {
                EnsureLength(ref scratch.Positions, mesh.Positions.Length);
                EnsureLength(ref scratch.Normals, mesh.Normals.Length);
                worldPositions = scratch.Positions;
                worldNormals = scratch.Normals;
            }

            ToWorld(mesh, job.Model, normalMatrix, worldPositions, worldNormals);
            Interlocked.Add(ref _transformedVertexCount, vertexCount);

            // The state takes the arrays only when they are full, so a cancelled frame leaves no half cache.
            if (state.Cacheable)
            {
                state.WorldPositions = worldPositions;
                state.WorldNormals = worldNormals;
                Interlocked.Add(ref _cachedBytes, WorldBytes(mesh));
            }
        }

        for (var v = 0; v < vertexCount; v++)
        {
            clip[v] = Vector4.Transform(Read3(worldPositions, v), frame.ViewProjection);
        }

        if (hasNormals)
        {
            for (var v = 0; v < vertexCount; v++)
            {
                viewNormals[v] = Normalized(Vector3.TransformNormal(Read3(worldNormals, v), frame.View));
            }
        }

        SetupTriangles(job, jobIndex, frame, clip, viewNormals, hasNormals, normalMatrix, mirrored, cull);
    }

    /// <summary>
    /// Moves the positions and the unit normals of <paramref name="mesh"/> to the vehicle frame. A normal
    /// without a finite direction stays zero, and the triangle then gets its geometric normal.
    /// </summary>
    private static void ToWorld(PreviewMesh mesh, Matrix4x4 model, Matrix4x4 normalMatrix, float[] positions, float[] normals)
    {
        for (var v = 0; v < mesh.VertexCount; v++)
        {
            Write3(positions, v, Vector3.Transform(Read3(mesh.Positions, v), model));
        }

        if (mesh.Normals.Length != 0)
        {
            for (var v = 0; v < mesh.VertexCount; v++)
            {
                Write3(normals, v, Normalized(Vector3.TransformNormal(Read3(mesh.Normals, v), normalMatrix)));
            }
        }
    }

    private static void SetupTriangles(DrawJob job, int jobIndex, FrameSetup frame, Vector4[] clip, Vector3[] viewNormals, bool hasNormals, Matrix4x4 normalMatrix, bool mirrored, bool cull)
    {
        var state = job.State;
        var mesh = job.Mesh;
        var translucent = job.Alpha < 255;
        Span<ClipVertex> polygon = stackalloc ClipVertex[MaxPolygon];
        Span<ClipVertex> scratch = stackalloc ClipVertex[MaxPolygon];
        Span<Corner> corners = stackalloc Corner[MaxPolygon];

        var indices = mesh.Indices;
        for (var t = 0; t < indices.Length; t += 3)
        {
            var i0 = (int)indices[t];
            var i1 = (int)indices[t + 1];
            var i2 = (int)indices[t + 2];
            var count = ClipTriangle(
                Vertex(clip, viewNormals, hasNormals, mesh, i0, i1, i2, 0, normalMatrix, frame.View),
                Vertex(clip, viewNormals, hasNormals, mesh, i0, i1, i2, 1, normalMatrix, frame.View),
                Vertex(clip, viewNormals, hasNormals, mesh, i0, i1, i2, 2, normalMatrix, frame.View),
                polygon,
                scratch);
            if (count < 3 || !Project(polygon[..count], frame, corners))
            {
                continue;
            }

            // The shoelace sum in screen space, where y points down, is negative for a triangle that is
            // counterclockwise in the y-up view, which is a front face of an unmirrored draw.
            long doubleArea = 0;
            for (var k = 0; k < count; k++)
            {
                var next = k + 1 == count ? 0 : k + 1;
                doubleArea += (long)corners[k].X * corners[next].Y - (long)corners[next].X * corners[k].Y;
            }

            if (doubleArea == 0)
            {
                continue;
            }

            var front = (doubleArea < 0) != mirrored;
            if (!front && cull)
            {
                continue;
            }

            // A back face that is drawn is lit as if seen from its front.
            if (!front)
            {
                for (var k = 0; k < count; k++)
                {
                    corners[k].Normal = -corners[k].Normal;
                }
            }

            var color = translucent ? ShadeFlat(corners[..count], job, frame) : new Rgba8(0, 0, 0, 255);
            for (var k = 1; k + 1 < count; k++)
            {
                // The rasterizer wants a positive area, so a negative polygon is walked the other way round.
                var (b, c) = doubleArea > 0 ? (k, k + 1) : (k + 1, k);
                if (state.TriangleCount == state.Triangles.Length)
                {
                    state.Grow(mesh.TriangleCount);
                }

                if (TryMakeTriangle(corners[0], corners[b], corners[c], color, jobIndex, frame, out state.Triangles[state.TriangleCount]))
                {
                    state.TriangleCount++;
                }
            }
        }
    }

    /// <summary>
    /// Corner <paramref name="corner"/> of the triangle i0, i1, i2 in clip space with its UV and its unit
    /// normal in camera space. A mesh without normals, or a vertex whose normal has no direction, gets the
    /// geometric normal of the triangle, which goes to camera space in the same two steps as a vertex normal.
    /// </summary>
    private static ClipVertex Vertex(Vector4[] clip, Vector3[] viewNormals, bool hasNormals, PreviewMesh mesh, int i0, int i1, int i2, int corner, Matrix4x4 normalMatrix, Matrix4x4 view)
    {
        var index = corner switch
        {
            0 => i0,
            1 => i1,
            _ => i2,
        };

        var normal = hasNormals ? viewNormals[index] : Vector3.Zero;
        if (normal == Vector3.Zero)
        {
            var p0 = Read3(mesh.Positions, i0);
            var face = Vector3.Cross(Read3(mesh.Positions, i1) - p0, Read3(mesh.Positions, i2) - p0);
            normal = Normalized(Vector3.TransformNormal(Normalized(Vector3.TransformNormal(face, normalMatrix)), view));
        }

        var uvs = mesh.Uvs;
        var uv = uvs is null ? Vector2.Zero : new Vector2(uvs[2 * index], uvs[2 * index + 1]);
        return new ClipVertex(clip[index], uv, normal);
    }

    /// <summary>
    /// Clips a triangle in clip space to the near plane z = 0 and to the guard band, and gives the corner
    /// count of the result in <paramref name="polygon"/>, or 0 when nothing is left. A triangle that lies
    /// fully outside one of the view planes is rejected at once.
    /// </summary>
    private static int ClipTriangle(ClipVertex a, ClipVertex b, ClipVertex c, Span<ClipVertex> polygon, Span<ClipVertex> scratch)
    {
        var codeA = OutCode(a.Position);
        var codeB = OutCode(b.Position);
        var codeC = OutCode(c.Position);
        if ((codeA & codeB & codeC) != 0)
        {
            return 0;
        }

        polygon[0] = a;
        polygon[1] = b;
        polygon[2] = c;
        var planes = (codeA | codeB | codeC) & ClipCodes;
        var count = 3;
        for (var plane = 0; planes != 0 && plane < 5; plane++)
        {
            if ((planes & (1 << plane)) == 0)
            {
                continue;
            }

            count = ClipAgainst(plane, polygon[..count], scratch);
            if (count < 3)
            {
                return 0;
            }

            scratch[..count].CopyTo(polygon);
        }

        return count;
    }

    /// <summary>
    /// The low five bits name the clip planes a vertex is outside of: near, then the guard band at +x, -x,
    /// +y and -y. The next five bits name the view planes at +x, -x, +y, -y and far, which only reject.
    /// </summary>
    private static int OutCode(Vector4 v)
    {
        var code = 0;
        if (v.Z < 0f)
        {
            code |= NearPlaneCode;
        }

        if (v.X > GuardBand * v.W)
        {
            code |= 1 << 1;
        }

        if (v.X < -GuardBand * v.W)
        {
            code |= 1 << 2;
        }

        if (v.Y > GuardBand * v.W)
        {
            code |= 1 << 3;
        }

        if (v.Y < -GuardBand * v.W)
        {
            code |= 1 << 4;
        }

        if (v.X > v.W)
        {
            code |= 1 << 5;
        }

        if (v.X < -v.W)
        {
            code |= 1 << 6;
        }

        if (v.Y > v.W)
        {
            code |= 1 << 7;
        }

        if (v.Y < -v.W)
        {
            code |= 1 << 8;
        }

        if (v.Z > v.W)
        {
            code |= 1 << 9;
        }

        return code;
    }

    private static float PlaneDistance(int plane, Vector4 v) => plane switch
    {
        0 => v.Z,
        1 => GuardBand * v.W - v.X,
        2 => GuardBand * v.W + v.X,
        3 => GuardBand * v.W - v.Y,
        _ => GuardBand * v.W + v.Y,
    };

    /// <summary>
    /// One Sutherland-Hodgman step that keeps the side of <paramref name="plane"/> with a distance of 0 or
    /// more. The UV and the normal are interpolated in clip space with the position, which is correct
    /// because clip space is still linear in the homogeneous coordinates.
    /// </summary>
    private static int ClipAgainst(int plane, ReadOnlySpan<ClipVertex> input, Span<ClipVertex> output)
    {
        var count = 0;
        for (var i = 0; i < input.Length; i++)
        {
            var current = input[i];
            var next = input[i + 1 == input.Length ? 0 : i + 1];
            var currentDistance = PlaneDistance(plane, current.Position);
            var nextDistance = PlaneDistance(plane, next.Position);
            if (currentDistance >= 0f)
            {
                output[count++] = current;
            }

            if ((currentDistance >= 0f) != (nextDistance >= 0f))
            {
                var s = currentDistance / (currentDistance - nextDistance);
                output[count++] = new ClipVertex(
                    Vector4.Lerp(current.Position, next.Position, s),
                    Vector2.Lerp(current.Uv, next.Uv, s),
                    Vector3.Lerp(current.Normal, next.Normal, s));
            }
        }

        return count;
    }

    /// <summary>
    /// Converts clipped corners to screen coordinates in 1/256 pixel with y down, to the depth of the
    /// projection and to 1/w for the perspective-correct interpolation. It gives false when a corner is not
    /// in front of the camera or not finite, which only an overflow in the transform can cause after the
    /// near clip.
    /// </summary>
    private static bool Project(ReadOnlySpan<ClipVertex> polygon, FrameSetup frame, Span<Corner> corners)
    {
        for (var k = 0; k < polygon.Length; k++)
        {
            var v = polygon[k].Position;
            if (!(v.W > 0f))
            {
                return false;
            }

            var inverseW = 1.0 / v.W;
            var x = (v.X * inverseW * 0.5 + 0.5) * frame.Width * SubPixel;
            var y = (0.5 - v.Y * inverseW * 0.5) * frame.Height * SubPixel;
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > int.MaxValue / 2 || Math.Abs(y) > int.MaxValue / 2)
            {
                return false;
            }

            corners[k] = new Corner
            {
                X = (int)Math.Round(x),
                Y = (int)Math.Round(y),
                Z = (float)(v.Z * inverseW),
                InverseW = (float)inverseW,
                Uv = polygon[k].Uv,
                Normal = polygon[k].Normal,
            };
        }

        return true;
    }

    /// <summary>The one color of a translucent placeholder triangle, lit with the mean normal of its corners.</summary>
    private static Rgba8 ShadeFlat(ReadOnlySpan<Corner> corners, DrawJob job, FrameSetup frame)
    {
        var sum = Vector3.Zero;
        foreach (var corner in corners)
        {
            sum += corner.Normal;
        }

        var linear = Light(job.Surface.BaseColor, 1f, 1f, 0f, Normalized(sum), frame);
        return Encode(linear, frame, job.Alpha);
    }

    /// <summary>
    /// The linear color of a surface point. It follows ThumbnailMesh.frag in a simple form: F0 is 0.04 mixed
    /// toward the albedo by the metalness, the sun gives a Lambert term on the part of the albedo that is not
    /// metal plus a normalized Blinn-Phong highlight, and the ambient is the albedo times the ambient
    /// occlusion. The viewer is taken to be along +Z in camera space, so the half vector is one value for
    /// the whole frame.
    /// </summary>
    private static Vector3 Light(Vector3 albedo, float ambientOcclusion, float roughness, float metalness, Vector3 normal, FrameSetup frame)
    {
        var color = frame.Ambient * ambientOcclusion * albedo;
        var nDotL = Vector3.Dot(normal, frame.Sun);
        if (nDotL > 0f)
        {
            var f0 = Vector3.Lerp(new Vector3(DielectricF0), albedo, metalness);

            // The Blinn-Phong power that matches a GGX alpha of roughness squared. The factor is the
            // normalized Blinn-Phong distribution (n + 2) / (2 pi) over the microfacet 4, times the pi that
            // the Lambert term also leaves out, so the highlight keeps about the same energy for every roughness.
            var alpha = MathF.Max(roughness * roughness, MinAlpha);
            var power = Math.Clamp(2f / (alpha * alpha) - 2f, 1f, MaxSpecularPower);
            var nDotH = MathF.Max(0f, Vector3.Dot(normal, frame.Half));
            var specular = (power + 2f) / 8f * MathF.Pow(nDotH, power);

            color += frame.SunIntensity * nDotL * (albedo * (1f - metalness) + f0 * specular);
        }

        return color;
    }

    /// <summary>
    /// The Reinhard tone map of tonemapReinhardSimple in the game shader Tonemapping.glsl, then the sRGB
    /// encoding through a table.
    /// </summary>
    private static Rgba8 Encode(Vector3 linear, FrameSetup frame, byte alpha)
    {
        var exposure = frame.Exposure;
        var mapped = linear * exposure / (Vector3.One + linear / exposure);
        return new Rgba8(ToSrgbByte(mapped.X), ToSrgbByte(mapped.Y), ToSrgbByte(mapped.Z), alpha);
    }

    private static byte ToSrgbByte(float linear)
    {
        if (!(linear > 0f))
        {
            return 0;
        }

        return linear >= 1f ? (byte)255 : SrgbTable[(int)(linear * SrgbTableSteps + 0.5f)];
    }

    private static byte[] BuildSrgbTable()
    {
        var table = new byte[SrgbTableSteps + 1];
        for (var i = 0; i < table.Length; i++)
        {
            var value = (float)i / SrgbTableSteps;
            var srgb = value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
            table[i] = (byte)(Math.Clamp(srgb, 0f, 1f) * 255f + 0.5f);
        }

        return table;
    }

    private static Vector3 Read3(float[] values, int vertex) => new(values[3 * vertex], values[3 * vertex + 1], values[3 * vertex + 2]);

    private static void Write3(float[] values, int vertex, Vector3 value)
    {
        values[3 * vertex] = value.X;
        values[3 * vertex + 1] = value.Y;
        values[3 * vertex + 2] = value.Z;
    }

    /// <summary>The unit vector of <paramref name="value"/>, or zero when it has no finite direction.</summary>
    private static Vector3 Normalized(Vector3 value)
    {
        var length = value.Length();
        return length > 1e-12f && float.IsFinite(length) ? value / length : Vector3.Zero;
    }

    /// <summary>
    /// Builds a screen triangle with a positive edge area and the range of pixels whose centers it can
    /// cover. It gives false when the triangle has no area or covers no pixel center of the frame.
    /// </summary>
    private static bool TryMakeTriangle(in Corner a, in Corner b, in Corner c, Rgba8 color, int jobIndex, FrameSetup frame, out ScreenTriangle triangle)
    {
        triangle = default;
        var area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (area <= 0)
        {
            return false;
        }

        var minX = Math.Max(0, FirstPixelAtOrAfter(Math.Min(a.X, Math.Min(b.X, c.X))));
        var maxX = Math.Min(frame.Width - 1, LastPixelAtOrBefore(Math.Max(a.X, Math.Max(b.X, c.X))));
        var minY = Math.Max(0, FirstPixelAtOrAfter(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        var maxY = Math.Min(frame.Height - 1, LastPixelAtOrBefore(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        if (minX > maxX || minY > maxY)
        {
            return false;
        }

        triangle = new ScreenTriangle
        {
            C0 = a,
            C1 = b,
            C2 = c,
            InverseArea = 1.0 / area,
            Color = color,
            Job = jobIndex,
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
        };
        return true;
    }

    /// <summary>The first pixel whose center at index * 256 + 128 is at or after the fixed-point coordinate.</summary>
    private static int FirstPixelAtOrAfter(int value) => (int)(((long)value - HalfSubPixel + SubPixel - 1) >> SubPixelBits);

    private static int LastPixelAtOrBefore(int value) => (int)(((long)value - HalfSubPixel) >> SubPixelBits);

    /// <summary>
    /// Twice the signed area of the triangle a, b, p in screen space with y down. It is positive when p is
    /// on the inner side of the edge from a to b for a triangle with a positive area.
    /// </summary>
    private static long Edge(long ax, long ay, long bx, long by, long px, long py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    /// <summary>
    /// The top-left fill rule. A pixel center exactly on an edge belongs to the triangle only when the edge is
    /// a top edge (horizontal, with the inside below) or a left edge (going up, with the inside to the right),
    /// so two triangles that share an edge never both fill a pixel and never both miss it.
    /// </summary>
    private static long FillBias(int ax, int ay, int bx, int by)
    {
        var dy = by - ay;
        var isTopLeft = (dy == 0 && bx - ax > 0) || dy < 0;
        return isTopLeft ? 0 : -1;
    }

    /// <summary>
    /// Lists, for each band of <see cref="BandHeight"/> rows, the triangles that reach it, in scene order, and
    /// gives the count of all triangles. Band b holds the items from _bandStarts[b] up to _bandStarts[b + 1].
    /// The lists are built in one pass so they do not depend on the worker count, and the buffers are kept for
    /// the next frame.
    /// </summary>
    private long BinIntoBands(DrawJob[] jobs, int jobCount, int bandCount)
    {
        EnsureLength(ref _bandStarts, bandCount + 1);
        EnsureLength(ref _bandCursors, bandCount);
        var starts = _bandStarts;
        Array.Clear(starts, 0, bandCount + 1);

        long triangleCount = 0;
        for (var job = 0; job < jobCount; job++)
        {
            var state = jobs[job].State;
            for (var i = 0; i < state.TriangleCount; i++)
            {
                ref readonly var triangle = ref state.Triangles[i];
                for (var band = triangle.MinY / BandHeight; band <= triangle.MaxY / BandHeight; band++)
                {
                    starts[band + 1]++;
                }
            }

            triangleCount += state.TriangleCount;
        }

        for (var band = 0; band < bandCount; band++)
        {
            starts[band + 1] += starts[band];
        }

        EnsureLength(ref _bandItems, starts[bandCount]);
        var items = _bandItems;
        var cursors = _bandCursors;
        Array.Copy(starts, cursors, bandCount);
        for (var job = 0; job < jobCount; job++)
        {
            var state = jobs[job].State;
            for (var i = 0; i < state.TriangleCount; i++)
            {
                ref readonly var triangle = ref state.Triangles[i];
                for (var band = triangle.MinY / BandHeight; band <= triangle.MaxY / BandHeight; band++)
                {
                    items[cursors[band]++] = new TriangleRef(job, i);
                }
            }
        }

        return triangleCount;
    }

    /// <summary>
    /// Fills one band in two steps. The opaque triangles only write the depth and the item of the nearest
    /// triangle per pixel, and the visible pixels are shaded once after that, so the shading cost does not
    /// grow with the overdraw. The translucent triangles follow and blend over the shaded pixels. The rows
    /// of the band in <paramref name="nearest"/> belong to this band only, so the bands do not share memory.
    /// </summary>
    private static void FillBand(int band, int start, int end, TriangleRef[] items, DrawJob[] jobs, FrameSetup frame, FrameBuffer target, int[] nearest)
    {
        var top = band * BandHeight;
        var bottom = Math.Min(top + BandHeight, target.Height) - 1;
        var width = target.Width;
        var bandNearest = nearest.AsSpan(top * width, (bottom - top + 1) * width);
        bandNearest.Fill(-1);
        var shaded = false;
        for (var item = start; item < end; item++)
        {
            ref readonly var t = ref Triangle(items, jobs, item);
            var opaque = t.Color.A == 255;
            if (!opaque && !shaded)
            {
                ShadeBand(top, bottom, bandNearest, items, jobs, frame, target);
                shaded = true;
            }

            Rasterize(in t, item, opaque, top, bottom, bandNearest, target);
        }

        if (!shaded)
        {
            ShadeBand(top, bottom, bandNearest, items, jobs, frame, target);
        }
    }

    private static ref readonly ScreenTriangle Triangle(TriangleRef[] items, DrawJob[] jobs, int item)
    {
        var reference = items[item];
        return ref jobs[reference.Job].State.Triangles[reference.Index];
    }

    private static void Rasterize(in ScreenTriangle t, int item, bool opaque, int top, int bottom, Span<int> nearest, FrameBuffer target)
    {
        var width = target.Width;
        var depth = target.Depth;
        var firstRow = Math.Max(t.MinY, top);
        var lastRow = Math.Min(t.MaxY, bottom);

        // Each edge value is the weight of the vertex opposite to it, so e0 belongs to vertex 0.
        var bias0 = FillBias(t.C1.X, t.C1.Y, t.C2.X, t.C2.Y);
        var bias1 = FillBias(t.C2.X, t.C2.Y, t.C0.X, t.C0.Y);
        var bias2 = FillBias(t.C0.X, t.C0.Y, t.C1.X, t.C1.Y);
        var step0 = -(long)(t.C2.Y - t.C1.Y) * SubPixel;
        var step1 = -(long)(t.C0.Y - t.C2.Y) * SubPixel;
        var step2 = -(long)(t.C1.Y - t.C0.Y) * SubPixel;
        var startX = (long)t.MinX * SubPixel + HalfSubPixel;

        for (var y = firstRow; y <= lastRow; y++)
        {
            var py = (long)y * SubPixel + HalfSubPixel;
            var e0 = Edge(t.C1.X, t.C1.Y, t.C2.X, t.C2.Y, startX, py);
            var e1 = Edge(t.C2.X, t.C2.Y, t.C0.X, t.C0.Y, startX, py);
            var e2 = Edge(t.C0.X, t.C0.Y, t.C1.X, t.C1.Y, startX, py);
            var pixel = y * width + t.MinX;
            var local = (y - top) * width + t.MinX;

            for (var x = t.MinX; x <= t.MaxX; x++, pixel++, local++, e0 += step0, e1 += step1, e2 += step2)
            {
                // The sign bit of the OR is set when any of the three biased edge values is negative.
                if (((e0 + bias0) | (e1 + bias1) | (e2 + bias2)) < 0)
                {
                    continue;
                }

                var z = (float)((e0 * (double)t.C0.Z + e1 * (double)t.C1.Z + e2 * (double)t.C2.Z) * t.InverseArea);
                if (!(z < depth[pixel]))
                {
                    continue;
                }

                if (opaque)
                {
                    depth[pixel] = z;
                    nearest[local] = item;
                }
                else
                {
                    Blend(target.Rgba, pixel, t.Color);
                }
            }
        }
    }

    /// <summary>
    /// Shades the visible pixels of one band. The quality is chosen once for the band, so the Standard loop
    /// has no branch for the High quality in it.
    /// </summary>
    private static void ShadeBand(int top, int bottom, Span<int> nearest, TriangleRef[] items, DrawJob[] jobs, FrameSetup frame, FrameBuffer target)
    {
        if (frame.Quality == RenderQuality.High)
        {
            ShadeBandHigh(top, bottom, nearest, items, jobs, frame, target);
            return;
        }

        var width = target.Width;
        for (var y = top; y <= bottom; y++)
        {
            var local = (y - top) * width;
            for (var x = 0; x < width; x++, local++)
            {
                var item = nearest[local];
                if (item >= 0)
                {
                    ref readonly var t = ref Triangle(items, jobs, item);
                    ref readonly var job = ref jobs[t.Job];
                    Write(target.Rgba, y * width + x, ShadePixel(in t, in job, x, y, frame));
                }
            }
        }
    }

    /// <summary>
    /// The High form of <see cref="ShadeBand"/>. Neighboring pixels mostly belong to the same triangle, so
    /// the surface axes of the last triangle are kept and only built again when the triangle changes.
    /// </summary>
    private static void ShadeBandHigh(int top, int bottom, Span<int> nearest, TriangleRef[] items, DrawJob[] jobs, FrameSetup frame, FrameBuffer target)
    {
        var width = target.Width;
        var lastItem = -1;
        var axes = default(SurfaceAxes);
        for (var y = top; y <= bottom; y++)
        {
            var local = (y - top) * width;
            for (var x = 0; x < width; x++, local++)
            {
                var item = nearest[local];
                if (item >= 0)
                {
                    ref readonly var t = ref Triangle(items, jobs, item);
                    ref readonly var job = ref jobs[t.Job];
                    if (item != lastItem)
                    {
                        lastItem = item;
                        axes = job.Surface.Normal is null ? default : SurfaceAxes.Of(in t, frame);
                    }

                    Write(target.Rgba, y * width + x, ShadePixelHigh(in t, in job, in axes, x, y, frame));
                }
            }
        }
    }

    /// <summary>
    /// The color of one pixel of an opaque triangle. The edge values at the pixel center are the screen
    /// weights of the corners, and dividing each by its w gives the weights in the plane of the triangle,
    /// which makes the UV and the normal correct under perspective.
    /// </summary>
    private static Rgba8 ShadePixel(in ScreenTriangle t, in DrawJob job, int x, int y, FrameSetup frame)
    {
        var px = (long)x * SubPixel + HalfSubPixel;
        var py = (long)y * SubPixel + HalfSubPixel;
        var w0 = Edge(t.C1.X, t.C1.Y, t.C2.X, t.C2.Y, px, py) * t.C0.InverseW;
        var w1 = Edge(t.C2.X, t.C2.Y, t.C0.X, t.C0.Y, px, py) * t.C1.InverseW;
        var w2 = Edge(t.C0.X, t.C0.Y, t.C1.X, t.C1.Y, px, py) * t.C2.InverseW;
        var sum = w0 + w1 + w2;
        if (!(sum > 0f))
        {
            (w0, w1, w2, sum) = (1f, 1f, 1f, 3f);
        }

        var l0 = w0 / sum;
        var l1 = w1 / sum;
        var l2 = w2 / sum;
        var uv = t.C0.Uv * l0 + t.C1.Uv * l1 + t.C2.Uv * l2;
        var normal = Normalized(t.C0.Normal * l0 + t.C1.Normal * l1 + t.C2.Normal * l2);
        if (normal == Vector3.Zero)
        {
            normal = Vector3.UnitZ;
        }

        var surface = job.Surface;
        var albedo = surface.Diffuse is null ? surface.BaseColor : TextureSampler.SampleDiffuse(surface.Diffuse, uv.X, uv.Y) * surface.Tint;
        var (ambientOcclusion, roughness, metalness) = (1f, 1f, 0f);
        if (surface.AoRoughMetal is not null)
        {
            var data = TextureSampler.SampleData(surface.AoRoughMetal, uv.X, uv.Y);
            (ambientOcclusion, roughness, metalness) = (data.X, data.Y, data.Z);
        }

        return Encode(Light(albedo, ambientOcclusion, roughness, metalness, normal, frame), frame, 255);
    }

    /// <summary>
    /// The color of one pixel of an opaque triangle at the High quality. The UV, the normal and the material
    /// values come as in <see cref="ShadePixel"/>. A normal map then bends the normal as getNormalFromMap_ShaderX
    /// in the game shader Common/SharedFrag.glsl does: it takes x and y from the red and green channels, rebuilds
    /// z, and turns them with the frame of cotangent_frame, whose tangent and bitangent are the in-plane axes
    /// of the triangle crossed with the interpolated normal of the pixel. The net mapping is red along +grad(u)
    /// and green along -grad(v), which is image up, for the reason that <see cref="SurfaceAxes"/> gives. The
    /// plain normal stays when the surface has no normal map or the UVs of the triangle give no direction.
    /// </summary>
    private static Rgba8 ShadePixelHigh(in ScreenTriangle t, in DrawJob job, in SurfaceAxes axes, int x, int y, FrameSetup frame)
    {
        var px = (long)x * SubPixel + HalfSubPixel;
        var py = (long)y * SubPixel + HalfSubPixel;
        var w0 = Edge(t.C1.X, t.C1.Y, t.C2.X, t.C2.Y, px, py) * t.C0.InverseW;
        var w1 = Edge(t.C2.X, t.C2.Y, t.C0.X, t.C0.Y, px, py) * t.C1.InverseW;
        var w2 = Edge(t.C0.X, t.C0.Y, t.C1.X, t.C1.Y, px, py) * t.C2.InverseW;
        var sum = w0 + w1 + w2;
        if (!(sum > 0f))
        {
            (w0, w1, w2, sum) = (1f, 1f, 1f, 3f);
        }

        var l0 = w0 / sum;
        var l1 = w1 / sum;
        var l2 = w2 / sum;
        var uv = t.C0.Uv * l0 + t.C1.Uv * l1 + t.C2.Uv * l2;
        var normal = Normalized(t.C0.Normal * l0 + t.C1.Normal * l1 + t.C2.Normal * l2);
        if (normal == Vector3.Zero)
        {
            normal = Vector3.UnitZ;
        }

        var surface = job.Surface;
        if (surface.Normal is not null)
        {
            // The scale-invariant frame of cotangent_frame, which divides both axes by the longer one.
            var tangent = Vector3.Cross(normal, axes.U);
            var bitangent = Vector3.Cross(normal, axes.V);
            var scale = 1f / MathF.Sqrt(MathF.Max(tangent.LengthSquared(), bitangent.LengthSquared()));
            if (float.IsFinite(scale))
            {
                var map = TextureSampler.SampleNormal(surface.Normal, uv.X, uv.Y);
                var mapped = Normalized(map.X * scale * tangent - map.Y * scale * bitangent + map.Z * normal);
                if (mapped != Vector3.Zero)
                {
                    normal = mapped;
                }
            }
        }

        var albedo = surface.Diffuse is null ? surface.BaseColor : TextureSampler.SampleDiffuse(surface.Diffuse, uv.X, uv.Y) * surface.Tint;
        var (ambientOcclusion, roughness, metalness) = (1f, 1f, 0f);
        if (surface.AoRoughMetal is not null)
        {
            var data = TextureSampler.SampleData(surface.AoRoughMetal, uv.X, uv.Y);
            (ambientOcclusion, roughness, metalness) = (data.X, data.Y, data.Z);
        }

        return Encode(Light(albedo, ambientOcclusion, roughness, metalness, normal, frame), frame, 255);
    }

    private static void Write(byte[] rgba, int pixel, Rgba8 color)
    {
        var offset = pixel * 4;
        rgba[offset] = color.R;
        rgba[offset + 1] = color.G;
        rgba[offset + 2] = color.B;
        rgba[offset + 3] = color.A;
    }

    /// <summary>The straight-alpha "over" operation, so a placeholder in front of the transparent background keeps its color.</summary>
    private static void Blend(byte[] rgba, int pixel, Rgba8 color)
    {
        var offset = pixel * 4;
        var sourceAlpha = color.A / 255f;
        var targetAlpha = rgba[offset + 3] / 255f * (1f - sourceAlpha);
        var alpha = sourceAlpha + targetAlpha;
        rgba[offset] = (byte)((color.R * sourceAlpha + rgba[offset] * targetAlpha) / alpha + 0.5f);
        rgba[offset + 1] = (byte)((color.G * sourceAlpha + rgba[offset + 1] * targetAlpha) / alpha + 0.5f);
        rgba[offset + 2] = (byte)((color.B * sourceAlpha + rgba[offset + 2] * targetAlpha) / alpha + 0.5f);
        rgba[offset + 3] = (byte)(alpha * 255f + 0.5f);
    }

    /// <summary>
    /// What a draw is shaded with. <see cref="BaseColor"/> already holds the glass tint, and
    /// <see cref="Tint"/> is the factor for the diffuse texture. The textures are null when the mesh has no
    /// UVs, because there is nothing to sample them with. <see cref="Normal"/> is set only for a frame that
    /// applies normal maps, and never for glass, which keeps the shading of the Standard quality.
    /// </summary>
    private readonly record struct Surface(Vector3 BaseColor, Vector3 Tint, PreviewImage? Diffuse, PreviewImage? AoRoughMetal, PreviewImage? Normal)
    {
        public static Surface From(PreviewMaterial material, bool hasUvs, bool normalMaps)
        {
            var tint = material.Glass ? GlassTint : Vector3.One;
            var normal = hasUvs && normalMaps && !material.Glass ? material.Normal : null;
            return new Surface(material.BaseColor * tint, tint, hasUvs ? material.Diffuse : null, hasUvs ? material.AoRoughMetal : null, normal);
        }
    }

    private readonly record struct DrawJob(PreviewMesh Mesh, Matrix4x4 Model, Surface Surface, byte Alpha, DrawState State);

    /// <summary>
    /// The two in-plane vectors of one screen triangle in camera space that the normal of a pixel is crossed
    /// with to give the tangent and the bitangent of cotangent_frame in Common/SharedFrag.glsl. That function
    /// solves the screen derivatives of the position and the UV, and its tangent cross(dp2, N) * du1 +
    /// cross(N, dp1) * du2 is the same as cross(N, dp1 * du2 - dp2 * du1). With the edges e1 and e2 of the
    /// triangle in place of the derivatives, this is grad(u) times det = dot(N, cross(e1, e2)) when N is the
    /// face normal, and the bitangent is the same for v.
    /// </summary>
    /// <remarks>
    /// The sign of det decides whether the relief shows raised or pushed in. The corners are read in their
    /// stored order, which always has a positive screen area because SetupTriangles walks a negative polygon
    /// the other way round, and their normals are the ones after the back face flip. So cross(e1, e2) points
    /// away from the viewer and N toward it, and det is negative for every drawn pixel, also on a mirrored draw
    /// and on a back face that is drawn. The mesh index order would not give this, because a mirrored draw
    /// turns it around. The game has the same fixed sign, because the derivatives follow the screen and
    /// ThumbnailRenderer culls back faces. The axes here are the negated sums, so cross(N, U) points along
    /// +grad(u) and cross(N, V) along +grad(v). getNormalFromMap_ShaderX sets normalMap.x = -normalMap.x, and
    /// that flip is not copied, because it cancels against the negative det of a visible face. The net mapping
    /// of the game is red = +grad(u) and green = -grad(v), which ShadePixelHigh applies directly. The game
    /// ignores the GLB TANGENT of a part mesh, because MeshReference.Load imports only
    /// VertexImportFlags.Normals | UVs, so this renderer does not read it either. The import axes of
    /// GltfUtils.LoadMeshAssetForPrim (Brutal.Gltf) and the view handedness of the game camera are not in the
    /// decompiled source, so it is not verified that the game maps a part to the screen with the same
    /// orientation as this renderer.
    /// </remarks>
    private readonly record struct SurfaceAxes(Vector3 U, Vector3 V)
    {
        public static SurfaceAxes Of(in ScreenTriangle t, FrameSetup frame)
        {
            var p0 = CameraPoint(in t.C0, frame);
            var e1 = CameraPoint(in t.C1, frame) - p0;
            var e2 = CameraPoint(in t.C2, frame) - p0;
            var step1 = t.C1.Uv - t.C0.Uv;
            var step2 = t.C2.Uv - t.C0.Uv;
            return new SurfaceAxes(e2 * step1.X - e1 * step2.X, e2 * step1.Y - e1 * step2.Y);
        }

        /// <summary>
        /// A projected corner back in camera space, where the camera looks along -Z, so w is the depth -z and
        /// the clip x and y are the camera x and y times the projection scales.
        /// </summary>
        private static Vector3 CameraPoint(in Corner corner, FrameSetup frame)
        {
            var w = 1f / corner.InverseW;
            var ndcX = 2f * corner.X / (frame.Width * SubPixel) - 1f;
            var ndcY = 1f - 2f * corner.Y / (frame.Height * SubPixel);
            return new Vector3(ndcX * w / frame.ProjectionX, ndcY * w / frame.ProjectionY, -w);
        }
    }

    /// <summary>A triangle in the list of a band, by its job and its place in the buffer of the state of that job.</summary>
    private readonly record struct TriangleRef(int Job, int Index);

    /// <summary>
    /// What the renderer keeps for one draw or placeholder of the current scene. The vehicle frame positions
    /// and normals are filled on the first frame that shows the draw when <see cref="Cacheable"/> is true. The
    /// triangle buffer holds the screen triangles of the last frame, and only the job of this state writes it.
    /// </summary>
    private sealed class DrawState(bool cacheable)
    {
        public bool Cacheable { get; } = cacheable;

        public float[]? WorldPositions { get; set; }

        public float[]? WorldNormals { get; set; }

        public ScreenTriangle[] Triangles { get; private set; } = [];

        public int TriangleCount { get; set; }

        /// <summary>
        /// Makes the buffer larger. It grows by doubling up to the triangle count of the mesh, which is the most
        /// that a frame without clipped triangles can need, so a large draw does not get a buffer of twice its size.
        /// </summary>
        public void Grow(int meshTriangles)
        {
            var length = Triangles.Length;
            var next = Math.Max(16, Math.Min(length * 2, meshTriangles + 16));
            var triangles = Triangles;
            Array.Resize(ref triangles, next > length ? next : length * 2);
            Triangles = triangles;
        }

        public long TriangleBytes => (long)Unsafe.SizeOf<ScreenTriangle>() * Triangles.Length;

        /// <summary>
        /// Shrinks a buffer that is more than twice as long as the triangles of the last frame need, to one and
        /// a half times that, so a draw that is now culled or smaller on screen gives back its memory and a
        /// small change of the count on the next frame does not make it grow at once.
        /// </summary>
        public void TrimSlack()
        {
            var count = TriangleCount;
            if (Triangles.Length <= 2L * count + SlackTriangles)
            {
                return;
            }

            if (count == 0)
            {
                Triangles = [];
                return;
            }

            var triangles = Triangles;
            Array.Resize(ref triangles, count + count / 2);
            Triangles = triangles;
        }

        public void FreeTriangles()
        {
            Triangles = [];
            TriangleCount = 0;
        }
    }

    /// <summary>
    /// The vertex buffers that one worker uses for the draws it sets up: the clip positions and camera space
    /// normals of the draw, and the vehicle frame vertices of a draw that the world cache does not hold.
    /// </summary>
    private sealed class Scratch
    {
        public Vector4[] Clip = [];
        public Vector3[] ViewNormals = [];
        public float[] Positions = [];
        public float[] Normals = [];

        public long Bytes =>
            (long)Unsafe.SizeOf<Vector4>() * Clip.Length
            + (long)Unsafe.SizeOf<Vector3>() * ViewNormals.Length
            + sizeof(float) * ((long)Positions.Length + Normals.Length);
    }

    /// <summary>A polygon corner in clip space, with its UV and its normal in camera space.</summary>
    private readonly record struct ClipVertex(Vector4 Position, Vector2 Uv, Vector3 Normal);

    private sealed class FrameSetup
    {
        public FrameSetup(Matrix4x4 view, Matrix4x4 viewProjection, Matrix4x4 projection, int width, int height, RenderOptions options)
        {
            View = view;
            ViewProjection = viewProjection;
            ProjectionX = projection.M11;
            ProjectionY = projection.M22;
            Width = width;
            Height = height;
            CullBackFaces = options.CullBackFaces;
            Quality = options.Quality;
            Sun = Normalized(options.SunDirection) is var sun && sun != Vector3.Zero ? sun : Vector3.UnitZ;
            Half = Normalized(Sun + Vector3.UnitZ) is var half && half != Vector3.Zero ? half : Vector3.UnitZ;
            SunIntensity = options.SunIntensity;
            Ambient = options.Ambient;
            Exposure = float.IsFinite(options.Exposure) && options.Exposure > 0f ? options.Exposure : 1f;
        }

        public Matrix4x4 View { get; }

        public Matrix4x4 ViewProjection { get; }

        /// <summary>
        /// The M11 and M22 of the perspective projection, which take camera space x and y to clip space. With
        /// them and w a screen corner goes back to camera space.
        /// </summary>
        public float ProjectionX { get; }

        public float ProjectionY { get; }

        public int Width { get; }

        public int Height { get; }

        public bool CullBackFaces { get; }

        public RenderQuality Quality { get; }

        /// <summary>The unit vector toward the sun in camera space.</summary>
        public Vector3 Sun { get; }

        /// <summary>The half vector between the sun and a viewer along +Z in camera space.</summary>
        public Vector3 Half { get; }

        public float SunIntensity { get; }

        public float Ambient { get; }

        public float Exposure { get; }
    }

    /// <summary>
    /// A projected corner in 1/256 pixel with y down, with the projection depth, 1/w, the UV and the camera
    /// space normal. It is mutable so a drawn back face can turn its normals around in place.
    /// </summary>
    private struct Corner
    {
        public int X;
        public int Y;
        public float Z;
        public float InverseW;
        public Vector2 Uv;
        public Vector3 Normal;
    }

    /// <summary>
    /// A triangle with a positive edge area, its pixel range in the frame and the draw it comes from.
    /// <see cref="Color"/> is the final color of a translucent triangle, and its alpha is 255 for an opaque
    /// triangle, whose pixels are shaded one by one.
    /// </summary>
    private struct ScreenTriangle
    {
        public Corner C0;
        public Corner C1;
        public Corner C2;
        public double InverseArea;
        public Rgba8 Color;
        public int Job;
        public int MinX;
        public int MaxX;
        public int MinY;
        public int MaxY;
    }
}
