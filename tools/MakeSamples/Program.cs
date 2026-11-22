using System.Globalization;
using System.Text;
using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Data;
using FrameAnalysis.Core.Model;

// Writes the validation frames (samples/validation/*.frame.json) and, for each, a sheet with the model's inputs and
// this solver's results, to compare against another program.
// Run from the repository root:  dotnet run --project tools/MakeSamples
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("samples", "validation"));
Directory.CreateDirectory(outDir);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

foreach (var (file, title, notes, model) in Samples.All())
{
    var problems = model.Validate();
    if (problems.Count > 0) throw new InvalidOperationException($"{file}: {string.Join("; ", problems)}");
    File.WriteAllText(Path.Combine(outDir, file + ".frame.json"), FrameModelJson.Serialize(model));
    File.WriteAllText(Path.Combine(outDir, file + ".results.md"), Sheet.Write(title, notes, model, FrameSolver.Analyze(model)));
    Console.WriteLine($"wrote {file}");
}

/// <summary>The validation frames. Same section and steel everywhere (W250x49, E = 200 000 MPa); self-weight off except in sample 11.</summary>
internal static class Samples
{
    public static IEnumerable<(string File, string Title, string Notes, FrameModel Model)> All()
    {
        yield return SimplySupported();
        yield return Cantilever();
        yield return ProppedCantilever();
        yield return ContinuousBeam();
        yield return Portal("05-portal-fixed-bases", "Portal frame, fixed bases", SupportType.Fixed);
        yield return Portal("06-portal-pinned-bases", "Portal frame, pinned bases", SupportType.Pin);
        yield return Gable();
        yield return TwoStorey();
        yield return BracedPortal();
        yield return InclinedBeam();
        yield return SelfWeight();
        yield return MidSpanPointLoad();
    }

    private static FrameModel New() => new()
    {
        Section = new FrameSection { Designation = "W250x49", Area = 6260, Ix = 70.6, Iy = 15.1, DeadLoad = 0.481 },
        Material = new FrameMaterial { Name = "350W", E = 200_000 },
    };

    private static void Combo(FrameModel m, string name, LimitState ls, params (string Case, double Factor)[] parts)
    {
        var c = new LoadCombination { Name = name, LimitState = ls };
        foreach (var lc in m.LoadCases) c.Factors[lc.Id] = 0;
        foreach (var (cs, f) in parts) c.Factors[m.LoadCases.Single(x => x.Name == cs).Id] = f;
        m.Combinations.Add(c);
    }

    private static (string, string, string, FrameModel) SimplySupported()
    {
        var m = New();
        var a = m.AddNode(0, 0, SupportType.Pin);
        var b = m.AddNode(6, 0, SupportType.RollerVertical);
        var beam = m.AddMember(a.Id, b.Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        m.AddMemberLoad(beam.Id, d.Id, 0, -10);
        return ("01-simply-supported-beam", "Simply supported beam, uniform load",
            "Closed form: R = wL/2 = 30 kN, M(L/2) = wL²/8 = 45 kN·m, mid-span deflection 5wL⁴/384EI = 11.951 mm," +
            "end rotation wL³/24EI = 0.006374 rad.", m);
    }

    private static (string, string, string, FrameModel) Cantilever()
    {
        var m = New();
        var a = m.AddNode(0, 0, SupportType.Fixed);
        var b = m.AddNode(4, 0);
        var beam = m.AddMember(a.Id, b.Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        var l = m.AddLoadCase("L", LoadCategory.L);
        m.AddNodalLoad(b.Id, d.Id, 0, -20, 10);
        m.AddMemberLoad(beam.Id, l.Id, 0, -5);
        Combo(m, "ULS 1.25D + 1.5L", LimitState.Uls, ("D", 1.25), ("L", 1.5));
        return ("02-cantilever", "Cantilever with tip force, tip moment and uniform load",
            "D: tip force P = 20 kN down and a counter-clockwise tip moment of 10 kN·m. L: 5 kN/m down. " +
            "Closed form (D): fixed-end moment −PL + M₀ = −70 kN·m; tip deflection −PL³/3EI + M₀L²/2EI = −30.217 + 5.666 = " +
            "−24.551 mm. " +
            "Closed form (L): fixed-end moment −wL²/2 = −40 kN·m; tip deflection −wL⁴/8EI = −11.331 mm.", m);
    }

    private static (string, string, string, FrameModel) ProppedCantilever()
    {
        var m = New();
        var a = m.AddNode(0, 0, SupportType.Fixed);
        var b = m.AddNode(8, 0, SupportType.RollerVertical);
        var beam = m.AddMember(a.Id, b.Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        m.AddMemberLoad(beam.Id, d.Id, 0, -12);
        return ("03-propped-cantilever", "Propped cantilever, uniform load",
            "Closed form: R at the roller = 3wL/8 = 36 kN, at the fixed end 5wL/8 = 60 kN; fixed-end moment wL²/8 = 96 kN·m " +
            "(hogging); largest sagging moment 9wL²/128 = 54 kN·m at 3L/8 = 3 m from the roller (x = 5 m).", m);
    }

    private static (string, string, string, FrameModel) ContinuousBeam()
    {
        var m = New();
        var n1 = m.AddNode(0, 0, SupportType.Pin);
        var n2 = m.AddNode(6, 0, SupportType.RollerVertical);
        var n3 = m.AddNode(14, 0, SupportType.RollerVertical);
        var n4 = m.AddNode(20, 0, SupportType.RollerVertical);
        var s1 = m.AddMember(n1.Id, n2.Id);
        var s2 = m.AddMember(n3.Id, n2.Id); // drawn right to left on purpose (see the notes)
        var s3 = m.AddMember(n3.Id, n4.Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        var l = m.AddLoadCase("L", LoadCategory.L);
        foreach (var s in new[] { s1, s2, s3 }) m.AddMemberLoad(s.Id, d.Id, 0, -8);
        m.AddMemberLoad(s2.Id, l.Id, 0, -12);
        Combo(m, "ULS 1.4D", LimitState.Uls, ("D", 1.4));
        Combo(m, "ULS 1.25D + 1.5L", LimitState.Uls, ("D", 1.25), ("L", 1.5));
        Combo(m, "SLS D + L", LimitState.Sls, ("D", 1), ("L", 1));
        return ("04-continuous-beam", "Continuous beam, three spans (6, 8, 6 m), pattern live load",
            "D: 8 kN/m on every span. L: 12 kN/m on the middle span only. Member 2 runs from node 3 to node 2 (right to left), " +
            "so in this sheet its moments have the opposite sign to the other spans (sagging is negative in its member axes) and " +
            "its x is measured from node 3. Your program will probably show the usual signs.", m);
    }

    private static (string, string, string, FrameModel) Portal(string file, string title, SupportType bases)
    {
        var m = New();
        var a = m.AddNode(0, 0, bases);
        var b = m.AddNode(6, 0, bases);
        var c = m.AddNode(0, 4);
        var e = m.AddNode(6, 4);
        m.AddMember(a.Id, c.Id);
        m.AddMember(b.Id, e.Id);
        var beam = m.AddMember(c.Id, e.Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        var l = m.AddLoadCase("L", LoadCategory.L);
        var w = m.AddLoadCase("W", LoadCategory.W);
        m.AddMemberLoad(beam.Id, d.Id, 0, -15);
        m.AddMemberLoad(beam.Id, l.Id, 0, -10);
        m.AddNodalLoad(c.Id, w.Id, 12, 0, 0);
        Combo(m, "ULS 1.25D + 1.5L", LimitState.Uls, ("D", 1.25), ("L", 1.5));
        Combo(m, "ULS 1.25D + 1.4W + 0.5L", LimitState.Uls, ("D", 1.25), ("W", 1.4), ("L", 0.5));
        Combo(m, "ULS 0.9D + 1.4W", LimitState.Uls, ("D", 0.9), ("W", 1.4));
        Combo(m, "SLS D + L", LimitState.Sls, ("D", 1), ("L", 1));
        return (file, title,
            "6 m wide, 4 m high. D: 15 kN/m on the beam. L: 10 kN/m on the beam. W: 12 kN to the right at the top of the left column.", m);
    }

    private static (string, string, string, FrameModel) Gable()
    {
        var m = New();
        var n1 = m.AddNode(0, 0, SupportType.Fixed);
        var n2 = m.AddNode(12, 0, SupportType.Fixed);
        var n3 = m.AddNode(0, 5);
        var n4 = m.AddNode(12, 5);
        var n5 = m.AddNode(6, 7);
        var colL = m.AddMember(n1.Id, n3.Id);
        var colR = m.AddMember(n2.Id, n4.Id);
        var rafL = m.AddMember(n3.Id, n5.Id);
        var rafR = m.AddMember(n5.Id, n4.Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        var s = m.AddLoadCase("S", LoadCategory.S);
        var w = m.AddLoadCase("W", LoadCategory.W);
        foreach (var r in new[] { rafL, rafR })
        {
            m.AddMemberLoad(r.Id, d.Id, 0, -4);
            m.AddMemberLoad(r.Id, s.Id, 0, -6);
            m.AddMemberLoad(r.Id, w.Id, 0, 1.2);
        }
        m.AddMemberLoad(colL.Id, w.Id, 3, 0);
        m.AddMemberLoad(colR.Id, w.Id, 1.5, 0);
        Combo(m, "ULS 1.25D + 1.5S", LimitState.Uls, ("D", 1.25), ("S", 1.5));
        Combo(m, "ULS 1.25D + 1.4W + 0.5S", LimitState.Uls, ("D", 1.25), ("W", 1.4), ("S", 0.5));
        Combo(m, "ULS 0.9D + 1.4W", LimitState.Uls, ("D", 0.9), ("W", 1.4));
        return ("07-gable-frame", "Gable frame, fixed bases (12 m span, 5 m eaves, 2 m rise)",
            "Rafters are 6.325 m long (slope 18.43°). All distributed loads are global and per metre of MEMBER LENGTH, " +
            "on the full member: D 4 kN/m down and S 6 kN/m down on both rafters; W 3 kN/m to the right on the left column, " +
            "1.5 kN/m to the right on the right column and 1.2 kN/m upward on both rafters. If your program takes loads on " +
            "inclined members per horizontal projection, enter the rafter loads × 6.325/6 (D 4.216, S 6.325, W uplift 1.265 kN/m). " +
            "Member 4 runs from the apex down to node 4.", m);
    }

    private static (string, string, string, FrameModel) TwoStorey()
    {
        var m = New();
        double[] xs = [0, 6, 14];
        double[] ys = [0, 4, 7.5];
        var node = new Dictionary<(int, int), Node>();
        for (var j = 0; j < ys.Length; j++)
            for (var i = 0; i < xs.Length; i++)
                node[(i, j)] = m.AddNode(xs[i], ys[j], j == 0 ? SupportType.Fixed : SupportType.Free);
        for (var j = 0; j < 2; j++)
            for (var i = 0; i < xs.Length; i++)
                m.AddMember(node[(i, j)].Id, node[(i, j + 1)].Id, i == 1 ? BendingAxis.Minor : BendingAxis.Major);
        var beams = new Dictionary<(int, int), Member>();
        for (var j = 1; j < ys.Length; j++)
            for (var i = 0; i < 2; i++)
                beams[(i, j)] = m.AddMember(node[(i, j)].Id, node[(i + 1, j)].Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        var l = m.AddLoadCase("L", LoadCategory.L);
        var w = m.AddLoadCase("W", LoadCategory.W);
        foreach (var ((_, j), b) in beams) m.AddMemberLoad(b.Id, d.Id, 0, j == 1 ? -20 : -10);
        m.AddMemberLoad(beams[(0, 1)].Id, l.Id, 0, -15);
        m.AddNodalLoad(node[(0, 1)].Id, w.Id, 15, 0, 0);
        m.AddNodalLoad(node[(0, 2)].Id, w.Id, 8, 0, 0);
        Combo(m, "ULS 1.25D + 1.5L", LimitState.Uls, ("D", 1.25), ("L", 1.5));
        Combo(m, "ULS 1.25D + 1.4W + 0.5L", LimitState.Uls, ("D", 1.25), ("W", 1.4), ("L", 0.5));
        Combo(m, "ULS 0.9D + 1.4W", LimitState.Uls, ("D", 0.9), ("W", 1.4));
        return ("08-two-storey-two-bay", "Two-storey, two-bay frame (bays 6 and 8 m, storeys 4 and 3.5 m), fixed bases",
            "The interior columns (members 2 and 5) bend about the MINOR axis (Iy = 15.1 × 10⁶ mm⁴); every other member about " +
            "the major axis. D: 20 kN/m on the floor beams, 10 kN/m on the roof beams. L: 15 kN/m on the left floor beam only. " +
            "W: 15 kN at the floor and 8 kN at the roof, to the right, on the left column line.", m);
    }

    private static (string, string, string, FrameModel) BracedPortal()
    {
        var m = New();
        var a = m.AddNode(0, 0, SupportType.Pin);
        var b = m.AddNode(6, 0, SupportType.Pin);
        var c = m.AddNode(0, 4);
        var e = m.AddNode(6, 4);
        m.AddMember(a.Id, c.Id);
        m.AddMember(b.Id, e.Id);
        var beam = m.AddMember(c.Id, e.Id);
        m.AddMember(a.Id, e.Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        var w = m.AddLoadCase("W", LoadCategory.W);
        m.AddMemberLoad(beam.Id, d.Id, 0, -10);
        m.AddNodalLoad(c.Id, w.Id, 20, 0, 0);
        Combo(m, "ULS 1.25D + 1.4W", LimitState.Uls, ("D", 1.25), ("W", 1.4));
        return ("09-braced-portal", "Portal with a diagonal, pinned bases, rigid joints",
            "Member 4 is a diagonal from node 1 (bottom left) to node 4 (top right), 7.211 m long, rigidly connected like every " +
            "joint here (no releases yet). D: 10 kN/m on the beam. W: 20 kN to the right at node 3.", m);
    }

    private static (string, string, string, FrameModel) InclinedBeam()
    {
        var m = New();
        var a = m.AddNode(0, 0, SupportType.Pin);
        var b = m.AddNode(4, 3, SupportType.RollerHorizontal);
        var beam = m.AddMember(a.Id, b.Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        var l = m.AddLoadCase("L", LoadCategory.L);
        m.AddMemberLoad(beam.Id, d.Id, 0, -10);
        m.AddNodalLoad(b.Id, l.Id, 0, -10, 0);
        Combo(m, "ULS 1.25D + 1.5L", LimitState.Uls, ("D", 1.25), ("L", 1.5));
        return ("10-inclined-beam-horizontal-roller", "Inclined member (3-4-5), pin at the bottom, horizontal-reaction roller at the top",
            "Member is 5 m long. The top roller only resists horizontally (it is free to slide vertically, like a beam leaning " +
            "on a wall). D: 10 kN/m down per metre of member length (50 kN in total; 12.5 kN/m per metre of horizontal " +
            "projection). L: 10 kN down at the top node. Closed form (D): Rx at the top = −50·2/3 = −33.333 kN, pin Rx = +33.333 kN, " +
            "pin Ry = 50 kN.", m);
    }

    private static (string, string, string, FrameModel) SelfWeight()
    {
        var m = New();
        var a = m.AddNode(0, 0, SupportType.Fixed);
        var b = m.AddNode(6, 0, SupportType.Fixed);
        var c = m.AddNode(0, 4);
        var e = m.AddNode(6, 4);
        m.AddMember(a.Id, c.Id);
        m.AddMember(b.Id, e.Id);
        m.AddMember(c.Id, e.Id);
        m.AddLoadCase("D", LoadCategory.D, includeSelfWeight: true);
        return ("11-self-weight-only", "Portal frame (sample 05 geometry) under self-weight only",
            "The only load is the section's self-weight, 0.481 kN/m downward along every member (columns too: it acts along " +
            "their axis). Total weight (4 + 4 + 6) × 0.481 = 6.734 kN, so the vertical reactions add up to 6.734 kN. If your " +
            "program computes self-weight from a density, 0.481 kN/m corresponds to A·ρ·g with ρ ≈ 7833 kg/m³.", m);
    }

    private static (string, string, string, FrameModel) MidSpanPointLoad()
    {
        var m = New();
        var a = m.AddNode(0, 0, SupportType.Pin);
        var mid = m.AddNode(3, 0);
        var b = m.AddNode(6, 0, SupportType.RollerVertical);
        m.AddMember(a.Id, mid.Id);
        m.AddMember(mid.Id, b.Id);
        var d = m.AddLoadCase("D", LoadCategory.D);
        m.AddNodalLoad(mid.Id, d.Id, 0, -40, 0);
        return ("12-point-load-at-mid-span", "Simply supported beam with a point load at mid-span",
            "Loads within a span are not supported yet, so the beam is split at mid-span by node 2, which carries the load. " +
            "Closed form: R = P/2 = 20 kN, M(L/2) = PL/4 = 60 kN·m, deflection PL³/48EI = 12.748 mm.", m);
    }
}

/// <summary>A Markdown sheet with the inputs (to re-enter elsewhere) and every result.</summary>
internal static class Sheet
{
    public static string Write(string title, string notes, FrameModel m, FrameResults results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}").AppendLine();
        sb.AppendLine(notes).AppendLine();
        sb.AppendLine($"Section {m.Section.Designation}: A = {m.Section.Area} mm², Ix = {m.Section.Ix} × 10⁶ mm⁴, " +
                      $"Iy = {m.Section.Iy} × 10⁶ mm⁴; E = {m.Material.E} MPa. Axial deformation included, shear deformation " +
                      "not included (switch it off in the other program). Units: m, kN, kN·m; displacements mm and rad.").AppendLine();

        sb.AppendLine("## Model").AppendLine();
        Table(sb, ["Node", "X (m)", "Y (m)", "Support"],
            m.Nodes.Select(n => new[] { I(n.Id), F(n.X, 3), F(n.Y, 3), n.Support.Label() }));
        Table(sb, ["Member", "Node I", "Node J", "Length (m)", "Bending axis"],
            m.Members.Select(x => new[] { I(x.Id), I(x.NodeI), I(x.NodeJ), F(m.LengthOf(x), 3), x.Axis == BendingAxis.Major ? "Major (Ix)" : "Minor (Iy)" }));
        string Case(int id) => m.FindLoadCase(id)!.Name;
        Table(sb, ["Load case", "Category", "Self-weight"],
            m.LoadCases.Select(c => new[] { c.Name, c.Category.ToString(), c.IncludeSelfWeight ? "yes" : "no" }));
        if (m.NodalLoads.Count > 0)
            Table(sb, ["Node", "Case", "Fx (kN)", "Fy (kN)", "Mz (kN·m)"],
                m.NodalLoads.Select(l => new[] { I(l.NodeId), Case(l.LoadCaseId), F(l.Fx, 2), F(l.Fy, 2), F(l.Mz, 2) }));
        if (m.MemberLoads.Count > 0)
            Table(sb, ["Member", "Case", "qx (kN/m)", "qy (kN/m)"],
                m.MemberLoads.Select(l => new[] { I(l.MemberId), Case(l.LoadCaseId), F(l.Qx, 2), F(l.Qy, 2) }));
        sb.AppendLine("Loads are in global axes (+X right, +Y up, moments counter-clockwise positive); distributed loads act on the " +
                      "whole member, per metre of member length.").AppendLine();
        if (m.Combinations.Count > 0)
            Table(sb, ["Combination", .. m.LoadCases.Select(c => c.Name)],
                m.Combinations.Select(c => new[] { c.Name }.Concat(m.LoadCases.Select(lc => F(c.FactorOf(lc.Id), 2))).ToArray()));

        sb.AppendLine("## Results").AppendLine();
        sb.AppendLine("Internal forces at x along each member (x from node I), on the part towards node I: **N** tension positive; " +
                      "**M** positive when it stretches the face on the right-hand side of I→J (sagging for a beam drawn left to " +
                      "right; for a column drawn upwards, tension on its right face); **V = dM/dx**. Reactions are the forces from " +
                      "the supports on the frame, global axes.").AppendLine();
        foreach (var set in results.All)
        {
            sb.AppendLine($"### {set.Name}").AppendLine();
            Table(sb, ["Node", "ux (mm)", "uy (mm)", "θ (rad)", "Rx (kN)", "Ry (kN)", "Mz (kN·m)"],
                set.Nodes.Select(n => new[] { I(n.NodeId), F(n.Ux, 4), F(n.Uy, 4), E(n.Rz), R(n.Rx), R(n.Ry), R(n.Mz) }));
            var rows = new List<string[]>();
            foreach (var r in set.Members)
            {
                var xs = new List<double> { 0, r.Length / 2, r.Length };
                var (_, xm) = r.MaxAbsMoment;
                // V is linear: where it changes sign inside the span, M has its peak there (sagging or hogging).
                var (v0, vl) = (r.ForcesAt(0).V, r.ForcesAt(r.Length).V);
                double? xv = v0 * vl < 0 ? r.Length * v0 / (v0 - vl) : null;
                foreach (var extra in new[] { (double?)xm, xv })
                    if (extra is { } e && xs.All(x => Math.Abs(x - e) > 1e-6)) xs.Add(e);
                foreach (var x in xs.Order())
                {
                    var f = r.ForcesAt(x);
                    var tags = new List<string>();
                    if (xv is { } p && Math.Abs(x - p) < 1e-6) tags.Add("V = 0");
                    if (Math.Abs(x - xm) < 1e-6) tags.Add("max \\|M\\|");
                    var tag = tags.Count > 0 ? $" ({string.Join(", ", tags)})" : "";
                    rows.Add([I(r.MemberId), F(x, 3) + tag, F(f.N, 3), F(f.V, 3), F(f.M, 3), F(r.DisplacementsAt(x).V, 4)]);
                }
            }
            Table(sb, ["Member", "x (m)", "N (kN)", "V (kN)", "M (kN·m)", "v across (mm)"], rows);
        }
        return sb.ToString();
    }

    private static void Table(StringBuilder sb, string[] head, IEnumerable<string[]> rows)
    {
        sb.AppendLine("| " + string.Join(" | ", head) + " |");
        sb.AppendLine("|" + string.Concat(head.Select(_ => " --- |")));
        foreach (var r in rows) sb.AppendLine("| " + string.Join(" | ", r) + " |");
        sb.AppendLine();
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string F(double v, int decimals)
    {
        var s = v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return s.TrimStart('-').Trim('0', '.').Length == 0 ? (0.0).ToString("F" + decimals, CultureInfo.InvariantCulture) : s; // no "-0.000"
    }

    private static string E(double v) => Math.Abs(v) < 5e-10 ? "0" : v.ToString("0.000000", CultureInfo.InvariantCulture);

    private static string R(double? v) => v is null ? "" : F(v.Value, 3);
}
