using System.Numerics;
using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Tests;

public class TrackTests
{
    public static TheoryData<string> TrackIds()
    {
        var data = new TheoryData<string>();
        foreach (var track in TrackLibrary.All)
        {
            data.Add(track.Id);
        }

        return data;
    }

    [Fact]
    public void Library_HasEightUniqueCoursesWithLaps()
    {
        Assert.Equal(8, TrackLibrary.All.Count);
        Assert.Equal(TrackLibrary.All.Count, TrackLibrary.All.Select(t => t.Id).Distinct().Count());
        Assert.All(TrackLibrary.All, t => Assert.InRange(t.Laps, 3, 5));
    }

    [Theory]
    [MemberData(nameof(TrackIds))]
    public void Project_PointOnCenterLine_HasZeroLateralAndMatchingDistance(string id)
    {
        var track = TrackLibrary.Get(id);
        foreach (var fraction in new[] { 0.1f, 0.37f, 0.8f })
        {
            var distance = track.Length * fraction;
            var projected = track.Project(track.PointAt(distance));
            Assert.InRange(MathF.Abs(projected.Lateral), 0, 1.5f);
            Assert.InRange(MathF.Abs(projected.Distance - distance), 0, 3f);
        }
    }

    [Theory]
    [MemberData(nameof(TrackIds))]
    public void Project_OffsetPoint_ReportsLateralOffset(string id)
    {
        var track = TrackLibrary.Get(id);
        var distance = track.Length * 0.5f;
        var position = track.PositionAt(distance, 20);
        var projected = track.Project(position);
        Assert.InRange(projected.Lateral, 18, 22);
        Assert.True(track.IsOnRoad(position));
        Assert.False(track.IsOnRoad(track.PositionAt(distance, track.HalfWidth + 12)));
    }

    [Fact]
    public void Wrap_KeepsDistanceInsideLap()
    {
        var track = TrackLibrary.All[0];
        Assert.Equal(10, track.Wrap(track.Length + 10), 2);
        Assert.Equal(track.Length - 10, track.Wrap(-10), 2);
    }

    [Theory]
    [MemberData(nameof(TrackIds))]
    public void Course_NeverOverlapsItselfAndFitsInWorld(string id)
    {
        var track = TrackLibrary.Get(id);
        const float sample = 6;
        var points = new List<Vector2>();
        for (float d = 0; d < track.Length; d += sample)
        {
            var p = track.PointAt(d);
            Assert.InRange(p.X, track.HalfWidth, track.WorldWidth - track.HalfWidth);
            Assert.InRange(p.Y, track.HalfWidth, track.WorldHeight - track.HalfWidth);
            points.Add(p);
        }

        // Two separate stretches of road must never touch: keep at least two road widths apart.
        var minimumGap = track.RoadWidth * 2;
        var adjacentSamples = (int)(track.RoadWidth * 4 / sample);
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + adjacentSamples; j < points.Count; j++)
            {
                if (points.Count - j + i < adjacentSamples)
                {
                    continue;
                }

                Assert.True(
                    Vector2.Distance(points[i], points[j]) >= minimumGap,
                    $"{id}: road at {i * sample} and {j * sample} is only {Vector2.Distance(points[i], points[j]):F0}px apart.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(TrackIds))]
    public void Features_LieOnTheRoad(string id)
    {
        var track = TrackLibrary.Get(id);
        Assert.NotEmpty(track.Features);
        Assert.All(track.Features, f => Assert.True(track.IsOnRoad(track.FeaturePosition(f)), $"{f.Kind} at {f.Distance} is off road"));
    }
}
