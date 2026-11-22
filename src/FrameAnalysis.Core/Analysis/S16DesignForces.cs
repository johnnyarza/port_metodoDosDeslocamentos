using FrameAnalysis.Core.Model;

namespace FrameAnalysis.Core.Analysis;

/// <summary>
/// The values one member's result gives to a WBeamS16App task, all without sign (the user's decision).
/// kN and kN·m. Built from one member of one result set (a load case or a combination) that the user picks.
/// </summary>
public sealed record S16DesignForces
{
    public required int MemberId { get; init; }
    public required string ResultName { get; init; }
    public required BendingAxis Axis { get; init; }

    /// <summary>Largest tension along the member (Tf). Both Tf and Cf are &gt; 0 only when an axial load changes the sign of N.</summary>
    public required double Tf { get; init; }
    /// <summary>Largest compression along the member (Cf).</summary>
    public required double Cf { get; init; }
    /// <summary>Largest |V| (Vf).</summary>
    public required double Vf { get; init; }
    /// <summary>Largest |M| (Mf, which is Mfx for a major-axis member and Mfy for a minor-axis one; also ω2's Mmax).</summary>
    public required double Mf { get; init; }
    public double Mfx => Axis == BendingAxis.Major ? Mf : 0;
    public double Mfy => Axis == BendingAxis.Minor ? Mf : 0;

    /// <summary>|M| at ¼, ½ and ¾ of the length (ω2's Ma, Mb, Mc).</summary>
    public required double Ma { get; init; }
    public required double Mb { get; init; }
    public required double Mc { get; init; }

    /// <summary>End moments for ω1: Mj is the larger, Mi the smaller.</summary>
    public required double Mi { get; init; }
    public required double Mj { get; init; }
    /// <summary>True when the end moments bend the member in double curvature (they have opposite signs as internal moments).</summary>
    public required bool DoubleCurvature { get; init; }

    public static S16DesignForces From(ResultSet set, int memberId)
    {
        var m = set.Member(memberId);
        var l = m.Length;
        var (tension, compression) = m.AxialExtremes;
        var m0 = m.ForcesAt(0).M;
        var m1 = m.ForcesAt(l).M;
        return new S16DesignForces
        {
            MemberId = memberId,
            ResultName = set.Name,
            Axis = m.Axis,
            Tf = tension,
            Cf = compression,
            Vf = m.MaxAbsShear,
            Mf = m.MaxAbsMoment.Value,
            Ma = Math.Abs(m.ForcesAt(l / 4).M),
            Mb = Math.Abs(m.ForcesAt(l / 2).M),
            Mc = Math.Abs(m.ForcesAt(3 * l / 4).M),
            Mi = Math.Min(Math.Abs(m0), Math.Abs(m1)),
            Mj = Math.Max(Math.Abs(m0), Math.Abs(m1)),
            DoubleCurvature = m0 * m1 < 0,
        };
    }
}
