using System.Numerics;

namespace ToniBreidingerRacing.Core.Racing;

public enum TrackFeatureKind
{
    /// <summary>Zipper boost pad: launches the car past its top speed.</summary>
    Zipper,

    /// <summary>Water puddle: heavy drag while driving through it.</summary>
    Puddle,

    /// <summary>Oil slick: spins the car out.</summary>
    Oil,
}

/// <summary>Scenery palette for a course: grass, desert sand, snow or a floodlit night race.</summary>
public enum TrackTheme
{
    Grass,
    Desert,
    Snow,
    Night,
    Dirt,
}

/// <summary>A fixed track hazard or boost pad, placed by distance along the racing line and lateral offset.</summary>
public sealed record TrackFeature(TrackFeatureKind Kind, float Distance, float Lateral, float Radius);

/// <summary>Where a world position sits relative to the track's centerline.</summary>
public readonly record struct TrackPosition(float Distance, float Lateral, Vector2 Closest, Vector2 Tangent, int Segment)
{
    public Vector2 Normal => new(-Tangent.Y, Tangent.X);
}

/// <summary>
/// A closed circuit built from a smooth Catmull-Rom spline through its control points. The road is
/// <see cref="RoadWidth"/> pixels wide with walls on both sides, like the barrier-lined R.C. Pro-Am courses.
/// </summary>
public sealed class Track
{
    private readonly Vector2[] _points;
    private readonly float[] _cumulative;
    private readonly TrackFeature[] _features;

    public Track(
        string id,
        string name,
        IReadOnlyList<Vector2> controlPoints,
        float roadWidth,
        int laps,
        IReadOnlyList<TrackFeature>? features = null,
        int samplesPerSegment = 10,
        float margin = 96)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(controlPoints);
        if (controlPoints.Count < 4)
        {
            throw new ArgumentException("A track needs at least four control points.", nameof(controlPoints));
        }

        if (roadWidth <= 0 || laps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roadWidth), "Road width and laps must be positive.");
        }

        Id = id;
        Name = name;
        RoadWidth = roadWidth;
        Laps = laps;

        var sampled = SampleClosedSpline(controlPoints, Math.Max(2, samplesPerSegment));
        var min = new Vector2(sampled.Min(p => p.X), sampled.Min(p => p.Y));
        var max = new Vector2(sampled.Max(p => p.X), sampled.Max(p => p.Y));
        var offset = new Vector2(margin + roadWidth / 2, margin + roadWidth / 2) - min;
        _points = sampled.Select(p => p + offset).ToArray();
        WorldWidth = (int)MathF.Ceiling(max.X - min.X + 2 * margin + roadWidth);
        WorldHeight = (int)MathF.Ceiling(max.Y - min.Y + 2 * margin + roadWidth);

        _cumulative = new float[_points.Length + 1];
        for (var i = 0; i < _points.Length; i++)
        {
            _cumulative[i + 1] = _cumulative[i] + Vector2.Distance(_points[i], _points[(i + 1) % _points.Length]);
        }

        Length = _cumulative[^1];
        _features = (features ?? []).ToArray();
    }

    public string Id { get; }

    public TrackTheme Theme { get; init; }

    public string Name { get; }

    public float RoadWidth { get; }

    public float HalfWidth => RoadWidth / 2;

    public int Laps { get; }

    public float Length { get; }

    public int WorldWidth { get; }

    public int WorldHeight { get; }

    /// <summary>Sampled centerline (closed: the last point connects back to the first). Distance 0 is the start/finish line.</summary>
    public IReadOnlyList<Vector2> Points => _points;

    public IReadOnlyList<TrackFeature> Features => _features;

    public float Wrap(float distance)
    {
        var d = distance % Length;
        return d < 0 ? d + Length : d;
    }

    public Vector2 PointAt(float distance)
    {
        var (segment, t) = Locate(distance);
        return Vector2.Lerp(_points[segment], _points[(segment + 1) % _points.Length], t);
    }

    public Vector2 TangentAt(float distance)
    {
        var (segment, _) = Locate(distance);
        return SegmentDirection(segment);
    }

    /// <summary>World position at a distance along the track, shifted sideways by <paramref name="lateral"/> pixels.</summary>
    public Vector2 PositionAt(float distance, float lateral)
    {
        var tangent = TangentAt(distance);
        return PointAt(distance) + new Vector2(-tangent.Y, tangent.X) * lateral;
    }

    public Vector2 FeaturePosition(TrackFeature feature) => PositionAt(feature.Distance, feature.Lateral);

    /// <summary>Projects a world position onto the nearest point of the centerline.</summary>
    public TrackPosition Project(Vector2 position)
    {
        var bestDistanceSquared = float.MaxValue;
        var bestSegment = 0;
        var bestT = 0f;
        for (var i = 0; i < _points.Length; i++)
        {
            var a = _points[i];
            var ab = _points[(i + 1) % _points.Length] - a;
            var lengthSquared = ab.LengthSquared();
            var t = lengthSquared > 0 ? Math.Clamp(Vector2.Dot(position - a, ab) / lengthSquared, 0f, 1f) : 0f;
            var d = Vector2.DistanceSquared(position, a + ab * t);
            if (d < bestDistanceSquared)
            {
                bestDistanceSquared = d;
                bestSegment = i;
                bestT = t;
            }
        }

        var start = _points[bestSegment];
        var end = _points[(bestSegment + 1) % _points.Length];
        var closest = Vector2.Lerp(start, end, bestT);
        var tangent = SegmentDirection(bestSegment);
        var normal = new Vector2(-tangent.Y, tangent.X);
        var along = _cumulative[bestSegment] + Vector2.Distance(start, end) * bestT;
        return new TrackPosition(Wrap(along), Vector2.Dot(position - closest, normal), closest, tangent, bestSegment);
    }

    public bool IsOnRoad(Vector2 position) => MathF.Abs(Project(position).Lateral) <= HalfWidth;

    /// <summary>Turn angle (radians) between the track direction at two distances; used by the AI to brake for corners.</summary>
    public float TurnBetween(float fromDistance, float toDistance)
    {
        var a = TangentAt(fromDistance);
        var b = TangentAt(toDistance);
        return MathF.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b));
    }

    private Vector2 SegmentDirection(int segment)
    {
        var direction = _points[(segment + 1) % _points.Length] - _points[segment];
        return direction.LengthSquared() > 0 ? Vector2.Normalize(direction) : Vector2.UnitX;
    }

    private (int Segment, float T) Locate(float distance)
    {
        var d = Wrap(distance);
        var index = Array.BinarySearch(_cumulative, d);
        if (index < 0)
        {
            index = ~index - 1;
        }

        index = Math.Clamp(index, 0, _points.Length - 1);
        var segmentLength = _cumulative[index + 1] - _cumulative[index];
        var t = segmentLength > 0 ? (d - _cumulative[index]) / segmentLength : 0f;
        return (index, Math.Clamp(t, 0f, 1f));
    }

    private static Vector2[] SampleClosedSpline(IReadOnlyList<Vector2> control, int samplesPerSegment)
    {
        var count = control.Count;
        var result = new Vector2[count * samplesPerSegment];
        for (var i = 0; i < count; i++)
        {
            var p0 = control[(i - 1 + count) % count];
            var p1 = control[i];
            var p2 = control[(i + 1) % count];
            var p3 = control[(i + 2) % count];
            for (var s = 0; s < samplesPerSegment; s++)
            {
                result[i * samplesPerSegment + s] = CatmullRom(p0, p1, p2, p3, s / (float)samplesPerSegment);
            }
        }

        return result;
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }
}
