using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Model;
using static FrameAnalysis.Tests.TestFrames;

namespace FrameAnalysis.Tests;

public class FrameTests
{
    /// <summary>
    /// Thesis Example 1 portal frame (guide §8, Case B): columns 20×30 cm, beam 20×40 cm, E = 30 000 MPa, 12 kN/m on the
    /// beam. Expected values are from the independent numpy run in the guide. The frame uses two sections, so the
    /// member properties are passed in instead of coming from the task section.
    /// </summary>
    private static (FrameModel Model, Func<Member, MemberProperties> Props) CaseB()
    {
        var m = Empty();
        m.Material.E = 30_000;
        m.AddNode(0, 0, SupportType.Fixed);
        m.AddNode(4, 0, SupportType.Fixed);
        m.AddNode(0, 3);
        m.AddNode(4, 3);
        m.AddMember(1, 3);
        m.AddMember(2, 4);
        var beam = m.AddMember(3, 4);
        var d = m.AddLoadCase("D", LoadCategory.D);
        m.AddMemberLoad(beam.Id, d.Id, 0, -12);
        var column = new MemberProperties(Area: 60_000, I: 200 * 300.0 * 300 * 300 / 12 / 1e6, E: 30_000, SelfWeight: 0);
        var girder = new MemberProperties(Area: 80_000, I: 200 * 400.0 * 400 * 400 / 12 / 1e6, E: 30_000, SelfWeight: 0);
        return (m, mem => mem.Id == beam.Id ? girder : column);
    }

    [Fact]
    public void CaseB_ThesisPortalFrame_MatchesTheIndependentRun()
    {
        var (model, props) = CaseB();
        var r = FrameSolver.Analyze(model, props).Case("D");

        Near(4.2226, r.Node(1).Rx!.Value, 1e-3, "Rx node 1");
        Near(24.000, r.Node(1).Ry!.Value, 1e-3, "Ry node 1");
        Near(-4.2120, r.Node(1).Mz!.Value, 1e-3, "Mz node 1");
        Near(-4.2226, r.Node(2).Rx!.Value, 1e-3, "Rx node 2");
        Near(24.000, r.Node(2).Ry!.Value, 1e-3, "Ry node 2");
        Near(4.2120, r.Node(2).Mz!.Value, 1e-3, "Mz node 2");

        // Node 3: the guide lists u = 4.2e−6 m, but that disagrees with its own beam force: by symmetry the top nodes
        // move by half the beam's shortening, u = N·L/(2EA) = 4222.6·4000/(2·30000·80000) = 0.00352 mm. Checked that way.
        Near(4222.6 * 4000 / (2 * 30_000.0 * 80_000), r.Node(3).Ux, 1e-6, "u node 3 = NL/2EA");
        Near(-r.Node(3).Ux, r.Node(4).Ux, 1e-12, "symmetry");
        Near(-0.040, r.Node(3).Uy, 0.0006, "v node 3");
        Near(-4.72e-4, r.Node(3).Rz, 0.006e-4, "θ node 3");

        var beam = r.Member(3);
        Near(8.4557, beam.EndForces.MI, 1e-3, "beam M_J");
        Near(-8.4557, beam.EndForces.MJ, 1e-3, "beam M_K");
        Near(15.544, beam.ForcesAt(2).M, 1e-3, "beam mid-span moment");
        Near(-4.2226, beam.ForcesAt(2).N, 1e-3, "beam axial (compression)");
        Near(8.4557, Math.Abs(r.Member(1).EndForces.MJ), 1e-3, "column top moment");

        // Global equilibrium: vertical reactions carry the 48 kN.
        Near(48, r.Node(1).Ry!.Value + r.Node(2).Ry!.Value, 1e-9);
        Near(0, r.Node(1).Rx!.Value + r.Node(2).Rx!.Value, 1e-9);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(-47)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(211.3)]
    public void RotatedFrame_GivesTheSameLocalResults(double degrees)
    {
        // Bugs 3–5 of the guide: the original only handled horizontal and vertical members (and exact ±1 cosines).
        // Rotating the whole frame and its loads must not change anything measured in member axes.
        var (model, props) = CaseB();
        var d = model.LoadCases[0];
        model.AddNodalLoad(3, d.Id, 5, -2, 3);
        model.AddMemberLoad(1, d.Id, 1.5, 0.7);
        var rotated = Rotate(model, degrees);

        var a = FrameSolver.Analyze(model, props).Cases[0];
        var b = FrameSolver.Analyze(rotated, props).Cases[0];
        foreach (var ma in a.Members)
        {
            var mb = b.Member(ma.MemberId);
            foreach (var x in new[] { 0, 0.3, ma.Length / 2, ma.Length * 0.8, ma.Length })
            {
                var fa = ma.ForcesAt(x);
                var fb = mb.ForcesAt(x);
                Near(fa.N, fb.N, 1e-8, $"N m{ma.MemberId} x={x}");
                Near(fa.V, fb.V, 1e-8, $"V m{ma.MemberId} x={x}");
                Near(fa.M, fb.M, 1e-8, $"M m{ma.MemberId} x={x}");
                var da = ma.DisplacementsAt(x);
                var db = mb.DisplacementsAt(x);
                Near(da.U, db.U, 1e-9); Near(da.V, db.V, 1e-9); Near(da.Rotation, db.Rotation, 1e-12);
            }
        }
        // Reactions turn with the frame; the support moments do not change.
        var t = degrees * Math.PI / 180;
        foreach (var na in a.Nodes.Where(n => n.Rx is not null))
        {
            var nb = b.Node(na.NodeId);
            Near(na.Rx!.Value * Math.Cos(t) - na.Ry!.Value * Math.Sin(t), nb.Rx!.Value, 1e-8);
            Near(na.Rx!.Value * Math.Sin(t) + na.Ry!.Value * Math.Cos(t), nb.Ry!.Value, 1e-8);
            Near(na.Mz!.Value, nb.Mz!.Value, 1e-8);
        }
    }

    private static FrameModel Rotate(FrameModel m, double degrees)
    {
        var t = degrees * Math.PI / 180;
        (double, double) Turn(double x, double y) => (x * Math.Cos(t) - y * Math.Sin(t), x * Math.Sin(t) + y * Math.Cos(t));
        var r = Empty();
        r.Material.E = m.Material.E;
        foreach (var n in m.Nodes)
        {
            var (x, y) = Turn(n.X, n.Y);
            r.Nodes.Add(new Node { Id = n.Id, X = x, Y = y, Support = n.Support });
        }
        r.Members.AddRange(m.Members.Select(x => new Member { Id = x.Id, NodeI = x.NodeI, NodeJ = x.NodeJ, Axis = x.Axis }));
        r.LoadCases.AddRange(m.LoadCases.Select(c => new LoadCase { Id = c.Id, Name = c.Name, Category = c.Category }));
        foreach (var l in m.NodalLoads)
        {
            var (fx, fy) = Turn(l.Fx, l.Fy);
            r.NodalLoads.Add(new NodalLoad { NodeId = l.NodeId, LoadCaseId = l.LoadCaseId, Fx = fx, Fy = fy, Mz = l.Mz });
        }
        foreach (var l in m.MemberLoads)
        {
            var (qx, qy) = Turn(l.Qx, l.Qy);
            r.MemberLoads.Add(new MemberLoad { MemberId = l.MemberId, LoadCaseId = l.LoadCaseId, Qx = qx, Qy = qy });
        }
        return r;
    }

    [Fact]
    public void InclinedSimplySupportedRafter_GravityPerMemberLength()
    {
        // 30° rafter, 5 m long, pin at the bottom, vertical-reaction roller at the top, gravity 4 kN per m of rafter.
        const double len = 5, w = 4;
        var t = 30 * Math.PI / 180;
        var model = Empty();
        var a = model.AddNode(0, 0, SupportType.Pin);
        var b = model.AddNode(len * Math.Cos(t), len * Math.Sin(t), SupportType.RollerVertical);
        var m = model.AddMember(a.Id, b.Id);
        var d = model.AddLoadCase("D", LoadCategory.D);
        model.AddMemberLoad(m.Id, d.Id, 0, -w);

        var r = FrameSolver.Analyze(model).Cases[0];
        var member = r.Members[0];
        Near(w * Math.Cos(t) * len * len / 8, member.MaxAbsMoment.Value, 1e-9, "w·cosα·L²/8");
        Near(len / 2, member.MaxAbsMoment.X, 1e-9);
        Near(w * len, r.Node(a.Id).Ry!.Value + r.Node(b.Id).Ry!.Value, 1e-9, "total vertical load");
        Near(0, r.Node(a.Id).Rx!.Value, 1e-9, "no horizontal reaction with a vertical roller");
        // The vertical roller reaction wL/2 has a component wL/2·sinα along the rafter, pulling the top end up-slope,
        // so N runs from −wL·sinα/2 (compression at the pin) to +wL·sinα/2 (tension at the roller).
        Near(-w * Math.Sin(t) * len / 2, member.ForcesAt(0).N, 1e-9);
        Near(w * Math.Sin(t) * len / 2, member.ForcesAt(len).N, 1e-9);
        Near(0, member.ForcesAt(len / 2).N, 1e-9);
    }

    [Fact]
    public void AxialDistributedLoad_OnAColumn()
    {
        // Bugs 1–2 of the guide: the axial part of a member load read the wrong table and used an unset index.
        const double h = 4, w = 5;
        var model = Empty();
        var a = model.AddNode(0, 0, SupportType.Fixed);
        var b = model.AddNode(0, h);
        var m = model.AddMember(a.Id, b.Id);
        var d = model.AddLoadCase("D", LoadCategory.D);
        model.AddMemberLoad(m.Id, d.Id, 0, -w); // along the column axis

        var r = FrameSolver.Analyze(model).Cases[0];
        Near(w * h, r.Node(a.Id).Ry!.Value, 1e-9);
        Near(-w * h, r.Members[0].ForcesAt(0).N, 1e-9, "compression at the base");
        Near(-w * h / 2, r.Members[0].ForcesAt(h / 2).N, 1e-9);
        Near(0, r.Members[0].ForcesAt(h).N, 1e-9);
        var top = -w * Math.Pow(h * 1000, 2) / (2 * E * Area);
        Near(top, r.Node(b.Id).Uy, 1e-12, "−wL²/2EA");
        Near(0, r.Members[0].ForcesAt(h / 2).M, 1e-9);
    }

    [Fact]
    public void AxialDistributedLoad_OnAHorizontalCantilever()
    {
        const double len = 4, w = 5;
        var model = Empty();
        var a = model.AddNode(0, 0, SupportType.Fixed);
        var b = model.AddNode(len, 0);
        var m = model.AddMember(a.Id, b.Id);
        var d = model.AddLoadCase("D", LoadCategory.D);
        model.AddMemberLoad(m.Id, d.Id, w, 0);

        var r = FrameSolver.Analyze(model).Cases[0];
        Near(-w * len, r.Node(a.Id).Rx!.Value, 1e-9);
        Near(w * len, r.Members[0].ForcesAt(0).N, 1e-9, "tension at the support");
        Near(w * Math.Pow(len * 1000, 2) / (2 * E * Area), r.Node(b.Id).Ux, 1e-12);
        Near(w * Math.Pow(len * 1000, 2) * 3 / 8 / (E * Area), r.Members[0].DisplacementsAt(len / 2).U, 1e-12, "u(L/2)");
    }

    [Fact]
    public void Combination_EqualsSolvingTheFactoredLoads()
    {
        var model = Empty();
        model.AddNode(0, 0, SupportType.Fixed);
        model.AddNode(5, 0, SupportType.Pin);
        model.AddNode(0, 4);
        model.AddNode(5, 4);
        model.AddMember(1, 3);
        model.AddMember(2, 4);
        model.AddMember(3, 4);
        var d = model.AddLoadCase("D", LoadCategory.D, includeSelfWeight: true);
        var l = model.AddLoadCase("L", LoadCategory.L);
        var wind = model.AddLoadCase("W", LoadCategory.W);
        model.AddMemberLoad(3, d.Id, 0, -6);
        model.AddMemberLoad(3, l.Id, 0, -9);
        model.AddNodalLoad(3, wind.Id, 7, 0, 0);
        model.Combinations.Add(new LoadCombination { Name = "C", LimitState = LimitState.Uls, Factors = { [d.Id] = 1.25, [l.Id] = 1.5, [wind.Id] = 0.4 } });

        var combined = FrameSolver.Analyze(model).Combination("C");

        var direct = Empty();
        direct.Nodes.AddRange(model.Nodes);
        direct.Members.AddRange(model.Members);
        var only = direct.AddLoadCase("All", LoadCategory.D);
        foreach (var mem in direct.Members)
            direct.AddMemberLoad(mem.Id, only.Id, 0, -1.25 * DeadLoad);
        direct.AddMemberLoad(3, only.Id, 0, -1.25 * 6 - 1.5 * 9);
        direct.AddNodalLoad(3, only.Id, 0.4 * 7, 0, 0);
        var expected = FrameSolver.Analyze(direct).Cases[0];

        foreach (var n in expected.Nodes)
        {
            var c = combined.Node(n.NodeId);
            Near(n.Ux, c.Ux, 1e-9); Near(n.Uy, c.Uy, 1e-9); Near(n.Rz, c.Rz, 1e-12);
            Assert.Equal(n.Rx.HasValue, c.Rx.HasValue);
            if (n.Rx.HasValue) Near(n.Rx!.Value, c.Rx!.Value, 1e-8);
            if (n.Mz.HasValue) Near(n.Mz!.Value, c.Mz!.Value, 1e-8);
        }
        foreach (var m in expected.Members)
            foreach (var x in new[] { 0, m.Length / 3, m.Length })
            {
                Near(m.ForcesAt(x).M, combined.Member(m.MemberId).ForcesAt(x).M, 1e-8);
                Near(m.ForcesAt(x).N, combined.Member(m.MemberId).ForcesAt(x).N, 1e-8);
                Near(m.DisplacementsAt(x).V, combined.Member(m.MemberId).DisplacementsAt(x).V, 1e-9);
            }
        Assert.Equal(ResultKind.Uls, combined.Kind);
    }

    [Fact]
    public void NonContiguousIds_Work()
    {
        // The original needed node and member numbers 1..N with no gaps.
        var model = Empty();
        var a = model.AddNode(0, 0, SupportType.Pin);
        var gone = model.AddNode(1, 1);
        var b = model.AddNode(6, 0, SupportType.RollerVertical);
        var scrap = model.AddMember(a.Id, gone.Id);
        var m = model.AddMember(b.Id, a.Id); // drawn right to left
        model.RemoveMember(scrap.Id);
        model.RemoveNode(gone.Id);
        var d = model.AddLoadCase("D", LoadCategory.D);
        model.AddMemberLoad(m.Id, d.Id, 0, -10);

        Assert.Equal(3, b.Id);
        Assert.Equal(2, m.Id);
        var r = FrameSolver.Analyze(model).Cases[0];
        Near(30, r.Node(a.Id).Ry!.Value, 1e-9);
        Near(30, r.Node(b.Id).Ry!.Value, 1e-9);
        // Drawn right to left, local y points down, so the same sagging is a negative moment in member axes.
        // Loads are global, so the load itself does not flip (it did on horizontal members in the original).
        Near(-45, r.Member(m.Id).ForcesAt(3).M, 1e-9);
        Near(45, S16DesignForces.From(r, m.Id).Mf, 1e-9);
    }
}
