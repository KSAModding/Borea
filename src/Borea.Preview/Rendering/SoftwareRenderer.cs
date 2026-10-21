using System.Numerics;
using Borea.Preview.Scene;

namespace Borea.Preview.Rendering;

/// <summary>
/// Draws a <see cref="PreviewScene"/> into a <see cref="FrameBuffer"/> on the CPU. Each triangle gets one
/// color from the mean of its vertex normals, lit by a sun in camera space plus a constant ambient. The
/// frame is the same byte for byte for any worker count, because each pixel row belongs to exactly one
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

    /// <summary>The linear color of every placeholder, an orange that no stock part has.</summary>
    private static readonly Vector3 PlaceholderColor = new(1f, 0.42f, 0f);

    private static readonly Vector3 GlassTint = new(0.25f, 0.3f, 0.35f);

    /// <summary>The box from -0.5 to 0.5 that each placeholder scales to its size.</summary>
    private static readonly PreviewMesh UnitBox = PreviewTestScenes.CreateBox();

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

        var jobs = new List<DrawJob>(scene.Draws.Count + scene.Placeholders.Count);
        var drawsVisited = 0;
        foreach (var draw in scene.Draws)
        {
            if (IsVisible(draw.Mesh, draw.Transform, planes))
            {
                var color = draw.Material.Glass ? draw.Material.BaseColor * GlassTint : draw.Material.BaseColor;
                jobs.Add(new DrawJob(draw.Mesh, draw.Transform, color, 255));
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
                jobs.Add(new DrawJob(UnitBox, model, PlaceholderColor, PlaceholderAlpha));
                placeholdersVisited++;
            }
        }

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxWorkers),
            CancellationToken = cancellationToken,
        };

        var perJob = new ScreenTriangle[jobs.Count][];
        Parallel.For(0, jobs.Count, parallel, index => perJob[index] = SetupDraw(jobs[index], frame));

        var triangles = perJob.SelectMany(list => list).ToArray();
        var bands = BinIntoBands(triangles, target.Height);

        Parallel.For(0, bands.Length, parallel, band =>
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                FillBand(band, bands[band], triangles, target);
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

    private static ScreenTriangle[] SetupDraw(DrawJob job, FrameSetup frame)
    {
        // A singular transform flattens the mesh to zero area, so nothing of it can be seen.
        if (!Matrix4x4.Invert(job.Model * frame.View, out var modelViewInverse))
        {
            return [];
        }

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

        var output = new List<ScreenTriangle>();
        Span<Vector4> polygon = stackalloc Vector4[MaxPolygon];
        Span<Vector4> scratch = stackalloc Vector4[MaxPolygon];
        Span<int> xs = stackalloc int[MaxPolygon];
        Span<int> ys = stackalloc int[MaxPolygon];
        Span<float> zs = stackalloc float[MaxPolygon];

        var indices = mesh.Indices;
        for (var t = 0; t < indices.Length; t += 3)
        {
            var i0 = (int)indices[t];
            var i1 = (int)indices[t + 1];
            var i2 = (int)indices[t + 2];
            var count = ClipTriangle(clip[i0], clip[i1], clip[i2], polygon, scratch);
            if (count < 3 || !Project(polygon[..count], frame, xs, ys, zs))
            {
                continue;
            }

            // The shoelace sum in screen space, where y points down, is negative for a triangle that is
            // counterclockwise in the y-up view, which is a front face of an unmirrored draw.
            long doubleArea = 0;
            for (var k = 0; k < count; k++)
            {
                var next = k + 1 == count ? 0 : k + 1;
                doubleArea += (long)xs[k] * ys[next] - (long)xs[next] * ys[k];
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

            var color = Shade(mesh, i0, i1, i2, normalMatrix, front, job, frame);
            for (var k = 1; k + 1 < count; k++)
            {
                // The rasterizer wants a positive area, so a negative polygon is walked the other way round.
                var (b, c) = doubleArea > 0 ? (k, k + 1) : (k + 1, k);
                if (TryMakeTriangle(xs, ys, zs, 0, b, c, color, frame, out var triangle))
                {
                    output.Add(triangle);
                }
            }
        }

        return [.. output];
    }

    /// <summary>
    /// Clips a triangle in clip space to the near plane z = 0 and to the guard band, and gives the corner
    /// count of the result in <paramref name="polygon"/>, or 0 when nothing is left. A triangle that lies
    /// fully outside one of the view planes is rejected at once.
    /// </summary>
    private static int ClipTriangle(Vector4 a, Vector4 b, Vector4 c, Span<Vector4> polygon, Span<Vector4> scratch)
    {
        var codeA = OutCode(a);
        var codeB = OutCode(b);
        var codeC = OutCode(c);
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

    /// <summary>One Sutherland-Hodgman step that keeps the side of <paramref name="plane"/> with a distance of 0 or more.</summary>
    private static int ClipAgainst(int plane, ReadOnlySpan<Vector4> input, Span<Vector4> output)
    {
        var count = 0;
        for (var i = 0; i < input.Length; i++)
        {
            var current = input[i];
            var next = input[i + 1 == input.Length ? 0 : i + 1];
            var currentDistance = PlaneDistance(plane, current);
            var nextDistance = PlaneDistance(plane, next);
            if (currentDistance >= 0f)
            {
                output[count++] = current;
            }

            if ((currentDistance >= 0f) != (nextDistance >= 0f))
            {
                var s = currentDistance / (currentDistance - nextDistance);
                output[count++] = current + (next - current) * s;
            }
        }

        return count;
    }

    /// <summary>
    /// Converts clipped corners to screen coordinates in 1/256 pixel with y down, and to the depth of the
    /// projection. It gives false when a corner is not in front of the camera or not finite, which only an
    /// overflow in the transform can cause after the near clip.
    /// </summary>
    private static bool Project(ReadOnlySpan<Vector4> polygon, FrameSetup frame, Span<int> xs, Span<int> ys, Span<float> zs)
    {
        for (var k = 0; k < polygon.Length; k++)
        {
            var v = polygon[k];
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

            xs[k] = (int)Math.Round(x);
            ys[k] = (int)Math.Round(y);
            zs[k] = (float)(v.Z * inverseW);
        }

        return true;
    }

    /// <summary>
    /// The flat color of one triangle. The normal is the mean of its vertex normals, or the geometric normal
    /// when the mesh has none, moved to camera space with the inverse transpose, which also keeps it pointing
    /// out of a mirrored draw. A back face that is drawn is lit as if seen from its front.
    /// </summary>
    private static Rgba8 Shade(PreviewMesh mesh, int i0, int i1, int i2, Matrix4x4 normalMatrix, bool front, DrawJob job, FrameSetup frame)
    {
        var local = Vector3.Zero;
        if (mesh.Normals.Length != 0)
        {
            local = Read3(mesh.Normals, i0) + Read3(mesh.Normals, i1) + Read3(mesh.Normals, i2);
        }

        if (local.LengthSquared() < 1e-12f)
        {
            var p0 = Read3(mesh.Positions, i0);
            local = Vector3.Cross(Read3(mesh.Positions, i1) - p0, Read3(mesh.Positions, i2) - p0);
        }

        var normal = Vector3.TransformNormal(local, normalMatrix);
        var length = normal.Length();
        normal = length > 0f && float.IsFinite(length) ? normal / length : Vector3.UnitZ;
        if (!front)
        {
            normal = -normal;
        }

        var lambert = MathF.Max(0f, Vector3.Dot(normal, frame.Sun));
        var linear = job.Color * (frame.Ambient + frame.SunIntensity * lambert);
        return new Rgba8(ToSrgbByte(linear.X), ToSrgbByte(linear.Y), ToSrgbByte(linear.Z), job.Alpha);
    }

    private static Vector3 Read3(float[] values, int vertex) => new(values[3 * vertex], values[3 * vertex + 1], values[3 * vertex + 2]);

    private static byte ToSrgbByte(float linear)
    {
        var value = Math.Clamp(linear, 0f, 1f);
        var srgb = value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
        return (byte)(srgb * 255f + 0.5f);
    }

    /// <summary>
    /// Builds a screen triangle with a positive edge area and the range of pixels whose centers it can
    /// cover. It gives false when the triangle has no area or covers no pixel center of the frame.
    /// </summary>
    private static bool TryMakeTriangle(Span<int> xs, Span<int> ys, Span<float> zs, int a, int b, int c, Rgba8 color, FrameSetup frame, out ScreenTriangle triangle)
    {
        triangle = default;
        var area = Edge(xs[a], ys[a], xs[b], ys[b], xs[c], ys[c]);
        if (area <= 0)
        {
            return false;
        }

        var minX = Math.Max(0, FirstPixelAtOrAfter(Math.Min(xs[a], Math.Min(xs[b], xs[c]))));
        var maxX = Math.Min(frame.Width - 1, LastPixelAtOrBefore(Math.Max(xs[a], Math.Max(xs[b], xs[c]))));
        var minY = Math.Max(0, FirstPixelAtOrAfter(Math.Min(ys[a], Math.Min(ys[b], ys[c]))));
        var maxY = Math.Min(frame.Height - 1, LastPixelAtOrBefore(Math.Max(ys[a], Math.Max(ys[b], ys[c]))));
        if (minX > maxX || minY > maxY)
        {
            return false;
        }

        triangle = new ScreenTriangle
        {
            X0 = xs[a],
            Y0 = ys[a],
            X1 = xs[b],
            Y1 = ys[b],
            X2 = xs[c],
            Y2 = ys[c],
            Z0 = zs[a],
            Z1 = zs[b],
            Z2 = zs[c],
            InverseArea = 1.0 / area,
            Color = color,
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

    private static void FillBand(int band, int[] triangleIndices, ScreenTriangle[] triangles, FrameBuffer target)
    {
        var top = band * BandHeight;
        var bottom = Math.Min(top + BandHeight, target.Height) - 1;
        var width = target.Width;
        var rgba = target.Rgba;
        var depth = target.Depth;

        foreach (var index in triangleIndices)
        {
            ref readonly var t = ref triangles[index];
            var firstRow = Math.Max(t.MinY, top);
            var lastRow = Math.Min(t.MaxY, bottom);

            // Each edge value is the weight of the vertex opposite to it, so e0 belongs to vertex 0.
            var bias0 = FillBias(t.X1, t.Y1, t.X2, t.Y2);
            var bias1 = FillBias(t.X2, t.Y2, t.X0, t.Y0);
            var bias2 = FillBias(t.X0, t.Y0, t.X1, t.Y1);
            var step0 = -(long)(t.Y2 - t.Y1) * SubPixel;
            var step1 = -(long)(t.Y0 - t.Y2) * SubPixel;
            var step2 = -(long)(t.Y1 - t.Y0) * SubPixel;
            var opaque = t.Color.A == 255;
            var startX = (long)t.MinX * SubPixel + HalfSubPixel;

            for (var y = firstRow; y <= lastRow; y++)
            {
                var py = (long)y * SubPixel + HalfSubPixel;
                var e0 = Edge(t.X1, t.Y1, t.X2, t.Y2, startX, py);
                var e1 = Edge(t.X2, t.Y2, t.X0, t.Y0, startX, py);
                var e2 = Edge(t.X0, t.Y0, t.X1, t.Y1, startX, py);
                var pixel = y * width + t.MinX;

                for (var x = t.MinX; x <= t.MaxX; x++, pixel++, e0 += step0, e1 += step1, e2 += step2)
                {
                    // The sign bit of the OR is set when any of the three biased edge values is negative.
                    if (((e0 + bias0) | (e1 + bias1) | (e2 + bias2)) < 0)
                    {
                        continue;
                    }

                    var z = (float)((e0 * (double)t.Z0 + e1 * (double)t.Z1 + e2 * (double)t.Z2) * t.InverseArea);
                    if (!(z < depth[pixel]))
                    {
                        continue;
                    }

                    if (opaque)
                    {
                        depth[pixel] = z;
                        Write(rgba, pixel, t.Color);
                    }
                    else
                    {
                        Blend(rgba, pixel, t.Color);
                    }
                }
            }
        }
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

    private readonly record struct DrawJob(PreviewMesh Mesh, Matrix4x4 Model, Vector3 Color, byte Alpha);

    private sealed class FrameSetup(Matrix4x4 view, Matrix4x4 viewProjection, int width, int height, RenderOptions options)
    {
        public Matrix4x4 View { get; } = view;

        public Matrix4x4 ViewProjection { get; } = viewProjection;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public bool CullBackFaces { get; } = options.CullBackFaces;

        public Vector3 Sun { get; } = ValidSun(options.SunDirection);

        public float SunIntensity { get; } = options.SunIntensity;

        public float Ambient { get; } = options.Ambient;

        private static Vector3 ValidSun(Vector3 direction)
        {
            var length = direction.Length();
            return length > 0f && float.IsFinite(length) ? direction / length : Vector3.UnitZ;
        }
    }

    /// <summary>A triangle in 1/256 pixel with y down and a positive edge area, plus its pixel range in the frame.</summary>
    private struct ScreenTriangle
    {
        public int X0;
        public int Y0;
        public int X1;
        public int Y1;
        public int X2;
        public int Y2;
        public float Z0;
        public float Z1;
        public float Z2;
        public double InverseArea;
        public Rgba8 Color;
        public int MinX;
        public int MaxX;
        public int MinY;
        public int MaxY;
    }
}
