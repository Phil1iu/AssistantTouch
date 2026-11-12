using System.Windows;

namespace AssistantTouch;

/// <summary>径向菜单的布局模式，用于测试与运行时诊断。</summary>
internal enum RadialLayoutMode
{
    /// <summary>默认整圆：所有槽位在当前工作区内，外观与旧实现完全一致。</summary>
    FullRing,

    /// <summary>球靠近边缘/角落时，N 个槽位均匀铺在一段可见扇形的单弧上。</summary>
    SingleArc,

    /// <summary>单弧放不下时，使用内外两条弧线交错排布。</summary>
    DoubleArc,

    /// <summary>标准半径（≤200）放不下，放大半径后仍放下了全部槽位。</summary>
    Expanded,

    /// <summary>几何上放不下全部槽位：只返回能放下的部分，调用方须自行兜底。</summary>
    Partial
}

internal sealed class RadialLayoutResult
{
    public RadialLayoutResult(
        IReadOnlyList<Point> centers,
        RadialLayoutMode mode,
        bool allPlaced,
        string diagnostic)
    {
        Centers = centers;
        Mode = mode;
        AllPlaced = allPlaced;
        Diagnostic = diagnostic;
    }

    /// <summary>按快捷项顺序排列的槽位中心（最后一个是「+」）。</summary>
    public IReadOnlyList<Point> Centers { get; }

    public RadialLayoutMode Mode { get; }

    /// <summary>是否放下了全部槽位。</summary>
    public bool AllPlaced { get; }

    /// <summary>模式说明（半径、弧长、拆分数量）或失败原因，便于测试与日志。</summary>
    public string Diagnostic { get; }
}

/// <summary>
/// 径向菜单的纯几何布局：把 N 个小圆排在大球周围。
/// 约束：小圆整体落在工作区内、两两中心距 ≥ 直径 + gap、距球心 ≥ minBallDistance。
/// 球在工作区中部时保持旧的整圆（半径 <see cref="DefaultRadius"/>、12 点方向顺时针）；
/// 球贴边/贴角时按可用扇区收拢为单弧或内外双弧，必要时放大半径。
/// </summary>
internal static class RadialLayout
{
    public const double DefaultCircleSize = 44;
    public const double DefaultRadius = 95;
    public const double DefaultMinBallDistance = 70;
    public const double DefaultGap = 4;

    /// <summary>标准自适应半径上限（D41）；超过后进入 Expanded 模式。</summary>
    public const double MaxStandardRadius = 200;

    private const double RadiusStep = 2.5;
    private const double ExpandedRadiusStep = 10;
    private const double TwoPi = Math.PI * 2;
    private const double Tolerance = 1e-6;
    private const double AngleTolerance = 1e-9;

    public static RadialLayoutResult Compute(
        Point ballCenter,
        Rect workArea,
        int itemCount,
        double circleSize = DefaultCircleSize,
        double baseRadius = DefaultRadius,
        double minBallDistance = DefaultMinBallDistance,
        double gap = DefaultGap)
    {
        if (itemCount <= 0)
        {
            return new RadialLayoutResult(Array.Empty<Point>(), RadialLayoutMode.FullRing, true, "no-items");
        }

        double half = circleSize / 2;
        double minSpacing = circleSize + gap;

        double left = workArea.Left + half;
        double top = workArea.Top + half;
        double right = workArea.Right - half;
        double bottom = workArea.Bottom - half;

        if (right < left || bottom < top)
        {
            return new RadialLayoutResult(
                Array.Empty<Point>(),
                RadialLayoutMode.Partial,
                false,
                $"work-area-too-small: {workArea.Width:0.#}x{workArea.Height:0.#} for {circleSize:0.#}px circles");
        }

        var centerArea = new Rect(left, top, right - left, bottom - top);

        // 1) 默认整圆：球在屏幕中部时与旧实现逐点一致。
        Point[] ring = BuildFullRing(ballCenter, itemCount, baseRadius);
        if (IsLayoutValid(ring, ballCenter, centerArea, minBallDistance, minSpacing))
        {
            return new RadialLayoutResult(ring, RadialLayoutMode.FullRing, true, $"full-ring r={baseRadius:0.#}");
        }

        // 2) 标准半径范围内先试单弧，再试双弧。
        double maxStandardRadius = Math.Max(baseRadius, MaxStandardRadius);

        if (TryFindSingleArc(ballCenter, itemCount, centerArea, minBallDistance, minSpacing,
                baseRadius, maxStandardRadius, RadiusStep, out Point[] single, out string singleInfo))
        {
            return new RadialLayoutResult(single, RadialLayoutMode.SingleArc, true, singleInfo);
        }

        if (TryFindDoubleArc(ballCenter, itemCount, centerArea, minBallDistance, minSpacing,
                baseRadius, maxStandardRadius, RadiusStep, out Point[] doubleArc, out string doubleInfo))
        {
            return new RadialLayoutResult(doubleArc, RadialLayoutMode.DoubleArc, true, doubleInfo);
        }

        // 3) 标准半径仍放不下：放大到工作区允许的最大半径再试一遍。
        double maxRadius = GetMaxRadius(ballCenter, centerArea);
        if (maxRadius > maxStandardRadius + Tolerance)
        {
            if (TryFindSingleArc(ballCenter, itemCount, centerArea, minBallDistance, minSpacing,
                    maxStandardRadius, maxRadius, ExpandedRadiusStep, out single, out singleInfo))
            {
                return new RadialLayoutResult(single, RadialLayoutMode.Expanded, true, singleInfo);
            }

            if (TryFindDoubleArc(ballCenter, itemCount, centerArea, minBallDistance, minSpacing,
                    baseRadius, maxRadius, ExpandedRadiusStep, out doubleArc, out doubleInfo))
            {
                return new RadialLayoutResult(doubleArc, RadialLayoutMode.Expanded, true, doubleInfo);
            }
        }

        // 4) 确实放不下：返回能放下的最多个数（沿最大可用弧），并注明原因。
        return BuildPartial(ballCenter, itemCount, centerArea, minBallDistance, minSpacing, baseRadius, maxRadius);
    }

    private static Point[] BuildFullRing(Point center, int count, double radius)
    {
        var points = new Point[count];
        for (int i = 0; i < count; i++)
        {
            double angle = -Math.PI / 2 + TwoPi * i / count;
            points[i] = new Point(
                center.X + radius * Math.Cos(angle),
                center.Y + radius * Math.Sin(angle));
        }

        return points;
    }

    private static bool TryFindSingleArc(
        Point ballCenter,
        int count,
        Rect centerArea,
        double minBallDistance,
        double minSpacing,
        double fromRadius,
        double toRadius,
        double step,
        out Point[] points,
        out string diagnostic)
    {
        points = Array.Empty<Point>();
        diagnostic = string.Empty;

        foreach (double radius in Radii(fromRadius, toRadius, step))
        {
            if (radius < minBallDistance - Tolerance)
            {
                continue;
            }

            Arc arc = GetLongestArc(ballCenter, centerArea, radius);
            if (arc.Length <= AngleTolerance)
            {
                continue;
            }

            double minStep = MinAngularStep(radius, minSpacing);
            if (count > 1 && (count - 1) * minStep > arc.Length + 1e-7)
            {
                continue;
            }

            Point[] candidate = DistributeOnArc(ballCenter, radius, arc, count)
                .Select(entry => entry.Point)
                .ToArray();

            if (!IsLayoutValid(candidate, ballCenter, centerArea, minBallDistance, minSpacing))
            {
                continue;
            }

            points = candidate;
            diagnostic = $"single-arc r={radius:0.#} span={RadiansToDegrees(arc.Length):0.#}°";
            return true;
        }

        return false;
    }

    private static bool TryFindDoubleArc(
        Point ballCenter,
        int count,
        Rect centerArea,
        double minBallDistance,
        double minSpacing,
        double fromRadius,
        double toRadius,
        double step,
        out Point[] points,
        out string diagnostic)
    {
        points = Array.Empty<Point>();
        diagnostic = string.Empty;

        if (count < 2)
        {
            return false;
        }

        double minInnerRadius = Math.Max(fromRadius, minBallDistance);

        // 两弧半径差至少为 minSpacing：这样跨弧任意两点的距离都 ≥ 半径差，天然不重叠。
        double minRadialGap = minSpacing;

        foreach (double innerRadius in Radii(minInnerRadius, toRadius - minRadialGap, step))
        {
            Arc innerArc = GetLongestArc(ballCenter, centerArea, innerRadius);
            int innerCapacity = GetArcCapacity(innerArc, innerRadius, minSpacing);
            if (innerCapacity < 1)
            {
                continue;
            }

            foreach (double outerRadius in Radii(innerRadius + minRadialGap, toRadius, step))
            {
                Arc outerArc = GetLongestArc(ballCenter, centerArea, outerRadius);
                int outerCapacity = GetArcCapacity(outerArc, outerRadius, minSpacing);
                if (outerCapacity < 1 || innerCapacity + outerCapacity < count)
                {
                    continue;
                }

                if (!TrySplitCounts(count, innerCapacity, outerCapacity, out int innerCount))
                {
                    continue;
                }

                List<(Point Point, double T)> inner = DistributeOnArc(ballCenter, innerRadius, innerArc, innerCount);
                List<(Point Point, double T)> outer = DistributeOnArc(ballCenter, outerRadius, outerArc, count - innerCount);

                // 两弧使用同一段可见扇区，按归一化角度参数交错，保持环绕顺序；
                // 半径差 ≥ minSpacing 保证跨弧距离，无需再做跨弧角度检查。
                Point[] candidate = inner
                    .Concat(outer)
                    .OrderBy(entry => entry.T)
                    .Select(entry => entry.Point)
                    .ToArray();

                if (!IsLayoutValid(candidate, ballCenter, centerArea, minBallDistance, minSpacing))
                {
                    continue;
                }

                points = candidate;
                diagnostic =
                    $"double-arc r={innerRadius:0.#}/{outerRadius:0.#} split={innerCount}+{count - innerCount} " +
                    $"span={RadiansToDegrees(innerArc.Length):0.#}°/{RadiansToDegrees(outerArc.Length):0.#}°";
                return true;
            }
        }

        return false;
    }

    /// <summary>在内外两条弧的容量内选择尽量均衡的拆分（内弧数量优先取 N/2 附近）。</summary>
    private static bool TrySplitCounts(int count, int innerCapacity, int outerCapacity, out int innerCount)
    {
        innerCount = 0;
        double bestDistance = double.MaxValue;

        int minInner = Math.Max(1, count - outerCapacity);
        int maxInner = Math.Min(innerCapacity, count - 1);
        if (minInner > maxInner)
        {
            return false;
        }

        for (int candidate = minInner; candidate <= maxInner; candidate++)
        {
            double distance = Math.Abs(candidate - count / 2.0);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                innerCount = candidate;
            }
        }

        return innerCount >= 1;
    }

    /// <summary>沿弧均匀放置 count 个点，并返回 0..1 的角度进度（用于内外弧交错排序）。</summary>
    private static List<(Point Point, double T)> DistributeOnArc(Point ballCenter, double radius, Arc arc, int count)
    {
        var result = new List<(Point, double)>(count);

        for (int i = 0; i < count; i++)
        {
            double t = count == 1 ? 0.5 : (double)i / (count - 1);
            double angle = arc.Start + arc.Length * t;
            result.Add((
                new Point(
                    ballCenter.X + radius * Math.Cos(angle),
                    ballCenter.Y + radius * Math.Sin(angle)),
                t));
        }

        return result;
    }

    private static RadialLayoutResult BuildPartial(
        Point ballCenter,
        int count,
        Rect centerArea,
        double minBallDistance,
        double minSpacing,
        double baseRadius,
        double maxRadius)
    {
        Point[] best = Array.Empty<Point>();
        double bestRadius = 0;
        int bestCapacity = 0;

        foreach (double radius in Radii(Math.Max(baseRadius, minBallDistance), maxRadius, ExpandedRadiusStep))
        {
            Arc arc = GetLongestArc(ballCenter, centerArea, radius);
            int capacity = GetArcCapacity(arc, radius, minSpacing);
            if (capacity > bestCapacity)
            {
                bestCapacity = capacity;
                bestRadius = radius;
                best = DistributeOnArc(ballCenter, radius, arc, Math.Min(count, capacity))
                    .Select(entry => entry.Point)
                    .ToArray();
            }
        }

        if (bestCapacity == 0)
        {
            return new RadialLayoutResult(
                Array.Empty<Point>(),
                RadialLayoutMode.Partial,
                false,
                $"no-valid-arc: ball=({ballCenter.X:0.#},{ballCenter.Y:0.#}) " +
                $"area={centerArea.Width:0.#}x{centerArea.Height:0.#}");
        }

        return new RadialLayoutResult(
            best,
            RadialLayoutMode.Partial,
            false,
            $"partial: placed {best.Length}/{count} at r={bestRadius:0.#}");
    }

    private static bool IsLayoutValid(
        IReadOnlyList<Point> points,
        Point ballCenter,
        Rect centerArea,
        double minBallDistance,
        double minSpacing)
    {
        for (int i = 0; i < points.Count; i++)
        {
            Point point = points[i];

            if (point.X < centerArea.Left - Tolerance ||
                point.X > centerArea.Right + Tolerance ||
                point.Y < centerArea.Top - Tolerance ||
                point.Y > centerArea.Bottom + Tolerance)
            {
                return false;
            }

            if ((point - ballCenter).Length < minBallDistance - Tolerance)
            {
                return false;
            }

            for (int j = 0; j < i; j++)
            {
                if ((point - points[j]).Length < minSpacing - Tolerance)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static IEnumerable<double> Radii(double from, double to, double step)
    {
        if (to < from - Tolerance)
        {
            yield break;
        }

        for (double radius = from; radius <= to + Tolerance; radius += step)
        {
            yield return Math.Round(radius, 6);
        }
    }

    private static double MinAngularStep(double radius, double minSpacing)
    {
        // 弦长 = 2r·sin(Δ/2) ≥ minSpacing。
        return 2 * Math.Asin(Math.Min(1, minSpacing / (2 * radius)));
    }

    private static int GetArcCapacity(Arc arc, double radius, double minSpacing)
    {
        if (arc.Length <= AngleTolerance)
        {
            return 0;
        }

        double step = MinAngularStep(radius, minSpacing);
        if (step <= AngleTolerance)
        {
            return 0;
        }

        return (int)Math.Floor(arc.Length / step + 1e-7) + 1;
    }

    private static Arc GetLongestArc(Point ballCenter, Rect centerArea, double radius)
    {
        Arc best = default;
        foreach (Arc arc in GetValidArcs(ballCenter, centerArea, radius))
        {
            if (arc.Length > best.Length)
            {
                best = arc;
            }
        }

        return best;
    }

    /// <summary>半径为 radius 的圆上，落在 centerArea 内的方向区间（0..2π 角度）。</summary>
    private static List<Arc> GetValidArcs(Point center, Rect area, double radius)
    {
        var segments = new List<Segment> { new(0, TwoPi) };

        segments = IntersectCos(segments, (area.Left - center.X) / radius, lowerBound: true);
        segments = IntersectCos(segments, (area.Right - center.X) / radius, lowerBound: false);
        segments = IntersectSin(segments, (area.Top - center.Y) / radius, lowerBound: true);
        segments = IntersectSin(segments, (area.Bottom - center.Y) / radius, lowerBound: false);

        return SegmentsToArcs(segments);
    }

    private static List<Segment> IntersectCos(List<Segment> segments, double value, bool lowerBound)
    {
        if (lowerBound)
        {
            // cos θ ≥ value
            if (value <= -1)
            {
                return segments;
            }

            if (value > 1)
            {
                return new List<Segment>();
            }

            double half = Math.Acos(Math.Clamp(value, -1, 1));
            return IntersectSegments(segments, ToSegments(new Arc(NormalizeAngle(TwoPi - half), 2 * half)));
        }
        else
        {
            // cos θ ≤ value
            if (value >= 1)
            {
                return segments;
            }

            if (value < -1)
            {
                return new List<Segment>();
            }

            double half = Math.Acos(Math.Clamp(value, -1, 1));
            return IntersectSegments(segments, ToSegments(new Arc(half, TwoPi - 2 * half)));
        }
    }

    private static List<Segment> IntersectSin(List<Segment> segments, double value, bool lowerBound)
    {
        if (lowerBound)
        {
            // sin θ ≥ value
            if (value <= -1)
            {
                return segments;
            }

            if (value > 1)
            {
                return new List<Segment>();
            }

            double edge = Math.Asin(Math.Clamp(value, -1, 1));
            return IntersectSegments(segments, ToSegments(new Arc(NormalizeAngle(edge), Math.PI - 2 * edge)));
        }
        else
        {
            // sin θ ≤ value
            if (value >= 1)
            {
                return segments;
            }

            if (value < -1)
            {
                return new List<Segment>();
            }

            double edge = Math.Asin(Math.Clamp(value, -1, 1));
            return IntersectSegments(segments, ToSegments(new Arc(NormalizeAngle(Math.PI - edge), Math.PI + 2 * edge)));
        }
    }

    private static List<Segment> IntersectSegments(List<Segment> left, List<Segment> right)
    {
        var result = new List<Segment>();

        foreach (Segment a in left)
        {
            foreach (Segment b in right)
            {
                double start = Math.Max(a.Start, b.Start);
                double end = Math.Min(a.End, b.End);
                if (end - start > 1e-12)
                {
                    result.Add(new Segment(start, end));
                }
            }
        }

        return MergeSegments(result);
    }

    private static List<Segment> MergeSegments(List<Segment> segments)
    {
        if (segments.Count <= 1)
        {
            return segments;
        }

        List<Segment> sorted = segments.OrderBy(segment => segment.Start).ToList();
        var merged = new List<Segment> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            Segment current = sorted[i];
            Segment previous = merged[^1];

            if (current.Start <= previous.End + AngleTolerance)
            {
                merged[^1] = new Segment(previous.Start, Math.Max(previous.End, current.End));
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }

    private static List<Segment> ToSegments(Arc arc)
    {
        double start = NormalizeAngle(arc.Start);
        double end = start + arc.Length;

        if (end <= TwoPi + AngleTolerance)
        {
            return new List<Segment> { new(start, Math.Min(end, TwoPi)) };
        }

        return new List<Segment>
        {
            new(start, TwoPi),
            new(0, end - TwoPi)
        };
    }

    private static List<Arc> SegmentsToArcs(List<Segment> segments)
    {
        var arcs = new List<Arc>();

        foreach (Segment segment in MergeSegments(segments))
        {
            arcs.Add(new Arc(segment.Start, segment.End - segment.Start));
        }

        // 若可用区间跨越 0/2π，把首尾合成一个弧，方便沿一段连续扇区均匀铺点。
        if (arcs.Count >= 2)
        {
            Arc first = arcs[0];
            Arc last = arcs[^1];

            if (first.Start <= AngleTolerance &&
                Math.Abs(last.Start + last.Length - TwoPi) <= 1e-7)
            {
                arcs.RemoveAt(arcs.Count - 1);
                arcs.RemoveAt(0);
                arcs.Add(new Arc(last.Start, last.Length + first.Length));
            }
        }

        return arcs;
    }

    private static double NormalizeAngle(double angle)
    {
        double normalized = angle % TwoPi;
        if (normalized < 0)
        {
            normalized += TwoPi;
        }

        return normalized;
    }

    private static double RadiansToDegrees(double radians) => radians * 180 / Math.PI;

    private static double GetMaxRadius(Point center, Rect area)
    {
        double max = 0;
        foreach (Point corner in new[]
                 {
                     new Point(area.Left, area.Top),
                     new Point(area.Right, area.Top),
                     new Point(area.Left, area.Bottom),
                     new Point(area.Right, area.Bottom)
                 })
        {
            max = Math.Max(max, (corner - center).Length);
        }

        return max;
    }

    /// <summary>
    /// <see cref="RadialLayoutMode.Partial"/> 时「+」的兜底选址（rev-10 P2）。
    /// 顺序：① preferred 中第一个「整圆在工作区内且距球心 ≥ minBallDistance」的点；
    /// ② 8 个罗盘方向 + 4 个朝向可用区角点的方向，以 70→可用区对角长的递增半径搜索；
    /// ③ 可用区内离球心最远的角点；若最远距离仍 &lt; minBallDistance 则返回 null（UI 不画「+」）。
    /// 任何成功返回的点都满足「整圆在工作区内 && 距球心 ≥ minBallDistance」，绝不与球心重合。
    /// </summary>
    public static Point? ChooseFallbackCenter(
        Point ballCenter,
        Rect workArea,
        double circleSize,
        double minBallDistance,
        IReadOnlyList<Point>? preferredCenters,
        out string diagnostic)
    {
        double half = circleSize / 2;
        double left = workArea.Left + half;
        double top = workArea.Top + half;
        double right = workArea.Right - half;
        double bottom = workArea.Bottom - half;

        if (right < left || bottom < top)
        {
            diagnostic = $"no-room-for-circle: area {workArea.Width:0.#}x{workArea.Height:0.#} < circle {circleSize:0.#}";
            return null;
        }

        var centerArea = new Rect(left, top, right - left, bottom - top);

        if (preferredCenters is not null)
        {
            for (int i = 0; i < preferredCenters.Count; i++)
            {
                if (IsFallbackValid(preferredCenters[i], ballCenter, centerArea, minBallDistance))
                {
                    diagnostic = $"preferred[{i}]";
                    return preferredCenters[i];
                }
            }
        }

        // ② 以递增半径做多方向搜索：优先离球近、且落回工作区内的点。
        double maxRadius = GetMaxRadius(ballCenter, centerArea);
        List<double> directions = BuildSearchDirections(ballCenter, centerArea);

        for (double radius = minBallDistance; radius <= maxRadius + Tolerance; radius += 1.0)
        {
            foreach (double angle in directions)
            {
                var candidate = new Point(
                    ballCenter.X + radius * Math.Cos(angle),
                    ballCenter.Y + radius * Math.Sin(angle));

                if (IsFallbackValid(candidate, ballCenter, centerArea, minBallDistance))
                {
                    diagnostic = $"search r={radius:0.#}";
                    return candidate;
                }
            }
        }

        // ③ 最远角点（矩形内离球心最远的点必是某个角点）。
        Point farthest = default;
        double farthestDistance = -1;
        foreach (Point corner in new[]
                 {
                     new Point(centerArea.Left, centerArea.Top),
                     new Point(centerArea.Right, centerArea.Top),
                     new Point(centerArea.Left, centerArea.Bottom),
                     new Point(centerArea.Right, centerArea.Bottom)
                 })
        {
            double distance = (corner - ballCenter).Length;
            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthest = corner;
            }
        }

        if (farthestDistance >= minBallDistance - Tolerance)
        {
            diagnostic = $"farthest-corner r={farthestDistance:0.#}";
            return farthest;
        }

        diagnostic =
            $"no-valid-fallback: farthest point {farthestDistance:0.#} < minBallDistance {minBallDistance:0.#}";
        return null;
    }

    private static bool IsFallbackValid(Point point, Point ballCenter, Rect centerArea, double minBallDistance)
    {
        return point.X >= centerArea.Left - Tolerance &&
               point.X <= centerArea.Right + Tolerance &&
               point.Y >= centerArea.Top - Tolerance &&
               point.Y <= centerArea.Bottom + Tolerance &&
               (point - ballCenter).Length >= minBallDistance - Tolerance;
    }

    private static List<double> BuildSearchDirections(Point ballCenter, Rect centerArea)
    {
        var directions = new List<double>(12);

        // 8 个罗盘方向：从 12 点开始顺时针。
        for (int i = 0; i < 8; i++)
        {
            directions.Add(-Math.PI / 2 + i * Math.PI / 4);
        }

        // 4 个朝向可用区角点的方向（球不在中心时与 45° 罗盘方向不同）。
        foreach (Point corner in new[]
                 {
                     new Point(centerArea.Left, centerArea.Top),
                     new Point(centerArea.Right, centerArea.Top),
                     new Point(centerArea.Left, centerArea.Bottom),
                     new Point(centerArea.Right, centerArea.Bottom)
                 })
        {
            directions.Add(Math.Atan2(corner.Y - ballCenter.Y, corner.X - ballCenter.X));
        }

        return directions;
    }

    private readonly record struct Segment(double Start, double End);

    private readonly record struct Arc(double Start, double Length);
}
