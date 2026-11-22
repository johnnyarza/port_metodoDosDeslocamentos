using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Data;
using FrameAnalysis.Core.Model;
using static FrameAnalysis.Tests.TestFrames;

namespace FrameAnalysis.Tests;

/// <summary>Running only the ticked combinations, and the largest forces of each result.</summary>
public class SummaryTests
{
    [Fact]
    public void UntickedCombinationsAreNotRun()
    {
        var model = Beam(6, SupportType.Pin, SupportType.RollerVertical, -10, out var d);
        model.Combinations.Add(new LoadCombination { Name = "A", Factors = { [d.Id] = 1.4 } });
        model.Combinations.Add(new LoadCombination { Name = "B", Factors = { [d.Id] = 1.25 }, IsActive = false });
        model.Combinations.Add(new LoadCombination { Name = "C", LimitState = LimitState.Sls, Factors = { [d.Id] = 1 } });

        var results = FrameSolver.Analyze(model);
        Assert.Equal(["A", "C"], results.Combinations.Select(c => c.Name));
        Assert.Single(results.Cases); // load cases are always solved
    }

    [Fact]
    public void TheTickIsSavedAndOldFilesRunEveryCombination()
    {
        var model = Beam(6, SupportType.Pin, SupportType.RollerVertical, -10, out var d);
        model.Combinations.Add(new LoadCombination { Name = "Off", Factors = { [d.Id] = 1 }, IsActive = false });
        var back = FrameModelJson.Deserialize(FrameModelJson.Serialize(model));
        Assert.False(back.Combinations.Single().IsActive);

        // Files saved before the tick existed have no "isActive": they load as ticked.
        var old = FrameModelJson.Serialize(model).Replace("\"isActive\": false,", "").Replace("\"isActive\":false,", "");
        Assert.DoesNotContain("isActive", old);
        Assert.True(FrameModelJson.Deserialize(old).Combinations.Single().IsActive);
    }

    [Fact]
    public void SummaryOfASimplySupportedBeam()
    {
        const double w = 10, l = 6;
        var model = Beam(l, SupportType.Pin, SupportType.RollerVertical, -w, out _);
        var s = ResultSummary.From(FrameSolver.Analyze(model).Case("D"));

        Near(w * l * l / 8, s.Moment.Value, 1e-9, "wL²/8");
        Near(l / 2, s.Moment.X, 1e-9);
        Near(w * l / 2, s.Shear.Value, 1e-9, "wL/2");
        Near(5 * w * Math.Pow(l * 1000, 4) / (384 * EIx), s.Displacement.Value, 1e-9, "5wL⁴/384EI at mid-span (a sample point)");
        Near(l / 2, s.Displacement.X, 1e-9);
        Assert.Equal(0, s.Tension.Value);
        Assert.Equal(0, s.Compression.Value);
    }

    [Fact]
    public void SummaryFindsTheGoverningMemberOfAPortal()
    {
        var model = Empty();
        var a = model.AddNode(0, 0, SupportType.Fixed);
        var b = model.AddNode(6, 0, SupportType.Fixed);
        var c = model.AddNode(0, 4);
        var e = model.AddNode(6, 4);
        var left = model.AddMember(a.Id, c.Id);
        var right = model.AddMember(b.Id, e.Id);
        var beam = model.AddMember(c.Id, e.Id);
        var lc = model.AddLoadCase("D", LoadCategory.D);
        model.AddMemberLoad(beam.Id, lc.Id, 0, -10);
        model.AddNodalLoad(c.Id, lc.Id, 5, 0, 0);
        var set = FrameSolver.Analyze(model).Case("D");
        var s = ResultSummary.From(set);

        // Gravity plus a push to the right: the leeward (right) column carries more of the beam's 60 kN.
        Assert.Equal(right.Id, s.Compression.Id);
        Near(set.Node(b.Id).Ry!.Value, s.Compression.Value, 1e-9, "column compression = its base reaction");

        // Every reported value is the largest over all members.
        foreach (var r in set.Members)
        {
            Assert.True(r.MaxAbsMoment.Value <= s.Moment.Value + 1e-9);
            Assert.True(r.MaxAbsShear <= s.Shear.Value + 1e-9);
            Assert.True(r.AxialExtremes.Compression <= s.Compression.Value + 1e-9);
        }
        Assert.Contains(s.Moment.Id, new[] { left.Id, right.Id, beam.Id });
    }
}
