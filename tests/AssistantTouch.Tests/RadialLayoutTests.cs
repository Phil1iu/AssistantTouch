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

    [Theory]
    [InlineData(0, 0, -1920, -51)]
    [InlineData(0, 0, 1920, 1080)]
    [InlineData(0, 540, -1920, -51)]
    [InlineData(0, 540, 1920, 1080)]
    public void Compute_TranslatedWorkArea_TranslatesCenters(
        double ballX, double ballY, double offsetX, double offsetY)
    {
        var baselineBall = new Point(ballX, ballY);
        var translatedBall = new Point(ballX + offsetX, ballY + offsetY);
        var translatedArea = new Rect(
            FullHd.X + offsetX,
            FullHd.Y + offsetY,
            FullHd.Width,
            FullHd.Height);

        RadialLayoutResult baseline = RadialLayout.Compute(baselineBall, FullHd, 9);
        RadialLayoutResult translated = RadialLayout.Compute(translatedBall, translatedArea, 9);

        Assert.Equal(baseline.Mode, translated.Mode);
        Assert.Equal(baseline.AllPlaced, translated.AllPlaced);
        Assert.Equal(baseline.Centers.Count, translated.Centers.Count);

        for (int i = 0; i < baseline.Centers.Count; i++)
        {
            var expected = new Point(
                baseline.Centers[i].X + offsetX,
                baseline.Centers[i].Y + offsetY);

            Assert.True(
                (expected - translated.Centers[i]).Length < 1e-9,
                $"slot {i}: expected {expected}, got {translated.Centers[i]}");
        }

        AssertValid(translated.Centers, translatedBall, translatedArea);
    }

    [Theory]
    [InlineData(-1, 540)]
    [InlineData(1921, 540)]
    [InlineData(960, -1)]
    [InlineData(960, 1081)]
    [InlineData(-1, -1)]
    [InlineData(1921, 1081)]
    public void Compute_BallJustOutsideWorkArea_StillPlacesAllItems(double x, double y)
    {
        var ballCenter = new Point(x, y);

        RadialLayoutResult result = RadialLayout.Compute(ballCenter, FullHd, 9);

        Assert.True(result.AllPlaced, $"mode={result.Mode} diag={result.Diagnostic}");
        Assert.Equal(9, result.Centers.Count);
        AssertValid(result.Centers, ballCenter, FullHd);
    }

    [Fact]
    public void Compute_TopLeftCornerNineItems_UsesDoubleArc()
    {
        var ballCenter = new Point(FullHd.Left, FullHd.Top);

        RadialLayoutResult result = RadialLayout.Compute(ballCenter, FullHd, 9);

        Assert.True(result.AllPlaced);
        Assert.Equal(RadialLayoutMode.DoubleArc, result.Mode);
        Assert.Equal(9, result.Centers.Count);
        AssertValid(result.Centers, ballCenter, FullHd);
    }

    [Fact]
    public void Compute_FarOutsideBall_NeedsExpandedRadius()
    {
        // 球在工作区外 300px：标准半径 95..200 放不下 9 项，需放大到工作区允许的最大半径后双弧布局。
        var area = new Rect(0, 0, 400, 400);
        var ballCenter = new Point(-300, 200);

        RadialLayoutResult result = RadialLayout.Compute(ballCenter, area, 9);

        Assert.True(result.AllPlaced, $"mode={result.Mode} diag={result.Diagnostic}");
        Assert.Equal(RadialLayoutMode.Expanded, result.Mode);
        Assert.Equal(9, result.Centers.Count);
        AssertValid(result.Centers, ballCenter, area);
    }

    [Fact]
    public void Compute_SmallWorkArea_ReturnsPartialWithValidCenters()
    {
        var area = new Rect(0, 0, 180, 180);
        var ballCenter = new Point(90, 90);

        RadialLayoutResult result = RadialLayout.Compute(ballCenter, area, 9);

        Assert.False(result.AllPlaced);
        Assert.Equal(RadialLayoutMode.Partial, result.Mode);
        Assert.NotEmpty(result.Centers);
        AssertValid(result.Centers, ballCenter, area);
    }

    [Theory]
    [InlineData(40, 20)]
    [InlineData(80, 40)]
    public void Compute_WorkAreaSmallerThanCircle_ReturnsEmptyPartial(double size, double expectedBall)
    {
        var area = new Rect(0, 0, size, size);
        var ballCenter = new Point(expectedBall, expectedBall);

        RadialLayoutResult result = RadialLayout.Compute(ballCenter, area, 9);

        Assert.False(result.AllPlaced);
        Assert.Equal(RadialLayoutMode.Partial, result.Mode);
        Assert.Empty(result.Centers);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
    }

    [Fact]
    public void Compute_SingleItem_IsPlacedOrDiagnosed()
    {
        var area = new Rect(0, 0, 180, 180);
        var ballCenter = new Point(90, 90);

        RadialLayoutResult result = RadialLayout.Compute(ballCenter, area, 1);

        Assert.Single(result.Centers);
        AssertValid(result.Centers, ballCenter, area);
        Assert.True(result.AllPlaced, result.Diagnostic);
        Assert.NotEqual(RadialLayoutMode.Partial, result.Mode);
    }

    [Fact]
    public void ChooseFallbackCenter_PrefersFirstValidExistingCenter()
    {
        var area = new Rect(0, 0, 180, 180);
        var ballCenter = new Point(90, 90);
        RadialLayoutResult layout = RadialLayout.Compute(ballCenter, area, 9);
        Assert.NotEmpty(layout.Centers);

        Point? result = RadialLayout.ChooseFallbackCenter(
            ballCenter, area, CircleSize, MinBallDistance, layout.Centers, out string diagnostic);

        Assert.True(result.HasValue);
        Assert.Equal(layout.Centers[0], result!.Value);
        Assert.StartsWith("preferred", diagnostic);
        AssertFallbackValid(result.Value, ballCenter, area);
    }

    [Fact]
    public void ChooseFallbackCenter_InvalidPreferred_SearchesDiagonals()
    {
        var area = new Rect(0, 0, 180, 180);
        var ballCenter = new Point(90, 90);
        var invalidPreferred = new[] { ballCenter };

        Point? result = RadialLayout.ChooseFallbackCenter(
            ballCenter, area, CircleSize, MinBallDistance, invalidPreferred, out string diagnostic);

        Assert.True(result.HasValue, diagnostic);
        Assert.NotEqual(ballCenter, result!.Value);
        AssertFallbackValid(result.Value, ballCenter, area);
    }

    [Theory]
    [InlineData(40, 20)]
    [InlineData(80, 40)]
    public void ChooseFallbackCenter_NoRoom_ReturnsNull(double size, double expectedBall)
    {
        var area = new Rect(0, 0, size, size);
        var ballCenter = new Point(expectedBall, expectedBall);

        Point? result = RadialLayout.ChooseFallbackCenter(
            ballCenter, area, CircleSize, MinBallDistance, null, out string diagnostic);

        Assert.Null(result);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }

    private static void AssertFallbackValid(Point point, Point ballCenter, Rect workArea)
    {
        Assert.InRange(
            point.X,
            workArea.Left + Half - Tolerance,
            workArea.Right - Half + Tolerance);
        Assert.InRange(
            point.Y,
            workArea.Top + Half - Tolerance,
            workArea.Bottom - Half + Tolerance);
        Assert.True(
            (point - ballCenter).Length >= MinBallDistance - Tolerance,
            $"fallback {point} is too close to ball {ballCenter}");
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
