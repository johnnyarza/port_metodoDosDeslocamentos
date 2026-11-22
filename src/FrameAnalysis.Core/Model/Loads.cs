namespace FrameAnalysis.Core.Model;

/// <summary>NBCC load types used so far: dead, live, snow, wind.</summary>
public enum LoadCategory
{
    D,
    L,
    S,
    W,
}

/// <summary>
/// One set of loads that is solved on its own. Several cases may share a category (e.g. wind left and wind right);
/// see <see cref="Loads.NbccCombinations"/> for how they are combined.
/// </summary>
public sealed class LoadCase
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public LoadCategory Category { get; set; }
    /// <summary>Adds the section's dead load to every member as a downward load (D cases only, at most one case).</summary>
    public bool IncludeSelfWeight { get; set; }
}

/// <summary>A point load on a node in global axes. kN and kN·m, X right, Y up, moment counter-clockwise.</summary>
public sealed class NodalLoad
{
    public int NodeId { get; set; }
    public int LoadCaseId { get; set; }
    public double Fx { get; set; }
    public double Fy { get; set; }
    public double Mz { get; set; }
}

/// <summary>
/// A uniform load over the full member length, in global directions, kN per m of member length.
/// Signed like the thesis: +Y is up, so gravity is negative (e.g. Qy = −12).
/// </summary>
public sealed class MemberLoad
{
    public int MemberId { get; set; }
    public int LoadCaseId { get; set; }
    public double Qx { get; set; }
    public double Qy { get; set; }
}

public enum LimitState
{
    Uls,
    Sls,
}

/// <summary>A factored sum of load cases. <see cref="Factors"/> is keyed by load case id; a missing case counts as 0.</summary>
public sealed class LoadCombination
{
    public string Name { get; set; } = "";
    public LimitState LimitState { get; set; }

    /// <summary>Whether the solver builds this combination. Unticked ones stay in the table (and the file) but are not run.</summary>
    public bool IsActive { get; set; } = true;

    public Dictionary<int, double> Factors { get; set; } = [];

    public double FactorOf(int loadCaseId) => Factors.TryGetValue(loadCaseId, out var f) ? f : 0;
}
