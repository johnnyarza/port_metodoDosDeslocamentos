using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Drawing;
using FrameAnalysis.Core.Model;
using static FrameAnalysis.Tests.TestFrames;

namespace FrameAnalysis.Tests;

/// <summary>Drawing geometry: which side each diagram goes on, and the deformed shape.</summary>
public class PlotTests
{
    [Fact]
    public void MomentIsDrawnOnTheTensionSide_WhicheverWayTheBeamIsDrawn()
    {
        // Simply supported 6 m beam, 10 kN/m down: sagging, so the bottom is in tension and M goes below the beam.
        foreach (var rightToLeft in new[] { false, true })
        {
            var model = Empty();
            var a = model.AddNode(0, 0, SupportType.Pin);
            var b = model.AddNode(6, 0, SupportType.RollerVertical);
            var m = rightToLeft ? model.AddMember(b.Id, a.Id) : model.AddMember(a.Id, b.Id);
            var d = model.AddLoadCase("D", LoadCategory.D);
            model.AddMemberLoad(m.Id, d.Id, 0, -10);
            var set = FrameSolver.Analyze(model).Case("D");

            var diagram = FramePlot.Diagram(model, set, DiagramQuantity.Moment, scale: 0.01).Single();
            var peak = diagram.Labels.MaxBy(s => Math.Abs(s.Value));
            Near(3, peak.Base.X, 1e-9, "peak at mid-span");
            Near(-0.45, peak.Tip.Y, 1e-9, "45 kN·m × 0.01 m drawn below");
            Assert.All(diagram.Stations, s => Assert.True(s.Tip.Y <= 1e-12));
        }
    }

    [Fact]
    public void AxialAndShearArePositiveTowardsLocalY()
    {
        var model = Beam(6, SupportType.Pin, SupportType.RollerVertical, -10, out _);
        var set = FrameSolver.Analyze(model).Case("D");

        var v = FramePlot.Diagram(model, set, DiagramQuantity.Shear, 0.01).Single();
        Near(0.30, v.Stations[0].Tip.Y, 1e-9, "V(0) = +30 drawn up");
        Near(-0.30, v.Stations[^1].Tip.Y, 1e-9, "V(L) = −30 drawn down");
        Assert.Equal(2, v.Labels.Count);

        // A column pulled up at the top (tension) has local y pointing to −X, so tension is drawn to the left.
        var col = Empty();
        var bottom = col.AddNode(0, 0, SupportType.Fixed);
        var top = col.AddNode(0, 3);
        col.AddMember(bottom.Id, top.Id);
        var lc = col.AddLoadCase("D", LoadCategory.D);
        col.AddNodalLoad(top.Id, lc.Id, 0, 20, 0);
        var n = FramePlot.Diagram(col, FrameSolver.Analyze(col).Case("D"), DiagramQuantity.Axial, 0.01).Single();
        Assert.All(n.Stations, s => Near(-0.20, s.Tip.X, 1e-9, "N = +20 drawn towards local +y"));
    }

    [Fact]
    public void DiagramsSplitIntoSignRunsAtTheirZeros()
    {
        // Simply supported: V is exactly zero at the mid-span station; the run must still split there.
        var ss = Beam(6, SupportType.Pin, SupportType.RollerVertical, -10, out _);
        var v = FramePlot.Diagram(ss, FrameSolver.Analyze(ss).Case("D"), DiagramQuantity.Shear, 0.01).Single();
        var vRuns = FramePlot.SignRuns(v.Stations);
        Assert.Equal([1, -1], vRuns.Select(r => r.Sign));
        Near(3, vRuns[0].Stations[^1].X, 1e-9);
        Near(3, vRuns[1].Stations[0].X, 1e-9);

        // Fixed-fixed: M = −30, +15, −30; zero crossings at L/2 ± L/(2√3), interpolated between stations.
        var ff = Beam(6, SupportType.Fixed, SupportType.Fixed, -10, out _);
        var m = FramePlot.Diagram(ff, FrameSolver.Analyze(ff).Case("D"), DiagramQuantity.Moment, 0.01).Single();
        var mRuns = FramePlot.SignRuns(m.Stations);
        Assert.Equal([-1, 1, -1], mRuns.Select(r => r.Sign));
        var c = 3 / Math.Sqrt(3);
        Near(3 - c, mRuns[0].Stations[^1].X, 0.02, "first zero (linear interpolation of a parabola)");
        Near(3 + c, mRuns[2].Stations[0].X, 0.02, "second zero");
        Assert.All(mRuns, r => Assert.All(r.Stations, s => Assert.True(s.Value * r.Sign >= -1e-9)));
    }

    [Fact]
    public void DeformedShapePassesThroughTheMagnifiedNodeDisplacements()
    {
        var model = Empty();
        var a = model.AddNode(0, 0, SupportType.Fixed);
        var b = model.AddNode(6, 0, SupportType.Fixed);
        var c = model.AddNode(0, 4);
        var d = model.AddNode(6, 4);
        model.AddMember(a.Id, c.Id);
        model.AddMember(d.Id, b.Id); // drawn downwards on purpose
        model.AddMember(c.Id, d.Id);
        var w = model.AddLoadCase("W", LoadCategory.W);
        model.AddNodalLoad(c.Id, w.Id, 10, 0, 0);
        model.AddMemberLoad(3, w.Id, 0, -6);
        var set = FrameSolver.Analyze(model).Case("W");

        const double mag = 100;
        var shapes = FramePlot.Deformed(model, set, mag);
        Assert.Equal(3, shapes.Count);
        foreach (var (memberId, pts) in shapes)
        {
            var member = model.FindMember(memberId)!;
            foreach (var (nodeId, p) in new[] { (member.NodeI, pts[0]), (member.NodeJ, pts[^1]) })
            {
                var node = model.FindNode(nodeId)!;
                var r = set.Node(nodeId);
                Near(node.X + mag * r.Ux / 1000, p.X, 1e-9, $"member {memberId}, node {nodeId} X");
                Near(node.Y + mag * r.Uy / 1000, p.Y, 1e-9, $"member {memberId}, node {nodeId} Y");
            }
        }
        Assert.True(FramePlot.MaxDisplacement(set) >= Math.Abs(set.Node(c.Id).Ux));
    }

    [Fact]
    public void BrokenModelsAreDrawnAsFarAsTheyGo()
    {
        var model = Beam(4, SupportType.Fixed, SupportType.Free, -5, out _);
        model.Members.Add(new Member { Id = 9, NodeI = 1, NodeJ = 42 }); // missing node
        model.Members.Add(new Member { Id = 10, NodeI = 1, NodeJ = 1 });  // zero length
        Assert.Single(FramePlot.Members(model));
        Assert.Null(FramePlot.Bounds(new FrameModel()));
    }

    [Fact]
    public void LoadsOfACombinationAreFactoredAndSummed()
    {
        var model = Beam(6, SupportType.Pin, SupportType.RollerVertical, -5, out var d);
        d.IncludeSelfWeight = true;
        var l = model.AddLoadCase("L", LoadCategory.L);
        model.AddMemberLoad(1, l.Id, 0, -8);
        model.AddMemberLoad(1, l.Id, 1, -2);
        model.AddNodalLoad(2, l.Id, 3, 0, 0);
        var combo = new LoadCombination { Name = "ULS", Factors = { [d.Id] = 1.25, [l.Id] = 1.5 } };

        var loads = FramePlot.Loads(model, combo.FactorOf);
        var q = loads.Member.Single();
        Near(1.5, q.Qx, 1e-12);
        Near(1.25 * -5 + 1.5 * -10, q.Qy, 1e-12);
        Near(4.5, loads.Nodal.Single().Fx, 1e-12);
        Near(1.25 * DeadLoad, loads.SelfWeight, 1e-12);

        var onlyL = FramePlot.Loads(model, id => id == l.Id ? 1 : 0);
        Near(0, onlyL.SelfWeight, 0);
    }

    [Theory]
    [InlineData(10.0, 0.5, 50)]   // raw 50
    [InlineData(3.0, 0.5, 100)]   // raw 166.7 → 100
    [InlineData(0.04, 0.5, 10000)] // raw 12 500 → 10 000
    [InlineData(1.0, 0.3, 200)]   // raw 300 → 200
    public void MagnificationIsRounded(double maxMm, double target, double expected) =>
        Near(expected, FramePlot.NiceMagnification(maxMm, target), 1e-9);
}
