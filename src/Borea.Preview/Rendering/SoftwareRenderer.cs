using System.Buffers;
using System.Numerics;
using Borea.Preview.Scene;

namespace Borea.Preview.Rendering;

/// <summary>
/// Draws a <see cref="PreviewScene"/> into a <see cref="FrameBuffer"/> on the CPU. The shading is a simple
/// form of the game shader ThumbnailMesh.frag: a linear diffuse from the texture or the base color, a
/// Lambert sun with a Blinn-Phong highlight from roughness and metalness, an ambient that the ambient
/// occlusion scales, a Reinhard tone map and an sRGB output. There are no normal maps and no emissive.
/// The frame is the same byte for byte for any worker count, because each pixel row belongs to exactly one
/// band and every band fills its triangles in scene order with math that does not depend on the band.
/// </summary>
public static class SoftwareRenderer
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

    /// <summary>
    /// Clears <paramref name="target"/> to the background and draws the opaque draws with a depth test. The
    /// placeholders follow as translucent boxes that test against the depth of the opaque draws but do not
    /// write it, so a placeholder hidden inside a part stays hidden and one in the open lets the parts behind
    /// it show through.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before or between the bands.</exception>
    public static RenderStats Render(PreviewScene scene, OrbitCamera camera, FrameBuffer target, RenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        target.Clear(options.Background);

        var view = camera.View;
        var frame = new FrameSetup(view, view * camera.Projection((float)target.Width / target.Height), target.Width, target.Height, options);
        var planes = FrustumPlanes(frame.ViewProjection);

        // The opaque jobs come first, so every band meets its opaque triangles before its translucent ones.
        var jobs = new List<DrawJob>(scene.Draws.Count + scene.Placeholders.Count);
        var drawsVisited = 0;
        foreach (var draw in scene.Draws)
        {
            if (IsVisible(draw.Mesh, draw.Transform, planes))
            {
                jobs.Add(new DrawJob(draw.Mesh, draw.Transform, Surface.From(draw.Material, draw.Mesh.Uvs is not null), 255));
                drawsVisited++;
            }
        }

        var placeholdersVisited = 0;
        foreach (var placeholder in scene.Placeholders)
        {
            var size = Vector3.Max(placeholder.HalfSize * 2f, new Vector3(MinPlaceholderSide));
            var model = Matrix4x4.CreateScale(size) * placeholder.Transform;
            if (IsVisible(UnitBox, model, planes))
            {
                jobs.Add(new DrawJob(UnitBox, model, new Surface(PlaceholderColor, Vector3.One, null, null), PlaceholderAlpha));
                placeholdersVisited++;
            }
        }

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxWorkers),
            CancellationToken = cancellationToken,
        };

        var jobArray = jobs.ToArray();
        var perJob = new ScreenTriangle[jobArray.Length][];
        Parallel.For(0, jobArray.Length, parallel, index => perJob[index] = SetupDraw(jobArray[index], index, frame));

        var triangles = perJob.SelectMany(list => list).ToArray();
        var bands = BinIntoBands(triangles, target.Height);

        Parallel.For(0, bands.Length, parallel, band =>
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                FillBand(band, bands[band], triangles, jobArray, frame, target);
            }
        });
        cancellationToken.ThrowIfCancellationRequested();

        return new RenderStats(drawsVisited, placeholdersVisited, triangles.Length);
    }

    /// <summary>
    /// The six frustum planes of a row-vector view projection, each with a unit normal that points inside,
    /// so the plane value of a point is its signed distance in meters.
    /// </summary>
    private static Vector4[] FrustumPlanes(Matrix4x4 m)
    {
        var column0 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var column1 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var column2 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var column3 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        Vector4[] planes = [column3 + column0, column3 - column0, column3 + column1, column3 - column1, column2, column3 - column2];
        for (var i = 0; i < planes.Length; i++)
        {
            planes[i] /= new Vector3(planes[i].X, planes[i].Y, planes[i].Z).Length();
        }

        return planes;
    }

    private static bool IsVisible(PreviewMesh mesh, Matrix4x4 model, Vector4[] planes)
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

    private static ScreenTriangle[] SetupDraw(DrawJob job, int jobIndex, FrameSetup frame)
    {
        // A singular transform flattens the mesh to zero area, so nothing of it can be seen.
        if (!Matrix4x4.Invert(job.Model * frame.View, out var modelViewInverse))
        {
            return [];
        }

        // The inverse transpose moves a normal to camera space and also keeps it pointing out of a mirrored draw.
        var normalMatrix = Matrix4x4.Transpose(modelViewInverse);
        var mirrored = job.Model.GetDeterminant() < 0f;
        var translucent = job.Alpha < 255;

        // A translucent box must not blend its back faces too, so placeholders always cull them.
        var cull = frame.CullBackFaces || translucent;
        var toClip = job.Model * frame.ViewProjection;

        var mesh = job.Mesh;
        var positions = mesh.Positions;
        var clip = new Vector4[mesh.VertexCount];
        for (var v = 0; v < clip.Length; v++)
        {
            clip[v] = Vector4.Transform(new Vector3(positions[3 * v], positions[3 * v + 1], positions[3 * v + 2]), toClip);
        }

        var viewNormals = new Vector3[mesh.Normals.Length == 0 ? 0 : mesh.VertexCount];
        for (var v = 0; v < viewNormals.Length; v++)
        {
            viewNormals[v] = Normalized(Vector3.TransformNormal(Read3(mesh.Normals, v), normalMatrix));
        }

        var output = new List<ScreenTriangle>();
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
                Vertex(clip, viewNormals, mesh, i0, i1, i2, 0, normalMatrix),
                Vertex(clip, viewNormals, mesh, i0, i1, i2, 1, normalMatrix),
                Vertex(clip, viewNormals, mesh, i0, i1, i2, 2, normalMatrix),
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
                if (TryMakeTriangle(corners[0], corners[b], corners[c], color, jobIndex, frame, out var triangle))
                {
                    output.Add(triangle);
                }
            }
        }

        return [.. output];
    }

    /// <summary>
    /// Corner <paramref name="corner"/> of the triangle i0, i1, i2 in clip space with its UV and its unit
    /// normal in camera space. A mesh without normals, or a vertex whose normal has no direction, gets the
    /// geometric normal of the triangle.
    /// </summary>
    private static ClipVertex Vertex(Vector4[] clip, Vector3[] viewNormals, PreviewMesh mesh, int i0, int i1, int i2, int corner, Matrix4x4 normalMatrix)
    {
        var index = corner switch
        {
            0 => i0,
            1 => i1,
            _ => i2,
        };

        var normal = viewNormals.Length == 0 ? Vector3.Zero : viewNormals[index];
        if (normal == Vector3.Zero)
        {
            var p0 = Read3(mesh.Positions, i0);
            var face = Vector3.Cross(Read3(mesh.Positions, i1) - p0, Read3(mesh.Positions, i2) - p0);
            normal = Normalized(Vector3.TransformNormal(face, normalMatrix));
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
    /// Lists, for each band of <see cref="BandHeight"/> rows, the triangles that reach it, in scene order.
    /// The lists are built in one pass so they do not depend on the worker count.
    /// </summary>
    private static int[][] BinIntoBands(ScreenTriangle[] triangles, int height)
    {
        var bandCount = (height + BandHeight - 1) / BandHeight;
        var counts = new int[bandCount];
        foreach (var triangle in triangles)
        {
            for (var band = triangle.MinY / BandHeight; band <= triangle.MaxY / BandHeight; band++)
            {
                counts[band]++;
            }
        }

        var bands = new int[bandCount][];
        for (var band = 0; band < bandCount; band++)
        {
            bands[band] = new int[counts[band]];
            counts[band] = 0;
        }

        for (var index = 0; index < triangles.Length; index++)
        {
            for (var band = triangles[index].MinY / BandHeight; band <= triangles[index].MaxY / BandHeight; band++)
            {
                bands[band][counts[band]++] = index;
            }
        }

        return bands;
    }

    /// <summary>
    /// Fills one band in two steps. The opaque triangles only write the depth and the index of the nearest
    /// triangle per pixel, and the visible pixels are shaded once after that, so the shading cost does not
    /// grow with the overdraw. The translucent triangles follow and blend over the shaded pixels.
    /// </summary>
    private static void FillBand(int band, int[] triangleIndices, ScreenTriangle[] triangles, DrawJob[] jobs, FrameSetup frame, FrameBuffer target)
    {
        var top = band * BandHeight;
        var bottom = Math.Min(top + BandHeight, target.Height) - 1;
        var width = target.Width;
        var pixelCount = (bottom - top + 1) * width;
        var nearest = ArrayPool<int>.Shared.Rent(pixelCount);
        try
        {
            nearest.AsSpan(0, pixelCount).Fill(-1);
            var shaded = false;
            foreach (var index in triangleIndices)
            {
                ref readonly var t = ref triangles[index];
                var opaque = t.Color.A == 255;
                if (!opaque && !shaded)
                {
                    ShadeBand(top, bottom, nearest, triangles, jobs, frame, target);
                    shaded = true;
                }

                Rasterize(in t, index, opaque, top, bottom, nearest, target);
            }

            if (!shaded)
            {
                ShadeBand(top, bottom, nearest, triangles, jobs, frame, target);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(nearest);
        }
    }

    private static void Rasterize(in ScreenTriangle t, int index, bool opaque, int top, int bottom, int[] nearest, FrameBuffer target)
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
                    nearest[local] = index;
                }
                else
                {
                    Blend(target.Rgba, pixel, t.Color);
                }
            }
        }
    }

    private static void ShadeBand(int top, int bottom, int[] nearest, ScreenTriangle[] triangles, DrawJob[] jobs, FrameSetup frame, FrameBuffer target)
    {
        var width = target.Width;
        for (var y = top; y <= bottom; y++)
        {
            var local = (y - top) * width;
            for (var x = 0; x < width; x++, local++)
            {
                var index = nearest[local];
                if (index >= 0)
                {
                    ref readonly var t = ref triangles[index];
                    Write(target.Rgba, y * width + x, ShadePixel(in t, jobs[t.Job], x, y, frame));
                }
            }
        }
    }

    /// <summary>
    /// The color of one pixel of an opaque triangle. The edge values at the pixel center are the screen
    /// weights of the corners, and dividing each by its w gives the weights in the plane of the triangle,
    /// which makes the UV and the normal correct under perspective.
    /// </summary>
    private static Rgba8 ShadePixel(in ScreenTriangle t, DrawJob job, int x, int y, FrameSetup frame)
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
    /// UVs, because there is nothing to sample them with.
    /// </summary>
    private readonly record struct Surface(Vector3 BaseColor, Vector3 Tint, PreviewImage? Diffuse, PreviewImage? AoRoughMetal)
    {
        public static Surface From(PreviewMaterial material, bool hasUvs)
        {
            var tint = material.Glass ? GlassTint : Vector3.One;
            return new Surface(material.BaseColor * tint, tint, hasUvs ? material.Diffuse : null, hasUvs ? material.AoRoughMetal : null);
        }
    }

    private readonly record struct DrawJob(PreviewMesh Mesh, Matrix4x4 Model, Surface Surface, byte Alpha);

    /// <summary>A polygon corner in clip space, with its UV and its normal in camera space.</summary>
    private readonly record struct ClipVertex(Vector4 Position, Vector2 Uv, Vector3 Normal);

    private sealed class FrameSetup
    {
        public FrameSetup(Matrix4x4 view, Matrix4x4 viewProjection, int width, int height, RenderOptions options)
        {
            View = view;
            ViewProjection = viewProjection;
            Width = width;
            Height = height;
            CullBackFaces = options.CullBackFaces;
            Sun = Normalized(options.SunDirection) is var sun && sun != Vector3.Zero ? sun : Vector3.UnitZ;
            Half = Normalized(Sun + Vector3.UnitZ) is var half && half != Vector3.Zero ? half : Vector3.UnitZ;
            SunIntensity = options.SunIntensity;
            Ambient = options.Ambient;
            Exposure = float.IsFinite(options.Exposure) && options.Exposure > 0f ? options.Exposure : 1f;
        }

        public Matrix4x4 View { get; }

        public Matrix4x4 ViewProjection { get; }

        public int Width { get; }

        public int Height { get; }

        public bool CullBackFaces { get; }

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
