using System.Windows;
using Xunit;

namespace AssistantTouch.Tests;

/// <summary>
/// 径向布局纯函数（<see cref="RadialLayout.Compute"/>）的几何测试：
/// 数量齐全、整圆在工作区内、两两中心距 ≥ 直径+gap、距球心 ≥ minBallDistance、无重复点。
/// </summary>
public class RadialLayoutTests
{
    private const double CircleSize = RadialLayout.DefaultCircleSize; // 44
    private const double Half = CircleSize / 2;
    private const double MinBallDistance = RadialLayout.DefaultMinBallDistance; // 70
    private const double MinSpacing = CircleSize + RadialLayout.DefaultGap; // 48
    private const double Tolerance = 1e-6;

    private static readonly Rect FullHd = new(0, 0, 1920, 1080);
    private static readonly Rect Laptop = new(0, 0, 1366, 768);

    public static IEnumerable<object[]> EdgeAndCornerCases()
    {
        foreach (Rect area in new[] { FullHd, Laptop })
        {
            foreach (double distance in new[] { 0.0, 10.0, 36.0 })
            {
                double cx = area.Left + area.Width / 2;
                double cy = area.Top + area.Height / 2;

                yield return new object[] { area, new Point(area.Left + distance, cy) };
                yield return new object[] { area, new Point(area.Right - distance, cy) };
                yield return new object[] { area, new Point(cx, area.Top + distance) };
                yield return new object[] { area, new Point(cx, area.Bottom - distance) };
                yield return new object[] { area, new Point(area.Left + distance, area.Top + distance) };
                yield return new object[] { area, new Point(area.Right - distance, area.Top + distance) };
                yield return new object[] { area, new Point(area.Left + distance, area.Bottom - distance) };
                yield return new object[] { area, new Point(area.Right - distance, area.Bottom - distance) };
            }
        }
    }

    [Theory]
    [MemberData(nameof(EdgeAndCornerCases))]
    public void Compute_EdgeAndCorner_AllItemsPlacedAndValid(Rect workArea, Point ballCenter)
    {
        for (int count = 1; count <= 9; count++)
        {
            RadialLayoutResult result = RadialLayout.Compute(ballCenter, workArea, count);

            Assert.True(
                result.AllPlaced,
                $"count={count} ball=({ballCenter.X:0.#},{ballCenter.Y:0.#}) mode={result.Mode} diag={result.Diagnostic}");
            Assert.Equal(count, result.Centers.Count);
            AssertValid(result.Centers, ballCenter, workArea);
        }
    }

    [Fact]
    public void Compute_BallCentered_MatchesLegacyFullRing()
    {
        var ballCenter = new Point(FullHd.Width / 2, FullHd.Height / 2);

        RadialLayoutResult result = RadialLayout.Compute(ballCenter, FullHd, 9);

        Assert.True(result.AllPlaced);
        Assert.Equal(RadialLayoutMode.FullRing, result.Mode);
        Assert.Equal(9, result.Centers.Count);

        for (int i = 0; i < 9; i++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * i / 9;
            var expected = new Point(
                ballCenter.X + RadialLayout.DefaultRadius * Math.Cos(angle),
                ballCenter.Y + RadialLayout.DefaultRadius * Math.Sin(angle));

            Assert.True(
                (expected - result.Centers[i]).Length < 1e-9,
                $"slot {i}: expected {expected}, got {result.Centers[i]}");
        }
    }

    [Fact]
    public void Compute_BallNearLeftEdge_UsesArcMode()
    {
        var ballCenter = new Point(FullHd.Left, FullHd.Height / 2);

        RadialLayoutResult result = RadialLayout.Compute(ballCenter, FullHd, 9);

        Assert.True(result.AllPlaced);
        Assert.NotEqual(RadialLayoutMode.FullRing, result.Mode);
        AssertValid(result.Centers, ballCenter, FullHd);
    }

    [Fact]
    public void Compute_RandomBallPositions_AllItemsPlacedAndValid()
    {
        var random = new Random(20261112);

        foreach (Rect area in new[] { FullHd, Laptop })
        {
            for (int sample = 0; sample < 200; sample++)
            {
                var ballCenter = new Point(
                    area.Left + random.NextDouble() * area.Width,
                    area.Top + random.NextDouble() * area.Height);

                foreach (int count in new[] { 6, 9 })
                {
                    RadialLayoutResult result = RadialLayout.Compute(ballCenter, area, count);

                    Assert.True(
                        result.AllPlaced,
                        $"count={count} ball=({ballCenter.X:0.#},{ballCenter.Y:0.#}) " +
                        $"mode={result.Mode} diag={result.Diagnostic}");
                    Assert.Equal(count, result.Centers.Count);
                    AssertValid(result.Centers, ballCenter, area);
                }
            }
        }
    }

    [Fact]
    public void Compute_TinyWorkArea_ReturnsDiagnosticWithoutThrowing()
    {
        var tiny = new Rect(0, 0, 80, 80);
        var ballCenter = new Point(40, 40);

        RadialLayoutResult result = RadialLayout.Compute(ballCenter, tiny, 9);

        Assert.False(result.AllPlaced);
        Assert.Equal(RadialLayoutMode.Partial, result.Mode);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
        Assert.All(result.Centers, point =>
        {
            Assert.InRange(point.X, tiny.Left + Half - Tolerance, tiny.Right - Half + Tolerance);
            Assert.InRange(point.Y, tiny.Top + Half - Tolerance, tiny.Bottom - Half + Tolerance);
        });
    }

    private static void AssertValid(IReadOnlyList<Point> centers, Point ballCenter, Rect workArea)
    {
        for (int i = 0; i < centers.Count; i++)
        {
            Point point = centers[i];

            Assert.True(
                point.X >= workArea.Left + Half - Tolerance &&
                point.X <= workArea.Right - Half + Tolerance &&
                point.Y >= workArea.Top + Half - Tolerance &&
                point.Y <= workArea.Bottom - Half + Tolerance,
                $"slot {i} {point} is not fully inside {workArea}");

            Assert.True(
                (point - ballCenter).Length >= MinBallDistance - Tolerance,
                $"slot {i} {point} is too close to ball {ballCenter}");

            for (int j = 0; j < i; j++)
            {
                Assert.True(
                    (point - centers[j]).Length >= MinSpacing - Tolerance,
                    $"slots {j} and {i} are too close: {centers[j]} / {point}");
            }
        }
    }
}
