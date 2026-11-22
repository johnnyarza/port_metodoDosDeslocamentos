using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Model;

namespace FrameAnalysis.Core.Drawing;

/// <summary>A point in model coordinates (m, global X right and Y up).</summary>
public readonly record struct Point2(double X, double Y)
{
    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2 operator *(double k, Point2 a) => new(k * a.X, k * a.Y);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

public enum DiagramQuantity
{
    Axial,
    Shear,
    Moment,
}

/// <summary>One point of a diagram: the station on the member axis and the diagram's ordinate drawn from it.</summary>
public readonly record struct DiagramStation(double X, double Value, Point2 Base, Point2 Tip);

/// <summary>A member's diagram, ends included, plus the stations worth labelling.</summary>
public sealed record MemberDiagram(int MemberId, IReadOnlyList<DiagramStation> Stations, IReadOnlyList<DiagramStation> Labels);

/// <summary>Where a member lies: its end points, its unit axis (I → J) and its unit local y (axis turned 90° counter-clockwise).</summary>
public readonly record struct MemberFrame(int MemberId, Point2 I, Point2 J, Point2 AxisX, Point2 AxisY, double Length)
{
    public Point2 At(double x) => I + x * AxisX;
}

/// <summary>
/// The geometry of frame drawings, in model coordinates, so the UI only has to map it to the screen and paint it.
/// Diagram side rules (the usual ones for frames):
/// <list type="bullet">
/// <item>M is drawn on the tension side: positive M stretches the local −y face, so it is drawn towards −y (below a
/// beam drawn left to right, under sagging).</item>
/// <item>N and V are drawn towards +y when positive (tension, V = dM/dx &gt; 0) and towards −y when negative.</item>
/// </list>
/// </summary>
public static class FramePlot
{
    /// <summary>Members whose two nodes exist and are apart; the others are skipped (a model being edited may be broken).</summary>
    public static IReadOnlyList<MemberFrame> Members(FrameModel model)
    {
        var list = new List<MemberFrame>(model.Members.Count);
        foreach (var m in model.Members)
            if (TryFrame(model, m, out var f)) list.Add(f);
        return list;
    }

    public static bool TryFrame(FrameModel model, Member m, out MemberFrame frame)
    {
        frame = default;
        var a = model.FindNode(m.NodeI);
        var b = model.FindNode(m.NodeJ);
        if (a is null || b is null) return false;
        var i = new Point2(a.X, a.Y);
        var d = new Point2(b.X, b.Y) - i;
        var l = d.Length;
        if (!(l > 0) || !double.IsFinite(l)) return false;
        var ax = (1 / l) * d;
        frame = new MemberFrame(m.Id, i, new Point2(b.X, b.Y), ax, new Point2(-ax.Y, ax.X), l);
        return true;
    }

    /// <summary>The smallest box holding every node, or null when there are no nodes.</summary>
    public static (Point2 Min, Point2 Max)? Bounds(FrameModel model)
    {
        var nodes = model.Nodes.Where(n => double.IsFinite(n.X) && double.IsFinite(n.Y)).ToList();
        if (nodes.Count == 0) return null;
        return (new Point2(nodes.Min(n => n.X), nodes.Min(n => n.Y)), new Point2(nodes.Max(n => n.X), nodes.Max(n => n.Y)));
    }

    /// <summary>The value of a quantity at x (m) along a member, in kN or kN·m.</summary>
    public static double ValueAt(MemberResult r, DiagramQuantity q, double x)
    {
        var f = r.ForcesAt(x);
        return q switch
        {
            DiagramQuantity.Axial => f.N,
            DiagramQuantity.Shear => f.V,
            _ => f.M,
        };
    }

    /// <summary>The largest |value| of a quantity over every member of a result set (0 when there is none).</summary>
    public static double MaxAbs(ResultSet set, DiagramQuantity q) =>
        set.Members.Select(r => Stations(r, q, 4).Max(x => Math.Abs(ValueAt(r, q, x)))).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The diagram of every member present in both the model and the results. <paramref name="scale"/> is the drawn
    /// ordinate per unit value (m per kN or per kN·m). Results are matched to members by id; when the model changed
    /// after the solve, stations are taken as fractions of the solved length.
    /// </summary>
    public static IReadOnlyList<MemberDiagram> Diagram(FrameModel model, ResultSet set, DiagramQuantity q, double scale, int segments = 32)
    {
        var list = new List<MemberDiagram>();
        foreach (var f in Members(model))
        {
            var r = set.Members.FirstOrDefault(x => x.MemberId == f.MemberId);
            if (r is null) continue;
            var side = q == DiagramQuantity.Moment ? -1.0 : 1.0;
            var stations = Stations(r, q, segments).Select(x =>
            {
                var v = ValueAt(r, q, x);
                var b = f.At(x / r.Length * f.Length);
                return new DiagramStation(x, v, b, b + (side * scale * v) * f.AxisY);
            }).ToList();
            list.Add(new MemberDiagram(f.MemberId, stations, LabelStations(r, q, stations)));
        }
        return list;
    }

    /// <summary>
    /// Deformed shape of every member: exact displacements (mm) along the member, magnified by
    /// <paramref name="magnification"/> and converted to model metres.
    /// </summary>
    public static IReadOnlyList<(int MemberId, IReadOnlyList<Point2> Points)> Deformed(FrameModel model, ResultSet set, double magnification, int segments = 24)
    {
        var list = new List<(int, IReadOnlyList<Point2>)>();
        foreach (var f in Members(model))
        {
            var r = set.Members.FirstOrDefault(x => x.MemberId == f.MemberId);
            if (r is null) continue;
            var pts = new List<Point2>(segments + 1);
            for (var k = 0; k <= segments; k++)
            {
                var x = r.Length * k / segments;
                var d = r.DisplacementsAt(x);
                pts.Add(f.At(x / r.Length * f.Length) + (magnification / 1000) * ((d.U * f.AxisX) + (d.V * f.AxisY)));
            }
            list.Add((f.MemberId, pts));
        }
        return list;
    }

    /// <summary>The largest displacement (mm) of any point of any member, ends included.</summary>
    public static double MaxDisplacement(ResultSet set) =>
        set.Members.Select(r => Enumerable.Range(0, 17).Max(k =>
        {
            var d = r.DisplacementsAt(r.Length * k / 16);
            return Math.Sqrt(d.U * d.U + d.V * d.V);
        })).DefaultIfEmpty(0).Max();

    /// <summary>A magnification that draws the largest displacement as <paramref name="target"/> (m), rounded to 1, 2 or 5 × 10ⁿ.</summary>
    public static double NiceMagnification(double maxDisplacementMm, double target)
    {
        if (!(maxDisplacementMm > 0) || !(target > 0)) return 1;
        var raw = target / (maxDisplacementMm / 1000);
        var p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var m = raw / p;
        return (m >= 5 ? 5 : m >= 2 ? 2 : 1) * p;
    }

    /// <summary>
    /// Splits a diagram into runs of one sign (+1 or −1; 0 only when the whole diagram is zero), for colouring.
    /// A crossing between two stations is interpolated and shared by both runs; a station that is exactly zero
    /// (mid-span shear of a symmetric beam, for one) is shared as it is.
    /// </summary>
    public static IReadOnlyList<(IReadOnlyList<DiagramStation> Stations, int Sign)> SignRuns(IReadOnlyList<DiagramStation> st)
    {
        var runs = new List<(IReadOnlyList<DiagramStation>, int)>();
        if (st.Count == 0) return runs;
        var eps = st.Max(s => Math.Abs(s.Value)) * 1e-9;
        int Sign(double v) => Math.Abs(v) <= eps ? 0 : Math.Sign(v);

        var cur = new List<DiagramStation> { st[0] };
        var curSign = Sign(st[0].Value);
        for (var k = 1; k < st.Count; k++)
        {
            var (a, b) = (st[k - 1], st[k]);
            var sb = Sign(b.Value);
            if (sb != 0 && curSign != 0 && sb != curSign)
            {
                DiagramStation zero;
                if (Sign(a.Value) == 0) zero = a;
                else
                {
                    var t = a.Value / (a.Value - b.Value);
                    var z = a.Base + t * (b.Base - a.Base);
                    zero = new DiagramStation(a.X + t * (b.X - a.X), 0, z, z);
                    cur.Add(zero);
                }
                runs.Add((cur, curSign));
                cur = [zero];
                curSign = sb;
            }
            else if (curSign == 0) curSign = sb;
            cur.Add(b);
        }
        runs.Add((cur, curSign));
        return runs;
    }

    /// <summary>Uniform stations plus, for M, the point of largest |M| (where V = 0) so the peak is drawn exactly.</summary>
    private static List<double> Stations(MemberResult r, DiagramQuantity q, int segments)
    {
        var xs = Enumerable.Range(0, segments + 1).Select(k => r.Length * k / segments).ToList();
        if (q == DiagramQuantity.Moment)
        {
            var peak = r.MaxAbsMoment.X;
            if (!xs.Any(x => Math.Abs(x - peak) < 1e-9)) xs.Add(peak);
            xs.Sort();
        }
        return xs;
    }

    /// <summary>Both ends, plus the interior peak of M when it is larger than both ends.</summary>
    private static List<DiagramStation> LabelStations(MemberResult r, DiagramQuantity q, List<DiagramStation> stations)
    {
        var labels = new List<DiagramStation> { stations[0], stations[^1] };
        if (q == DiagramQuantity.Moment)
        {
            var peak = stations.MaxBy(s => Math.Abs(s.Value));
            var ends = Math.Max(Math.Abs(stations[0].Value), Math.Abs(stations[^1].Value));
            if (peak.X > 1e-6 && peak.X < r.Length - 1e-6 && Math.Abs(peak.Value) > ends + 1e-6) labels.Add(peak);
        }
        return labels;
    }

    /// <summary>
    /// The loads of one load case, or of a combination when <paramref name="factors"/> gives every case's factor:
    /// point loads summed per node and distributed loads summed per member (global kN, kN/m, kN·m).
    /// Self-weight is returned as one number (kN/m, downward), since it is the same on every member.
    /// </summary>
    public static PlotLoads Loads(FrameModel model, Func<int, double> factors)
    {
        var nodal = model.NodalLoads
            .Where(l => factors(l.LoadCaseId) != 0)
            .GroupBy(l => l.NodeId)
            .Select(g => new NodalLoadSum(g.Key, g.Sum(l => factors(l.LoadCaseId) * l.Fx), g.Sum(l => factors(l.LoadCaseId) * l.Fy),
                g.Sum(l => factors(l.LoadCaseId) * l.Mz)))
            .Where(l => l.Fx != 0 || l.Fy != 0 || l.Mz != 0)
            .ToList();
        var member = model.MemberLoads
            .Where(l => factors(l.LoadCaseId) != 0)
            .GroupBy(l => l.MemberId)
            .Select(g => new MemberLoadSum(g.Key, g.Sum(l => factors(l.LoadCaseId) * l.Qx), g.Sum(l => factors(l.LoadCaseId) * l.Qy)))
            .Where(l => l.Qx != 0 || l.Qy != 0)
            .ToList();
        var selfWeight = model.LoadCases.Where(c => c.IncludeSelfWeight).Sum(c => factors(c.Id)) * model.Section.DeadLoad;
        return new PlotLoads(nodal, member, selfWeight);
    }
}

public readonly record struct NodalLoadSum(int NodeId, double Fx, double Fy, double Mz);

public readonly record struct MemberLoadSum(int MemberId, double Qx, double Qy);

public sealed record PlotLoads(IReadOnlyList<NodalLoadSum> Nodal, IReadOnlyList<MemberLoadSum> Member, double SelfWeight);
