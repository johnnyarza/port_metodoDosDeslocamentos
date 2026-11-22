using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Model;
using static FrameAnalysis.Tests.TestFrames;

namespace FrameAnalysis.Tests;

/// <summary>Closed-form beam results (guide §8, Case A and textbook cases).</summary>
public class BeamTests
{
    private const double W = 10;   // kN/m, downward
    private const double L = 6;    // m

    [Fact]
    public void CaseA_FixedFixedBeam_EndMomentsShearsMidSpanAndDeflection()
    {
        var model = Beam(L, SupportType.Fixed, SupportType.Fixed, -W, out _);
        var r = FrameSolver.Analyze(model).Case("D");
        var m = r.Members[0];

        // wL²/12 = 30 kN·m at the ends (hogging), wL²/24 = 15 at mid-span (sagging), end shears wL/2 = 30.
        Near(-30, m.ForcesAt(0).M, 1e-9, "M(0)");
        Near(-30, m.ForcesAt(L).M, 1e-9, "M(L)");
        Near(15, m.ForcesAt(L / 2).M, 1e-9, "M(L/2)");
        Near(30, m.ForcesAt(0).V, 1e-9, "V(0)");
        Near(-30, m.ForcesAt(L).V, 1e-9, "V(L)");
        Near(0, m.ForcesAt(L / 2).V, 1e-9, "V(L/2)");

        // Member-end actions (Gere & Weaver): upward 30 at both ends, +30 CCW at I, −30 at J.
        var f = m.EndForces;
        Near(30, f.VI, 1e-9); Near(30, f.MI, 1e-9); Near(30, f.VJ, 1e-9); Near(-30, f.MJ, 1e-9);

        // Mid-span deflection wL⁴/(384EI), downward.
        var delta = W * Math.Pow(L * 1000, 4) / (384 * EIx);
        Near(-delta, m.DisplacementsAt(L / 2).V, 1e-9, "v(L/2)");
        Near(0, m.DisplacementsAt(L / 2).Rotation, 1e-12);

        // Reactions: 30 up at each end, +30 (CCW) at the left support, −30 at the right.
        Near(30, r.Nodes[0].Ry!.Value, 1e-9); Near(30, r.Nodes[0].Mz!.Value, 1e-9);
        Near(30, r.Nodes[1].Ry!.Value, 1e-9); Near(-30, r.Nodes[1].Mz!.Value, 1e-9);
    }

    [Fact]
    public void CaseA_SplitIntoTwoMembers_GivesTheSameAnswerThroughTheSolve()
    {
        // With a free mid node the deflection comes out of the Cholesky solve instead of the fixed-end terms.
        var model = Empty();
        var a = model.AddNode(0, 0, SupportType.Fixed);
        var mid = model.AddNode(L / 2, 0);
        var b = model.AddNode(L, 0, SupportType.Fixed);
        var m1 = model.AddMember(a.Id, mid.Id);
        var m2 = model.AddMember(mid.Id, b.Id);
        var d = model.AddLoadCase("D", LoadCategory.D);
        model.AddMemberLoad(m1.Id, d.Id, 0, -W);
        model.AddMemberLoad(m2.Id, d.Id, 0, -W);

        var r = FrameSolver.Analyze(model).Case("D");
        var delta = W * Math.Pow(L * 1000, 4) / (384 * EIx);
        Near(-delta, r.Node(mid.Id).Uy, 1e-9, "mid node deflection");
        Near(0, r.Node(mid.Id).Rz, 1e-12);
        Near(15, r.Member(m1.Id).ForcesAt(L / 2).M, 1e-8);
        Near(-30, r.Member(m1.Id).ForcesAt(0).M, 1e-8);
        Near(30, r.Node(a.Id).Mz!.Value, 1e-8);
    }

    [Fact]
    public void SimplySupportedBeam_MomentDeflectionReactionsAndRotations()
    {
        var model = Beam(L, SupportType.Pin, SupportType.RollerVertical, -W, out _);
        var r = FrameSolver.Analyze(model).Case("D");
        var m = r.Members[0];

        Near(45, m.ForcesAt(L / 2).M, 1e-9, "wL²/8");
        Near(0, m.ForcesAt(0).M, 1e-9);
        Near(0, m.ForcesAt(L).M, 1e-9);
        Near(30, r.Nodes[0].Ry!.Value, 1e-9);
        Near(30, r.Nodes[1].Ry!.Value, 1e-9);
        Near(0, r.Nodes[0].Rx!.Value, 1e-9);
        Assert.Null(r.Nodes[1].Rx);
        Assert.Null(r.Nodes[0].Mz);

        var delta = 5 * W * Math.Pow(L * 1000, 4) / (384 * EIx);
        Near(-delta, m.DisplacementsAt(L / 2).V, 1e-9, "5wL⁴/384EI");
        var theta = W * Math.Pow(L * 1000, 3) / (24 * EIx);
        Near(-theta, r.Nodes[0].Rz, 1e-12, "θ left");
        Near(theta, r.Nodes[1].Rz, 1e-12, "θ right");

        var (max, x) = m.MaxAbsMoment;
        Near(45, max, 1e-9);
        Near(L / 2, x, 1e-9);
    }

    [Fact]
    public void Cantilever_TipLoad_DeflectionRotationAndFixedEndReactions()
    {
        const double p = 10, len = 4;
        var model = Empty();
        var a = model.AddNode(0, 0, SupportType.Fixed);
        var b = model.AddNode(len, 0);
        model.AddMember(a.Id, b.Id);
        var d = model.AddLoadCase("D", LoadCategory.D);
        model.AddNodalLoad(b.Id, d.Id, 0, -p, 0);

        var r = FrameSolver.Analyze(model).Case("D");
        var lmm = len * 1000;
        Near(-p * 1000 * lmm * lmm * lmm / (3 * EIx), r.Node(b.Id).Uy, 1e-9, "PL³/3EI");
        Near(-p * 1000 * lmm * lmm / (2 * EIx), r.Node(b.Id).Rz, 1e-12, "PL²/2EI");
        Near(p, r.Node(a.Id).Ry!.Value, 1e-9);
        Near(p * len, r.Node(a.Id).Mz!.Value, 1e-9);
        Near(-p * len, r.Members[0].ForcesAt(0).M, 1e-9, "hogging at the support");
        Near(p, r.Members[0].ForcesAt(1).V, 1e-9, "V = dM/dx");
    }

    [Fact]
    public void MinorAxisMember_UsesIy()
    {
        var major = Beam(L, SupportType.Pin, SupportType.RollerVertical, -W, out _);
        var minor = Beam(L, SupportType.Pin, SupportType.RollerVertical, -W, out _);
        minor.Members[0].Axis = BendingAxis.Minor;

        var vMajor = FrameSolver.Analyze(major).Cases[0].Members[0].DisplacementsAt(L / 2).V;
        var vMinor = FrameSolver.Analyze(minor).Cases[0].Members[0].DisplacementsAt(L / 2).V;
        Near(vMajor * Ix / Iy, vMinor, 1e-9);
    }

    [Fact]
    public void SeveralLoadsOnOneMember_AddUp()
    {
        // Bug 7 of the guide: the original kept only one distributed load per member.
        var one = Beam(L, SupportType.Pin, SupportType.RollerVertical, -W, out _);
        var two = Beam(L, SupportType.Pin, SupportType.RollerVertical, -W / 2, out var d);
        two.AddMemberLoad(two.Members[0].Id, d.Id, 0, -W / 2);

        var a = FrameSolver.Analyze(one).Cases[0];
        var b = FrameSolver.Analyze(two).Cases[0];
        Near(a.Members[0].ForcesAt(L / 2).M, b.Members[0].ForcesAt(L / 2).M, 1e-9);
        Near(a.Members[0].DisplacementsAt(L / 2).V, b.Members[0].DisplacementsAt(L / 2).V, 1e-9);
        Near(a.Nodes[0].Ry!.Value, b.Nodes[0].Ry!.Value, 1e-9);
    }

    [Fact]
    public void SelfWeight_IsAddedToItsCase_AndCanBeTurnedOff()
    {
        var model = Beam(L, SupportType.Pin, SupportType.RollerVertical, 0, out var d);
        d.IncludeSelfWeight = true;
        var with = FrameSolver.Analyze(model).Cases[0];
        Near(DeadLoad * L / 2, with.Nodes[0].Ry!.Value, 1e-9);
        Near(DeadLoad * L * L / 8, with.Members[0].ForcesAt(L / 2).M, 1e-9);

        d.IncludeSelfWeight = false;
        var without = FrameSolver.Analyze(model).Cases[0];
        Near(0, without.Nodes[0].Ry!.Value, 1e-12);
    }

    [Fact]
    public void BeamOnTwoRollers_IsAMechanism()
    {
        var model = Beam(L, SupportType.RollerVertical, SupportType.RollerVertical, -W, out _);
        var ex = Assert.Throws<UnstableStructureException>(() => FrameSolver.Analyze(model));
        Assert.Contains("horizontally", ex.Message);
    }

    [Fact]
    public void InvalidModel_ThrowsWithAllProblems()
    {
        var model = Beam(L, SupportType.Free, SupportType.Free, -W, out _);
        model.Material.E = 0;
        var ex = Assert.Throws<ModelValidationException>(() => FrameSolver.Analyze(model));
        Assert.Contains(ex.Errors, e => e.Contains("support"));
        Assert.Contains(ex.Errors, e => e.Contains("E must be"));
    }
}
