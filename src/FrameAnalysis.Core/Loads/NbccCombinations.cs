using FrameAnalysis.Core.Model;

namespace FrameAnalysis.Core.Loads;

/// <summary>
/// Starting set of NBCC load combinations, which the user can then edit (the table is user data, not fixed).
/// ULS follows the principal/companion pattern of NBCC Table 4.1.3.2.-A without earthquake; each "or" becomes its
/// own combination and every combination is made with both 1.25D and 0.9D.
/// Several load cases of one category: D, L and S cases are added together (they act at the same time);
/// W cases are alternatives (e.g. wind from the left or the right), so each gets its own combinations.
/// Check the factors against your copy of the code: for example, the companion factor on L in combinations 3 and 4
/// is 0.5 here, and NBCC raises it to 1.0 for storage, equipment areas and service rooms.
/// </summary>
public static class NbccCombinations
{
    private sealed record Template(string Name, LimitState State, double D, double L, double S, double W);

    private static readonly Template[] Uls =
    [
        new("1", LimitState.Uls, 1.4, 0, 0, 0),
        new("2", LimitState.Uls, 1.25, 1.5, 0, 0),
        new("2", LimitState.Uls, 1.25, 1.5, 1.0, 0),
        new("2", LimitState.Uls, 1.25, 1.5, 0, 0.4),
        new("3", LimitState.Uls, 1.25, 0, 1.5, 0),
        new("3", LimitState.Uls, 1.25, 0.5, 1.5, 0),
        new("3", LimitState.Uls, 1.25, 0, 1.5, 0.4),
        new("4", LimitState.Uls, 1.25, 0, 0, 1.4),
        new("4", LimitState.Uls, 1.25, 0.5, 0, 1.4),
        new("4", LimitState.Uls, 1.25, 0, 0.5, 1.4),
    ];

    /// <summary>Specified (unfactored) loads for deflection and other serviceability checks.</summary>
    private static readonly Template[] Sls =
    [
        new("S1", LimitState.Sls, 1, 0, 0, 0),
        new("S2", LimitState.Sls, 1, 1, 0, 0),
        new("S3", LimitState.Sls, 1, 0, 1, 0),
        new("S4", LimitState.Sls, 1, 0, 0, 1),
        new("S5", LimitState.Sls, 1, 1, 0.5, 0),
        new("S6", LimitState.Sls, 1, 0.5, 1, 0),
    ];

    /// <summary>
    /// Builds the default combinations for the given load cases. A combination that needs a category with no load
    /// case is left out, and duplicates are dropped. Names describe the factors, e.g. "ULS 2: 1.25D + 1.5L + 1.0S".
    /// </summary>
    public static List<LoadCombination> CreateDefaults(IReadOnlyList<LoadCase> cases)
    {
        var has = cases.Select(c => c.Category).ToHashSet();
        var winds = cases.Where(c => c.Category == LoadCategory.W).ToList();
        var result = new List<LoadCombination>();
        var seen = new HashSet<string>();

        foreach (var t in Uls.Concat(Sls))
        {
            if (t.D == 0 || !has.Contains(LoadCategory.D)) continue;
            if ((t.L > 0 && !has.Contains(LoadCategory.L)) || (t.S > 0 && !has.Contains(LoadCategory.S)) || (t.W > 0 && winds.Count == 0))
                continue;

            var dFactors = t.State == LimitState.Uls && t.D == 1.25 ? new[] { 1.25, 0.9 } : [t.D];
            var windOptions = t.W > 0 ? winds.Cast<LoadCase?>().ToList() : [null];
            foreach (var d in dFactors)
                foreach (var w in windOptions)
                {
                    var c = new LoadCombination { LimitState = t.State };
                    foreach (var lc in cases)
                    {
                        var f = lc.Category switch
                        {
                            LoadCategory.D => d,
                            LoadCategory.L => t.L,
                            LoadCategory.S => t.S,
                            LoadCategory.W => w is not null && w.Id == lc.Id ? t.W : 0,
                            _ => 0,
                        };
                        if (f != 0) c.Factors[lc.Id] = f;
                    }
                    c.Name = $"{(t.State == LimitState.Uls ? "ULS " + t.Name : "SLS " + t.Name[1..])}: {Describe(t, d, w)}";
                    if (seen.Add(c.Name)) result.Add(c);
                }
        }
        return result;
    }

    private static string Describe(Template t, double d, LoadCase? wind)
    {
        var parts = new List<string> { Term(d, "D") };
        if (t.L > 0) parts.Add(Term(t.L, "L"));
        if (t.S > 0) parts.Add(Term(t.S, "S"));
        if (t.W > 0) parts.Add(Term(t.W, wind is null ? "W" : $"W ({wind.Name})"));
        return string.Join(" + ", parts);

        string Term(double f, string what) =>
            t.State == LimitState.Sls && f == 1 ? what : f.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + what;
    }
}
