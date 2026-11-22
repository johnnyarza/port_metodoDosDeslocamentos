using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Loads;
using FrameAnalysis.Core.Model;
using static FrameAnalysis.Tests.TestFrames;

namespace FrameAnalysis.Tests;

public class CombinationAndDesignTests
{
    private static List<LoadCase> Cases(params (string Name, LoadCategory Cat)[] list) =>
        list.Select((c, i) => new LoadCase { Id = i + 1, Name = c.Name, Category = c.Cat }).ToList();

    [Fact]
    public void Nbcc_AllCategoriesAndTwoWinds()
    {
        var cases = Cases(("D", LoadCategory.D), ("L", LoadCategory.L), ("S", LoadCategory.S), ("W left", LoadCategory.W), ("W right", LoadCategory.W));
        var combos = NbccCombinations.CreateDefaults(cases);

        Assert.Equal(29, combos.Count(c => c.LimitState == LimitState.Uls));
        Assert.Equal(7, combos.Count(c => c.LimitState == LimitState.Sls));
        Assert.Equal(combos.Count, combos.Select(c => c.Name).Distinct().Count());

        var c1 = combos.Single(c => c.Name == "ULS 1: 1.4D");
        Assert.Equal(1.4, c1.FactorOf(1));
        Assert.Equal(0, c1.FactorOf(2));

        var c2 = combos.Single(c => c.Name == "ULS 2: 1.25D + 1.5L + 1.0S");
        Assert.Equal([1.25, 1.5, 1.0, 0, 0], cases.Select(c => c.FactorOf(c2)));

        var c4 = combos.Single(c => c.Name == "ULS 4: 0.9D + 1.4W (W right)");
        Assert.Equal([0.9, 0, 0, 0, 1.4], cases.Select(c => c.FactorOf(c4)));

        var s5 = combos.Single(c => c.Name == "SLS 5: D + L + 0.5S");
        Assert.Equal([1, 1, 0.5, 0, 0], cases.Select(c => c.FactorOf(s5)));
    }

    [Fact]
    public void Nbcc_OnlyDeadAndLive()
    {
        var combos = NbccCombinations.CreateDefaults(Cases(("D", LoadCategory.D), ("L", LoadCategory.L)));
        Assert.Equal(["ULS 1: 1.4D", "ULS 2: 1.25D + 1.5L", "ULS 2: 0.9D + 1.5L", "SLS 1: D", "SLS 2: D + L"], combos.Select(c => c.Name));
    }

    [Fact]
    public void Nbcc_SeveralDeadCases_AreAddedTogether()
    {
        var cases = Cases(("Self", LoadCategory.D), ("Finishes", LoadCategory.D), ("L", LoadCategory.L));
        var c = NbccCombinations.CreateDefaults(cases).Single(x => x.Name == "ULS 2: 1.25D + 1.5L");
        Assert.Equal([1.25, 1.25, 1.5], cases.Select(x => x.FactorOf(c)));
    }

    [Fact]
    public void Nbcc_DefaultsSolveWithoutProblems()
    {
        var m = Beam(6, SupportType.Pin, SupportType.RollerVertical, -5, out var d);
        d.IncludeSelfWeight = true;
        var l = m.AddLoadCase("L", LoadCategory.L);
        m.AddMemberLoad(1, l.Id, 0, -8);
        m.Combinations.AddRange(NbccCombinations.CreateDefaults(m.LoadCases));
        var r = FrameSolver.Analyze(m);
        var wl = 1.25 * (5 + DeadLoad) + 1.5 * 8;
        Near(wl * 36 / 8, r.Combination("ULS 2: 1.25D + 1.5L").Members[0].ForcesAt(3).M, 1e-9);
    }

    [Fact]
    public void S16Forces_SimplySupportedBeam()
    {
        var m = Beam(6, SupportType.Pin, SupportType.RollerVertical, -10, out _);
        var f = S16DesignForces.From(FrameSolver.Analyze(m).Cases[0], 1);
        Near(45, f.Mf, 1e-9); Near(45, f.Mfx, 1e-9); Assert.Equal(0, f.Mfy);
        Near(3 * 10 * 36 / 32.0, f.Ma, 1e-9);
        Near(45, f.Mb, 1e-9);
        Near(3 * 10 * 36 / 32.0, f.Mc, 1e-9);
        Near(30, f.Vf, 1e-9);
        Near(0, f.Tf, 1e-9); Near(0, f.Cf, 1e-9);
        Near(0, f.Mi, 1e-9); Near(0, f.Mj, 1e-9);
    }

    [Fact]
    public void S16Forces_PortalColumn_IsInCompressionAndDoubleCurvature()
    {
        var m = Empty();
        m.AddNode(0, 0, SupportType.Fixed);
        m.AddNode(4, 0, SupportType.Fixed);
        m.AddNode(0, 3);
        m.AddNode(4, 3);
        m.AddMember(1, 3, BendingAxis.Minor);
        m.AddMember(2, 4);
        m.AddMember(3, 4);
        var d = m.AddLoadCase("D", LoadCategory.D);
        m.AddMemberLoad(3, d.Id, 0, -12);

        var set = FrameSolver.Analyze(m).Cases[0];
        var col = S16DesignForces.From(set, 1);
        Assert.True(col.Cf > 0);
        Assert.Equal(0, col.Tf);
        Assert.True(col.DoubleCurvature);
        Assert.True(col.Mj >= col.Mi);
        Assert.Equal(0, col.Mfx);
        Near(col.Mf, col.Mfy, 1e-12);
        var end = set.Member(1).EndForces;
        Near(Math.Max(Math.Abs(end.MI), Math.Abs(end.MJ)), col.Mj, 1e-9);
    }
}

internal static class LoadCaseExtensions
{
    public static double FactorOf(this LoadCase lc, LoadCombination c) => c.FactorOf(lc.Id);
}
