namespace FrameAnalysis.Core.Analysis;

/// <summary>A largest value and where it occurs: member (or node) id and x along the member (m from node I).</summary>
public readonly record struct Extreme(double Value, int Id, double X)
{
    public static Extreme None => new(0, 0, 0);
}

/// <summary>
/// The largest forces and displacement anywhere in the frame for one result set, as magnitudes (no signs):
/// tension, compression, shear, moment, and the total displacement (√(u² + v²)) of any point of any member.
/// </summary>
public sealed record ResultSummary(string Name, ResultKind Kind, Extreme Tension, Extreme Compression, Extreme Shear, Extreme Moment,
    Extreme Displacement)
{
    /// <summary>Points per member at which displacements are sampled (N, V and M extremes are exact).</summary>
    public const int DisplacementSamples = 64;

    public static ResultSummary From(ResultSet set)
    {
        Extreme t = Extreme.None, c = Extreme.None, v = Extreme.None, m = Extreme.None, d = Extreme.None;
        foreach (var r in set.Members)
        {
            // N and V are linear along a member (uniform loads only), so the ends govern; M peaks at an end or where V = 0.
            foreach (var x in new[] { 0, r.Length })
            {
                var f = r.ForcesAt(x);
                if (f.N > t.Value) t = new Extreme(f.N, r.MemberId, x);
                if (-f.N > c.Value) c = new Extreme(-f.N, r.MemberId, x);
                if (Math.Abs(f.V) > v.Value) v = new Extreme(Math.Abs(f.V), r.MemberId, x);
            }
            var (mMax, mx) = r.MaxAbsMoment;
            if (mMax > m.Value) m = new Extreme(mMax, r.MemberId, mx);

            for (var k = 0; k <= DisplacementSamples; k++)
            {
                var x = r.Length * k / DisplacementSamples;
                var u = r.DisplacementsAt(x);
                var total = Math.Sqrt(u.U * u.U + u.V * u.V);
                if (total > d.Value) d = new Extreme(total, r.MemberId, x);
            }
        }
        return new ResultSummary(set.Name, set.Kind, t, c, v, m, d);
    }
}
