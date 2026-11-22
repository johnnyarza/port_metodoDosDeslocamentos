using FrameAnalysis.Core.Model;

namespace FrameAnalysis.Core.Analysis;

/// <summary>Internal forces at a section, taken on the part to the left of the cut (towards node I). kN and kN·m.</summary>
/// <param name="N">Axial force, tension positive.</param>
/// <param name="V">Shear, V = dM/dx (positive near the left support of a simply supported beam under gravity).</param>
/// <param name="M">Bending moment, positive when it stretches the local −y face (sagging for a beam drawn left to right).</param>
public readonly record struct SectionForces(double N, double V, double M);

/// <summary>Displacements of a point on a member in its local axes: u along the member, v across it (mm), rotation (rad).</summary>
public readonly record struct SectionDisplacements(double U, double V, double Rotation);

/// <summary>Member-end actions in local axes (forces on the member, Gere &amp; Weaver convention). kN and kN·m, counter-clockwise moments positive.</summary>
public readonly record struct MemberEndForces(double NI, double VI, double MI, double NJ, double VJ, double MJ);

public enum ResultKind
{
    LoadCase,
    Uls,
    Sls,
}

/// <summary>Displacements and, at a support, reactions of one node. Global axes; mm, rad, kN, kN·m.</summary>
public sealed class NodeResult
{
    public int NodeId { get; init; }
    public double Ux { get; init; }
    public double Uy { get; init; }
    public double Rz { get; init; }
    /// <summary>Reactions; null where the DOF is not restrained.</summary>
    public double? Rx { get; init; }
    public double? Ry { get; init; }
    public double? Mz { get; init; }

    internal static NodeResult Combine(IReadOnlyList<(NodeResult R, double F)> parts) => new()
    {
        NodeId = parts[0].R.NodeId,
        Ux = parts.Sum(p => p.F * p.R.Ux),
        Uy = parts.Sum(p => p.F * p.R.Uy),
        Rz = parts.Sum(p => p.F * p.R.Rz),
        Rx = parts[0].R.Rx is null ? null : parts.Sum(p => p.F * p.R.Rx!.Value),
        Ry = parts[0].R.Ry is null ? null : parts.Sum(p => p.F * p.R.Ry!.Value),
        Mz = parts[0].R.Mz is null ? null : parts.Sum(p => p.F * p.R.Mz!.Value),
    };
}

/// <summary>
/// Results for one member. Stores the local end forces, end displacements and the local uniform load, so forces and
/// displacements can be found at any point (the original gave mid-span only). Internally N and mm.
/// </summary>
public sealed class MemberResult
{
    private readonly double _l;        // mm
    private readonly double _ea;       // N
    private readonly double _ei;       // N·mm²
    private readonly double[] _f;      // local end forces, N and N·mm
    private readonly double[] _d;      // local end displacements, mm and rad
    private readonly double _qx, _qy;  // local uniform load, N/mm

    internal MemberResult(int memberId, BendingAxis axis, double l, double ea, double ei, double[] f, double[] d, double qx, double qy)
    {
        MemberId = memberId;
        Axis = axis;
        _l = l; _ea = ea; _ei = ei; _f = f; _d = d; _qx = qx; _qy = qy;
    }

    public int MemberId { get; }
    public BendingAxis Axis { get; }
    /// <summary>m</summary>
    public double Length => _l / 1000;
    /// <summary>Uniform load in local axes, kN/m (x along the member, y across it).</summary>
    public double LocalQx => _qx;
    public double LocalQy => _qy;

    public MemberEndForces EndForces => new(_f[0] / 1e3, _f[1] / 1e3, _f[2] / 1e6, _f[3] / 1e3, _f[4] / 1e3, _f[5] / 1e6);

    /// <summary>Internal forces at <paramref name="x"/> m from node I.</summary>
    public SectionForces ForcesAt(double x)
    {
        var s = Clamp(x);
        var n = -_f[0] - _qx * s;
        var v = _f[1] + _qy * s;
        var m = -_f[2] + s * _f[1] + _qy * s * s / 2;
        return new SectionForces(n / 1e3, v / 1e3, m / 1e6);
    }

    /// <summary>Local displacements at <paramref name="x"/> m from node I (exact for a uniform load).</summary>
    public SectionDisplacements DisplacementsAt(double x)
    {
        var s = Clamp(x);
        var l = _l;
        var xi = s / l;
        var u = _d[0] * (1 - xi) + _d[3] * xi + _qx * s * (l - s) / (2 * _ea);
        var n1 = 1 - 3 * xi * xi + 2 * xi * xi * xi;
        var n2 = l * (xi - 2 * xi * xi + xi * xi * xi);
        var n3 = 3 * xi * xi - 2 * xi * xi * xi;
        var n4 = l * (-xi * xi + xi * xi * xi);
        var v = n1 * _d[1] + n2 * _d[2] + n3 * _d[4] + n4 * _d[5] + _qy * s * s * (l - s) * (l - s) / (24 * _ei);
        var dn1 = (-6 * xi + 6 * xi * xi) / l;
        var dn2 = 1 - 4 * xi + 3 * xi * xi;
        var dn3 = (6 * xi - 6 * xi * xi) / l;
        var dn4 = -2 * xi + 3 * xi * xi;
        var rot = dn1 * _d[1] + dn2 * _d[2] + dn3 * _d[4] + dn4 * _d[5] + _qy * s * (l - s) * (l - 2 * s) / (12 * _ei);
        return new SectionDisplacements(u, v, rot);
    }

    /// <summary>Largest |M| along the member and where it occurs (m from node I). Exact: M is at most quadratic.</summary>
    public (double Value, double X) MaxAbsMoment
    {
        get
        {
            var candidates = new List<double> { 0, Length };
            if (_qy != 0)
            {
                var s = -_f[1] / _qy; // V = 0
                if (s > 0 && s < _l) candidates.Add(s / 1000);
            }
            var best = candidates.MaxBy(x => Math.Abs(ForcesAt(x).M));
            return (Math.Abs(ForcesAt(best).M), best);
        }
    }

    /// <summary>Largest tension and largest compression along the member, both ≥ 0, kN. N is linear, so the ends govern.</summary>
    public (double Tension, double Compression) AxialExtremes
    {
        get
        {
            var a = ForcesAt(0).N;
            var b = ForcesAt(Length).N;
            return (Math.Max(0, Math.Max(a, b)), Math.Max(0, -Math.Min(a, b)));
        }
    }

    /// <summary>Largest |V| along the member, kN. V is linear, so the ends govern.</summary>
    public double MaxAbsShear => Math.Max(Math.Abs(ForcesAt(0).V), Math.Abs(ForcesAt(Length).V));

    private double Clamp(double xMetres)
    {
        if (!double.IsFinite(xMetres)) throw new ArgumentOutOfRangeException(nameof(xMetres));
        return Math.Clamp(xMetres * 1000, 0, _l);
    }

    internal static MemberResult Combine(IReadOnlyList<(MemberResult R, double F)> parts)
    {
        var first = parts[0].R;
        var f = new double[6];
        var d = new double[6];
        double qx = 0, qy = 0;
        foreach (var (r, k) in parts)
        {
            for (var i = 0; i < 6; i++)
            {
                f[i] += k * r._f[i];
                d[i] += k * r._d[i];
            }
            qx += k * r._qx;
            qy += k * r._qy;
        }
        return new MemberResult(first.MemberId, first.Axis, first._l, first._ea, first._ei, f, d, qx, qy);
    }
}

/// <summary>Results of one load case or one load combination.</summary>
public sealed class ResultSet
{
    public string Name { get; init; } = "";
    public ResultKind Kind { get; init; }
    public IReadOnlyList<NodeResult> Nodes { get; init; } = [];
    public IReadOnlyList<MemberResult> Members { get; init; } = [];

    public NodeResult Node(int id) => Nodes.First(n => n.NodeId == id);
    public MemberResult Member(int id) => Members.First(m => m.MemberId == id);

    /// <summary>Superposition: the analysis is linear, so a combination is exactly the factored sum of its cases.</summary>
    public static ResultSet Combine(string name, ResultKind kind, IReadOnlyList<(ResultSet Set, double Factor)> parts)
    {
        if (parts.Count == 0) throw new ArgumentException("A combination needs at least one load case.", nameof(parts));
        var first = parts[0].Set;
        return new ResultSet
        {
            Name = name,
            Kind = kind,
            Nodes = first.Nodes.Select((_, i) => NodeResult.Combine(parts.Select(p => (p.Set.Nodes[i], p.Factor)).ToList())).ToList(),
            Members = first.Members.Select((_, i) => MemberResult.Combine(parts.Select(p => (p.Set.Members[i], p.Factor)).ToList())).ToList(),
        };
    }
}

/// <summary>Everything one analysis produced: one set per load case, then one per combination.</summary>
public sealed class FrameResults
{
    public IReadOnlyList<ResultSet> Cases { get; init; } = [];
    public IReadOnlyList<ResultSet> Combinations { get; init; } = [];
    public IEnumerable<ResultSet> All => Cases.Concat(Combinations);

    public ResultSet Case(string name) => Cases.First(c => c.Name == name);
    public ResultSet Combination(string name) => Combinations.First(c => c.Name == name);
}
