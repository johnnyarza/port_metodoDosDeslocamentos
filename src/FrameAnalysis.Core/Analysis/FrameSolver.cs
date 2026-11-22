using FrameAnalysis.Core.Model;

namespace FrameAnalysis.Core.Analysis;

/// <summary>The model cannot be solved; <see cref="Errors"/> lists why.</summary>
public sealed class ModelValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>The stiffness matrix is singular: the structure is a mechanism (usually missing supports).</summary>
public sealed class UnstableStructureException(string message) : Exception(message);

/// <summary>Stiffness properties of one member, in the section table's units: mm², 10⁶ mm⁴, MPa, kN/m.</summary>
public sealed record MemberProperties(double Area, double I, double E, double SelfWeight);

/// <summary>
/// Linear-elastic plane-frame analysis by the displacement (direct stiffness) method, ported from
/// metodo_deslocamentos in the 2020 Delphi app (Gere &amp; Weaver's plane-frame program).
/// Three DOFs per node (u, v, θ). Works in N and mm internally; results are kN, kN·m, mm and rad.
/// Pure: no database or UI access.
/// </summary>
public static class FrameSolver
{
    /// <summary>
    /// Solves every load case once (one factorization), then builds each combination by superposition.
    /// <paramref name="properties"/> overrides the member properties; by default every member uses the model's
    /// section (Ix or Iy by its <see cref="BendingAxis"/>) and material.
    /// </summary>
    public static FrameResults Analyze(FrameModel model, Func<Member, MemberProperties>? properties = null)
    {
        var errors = model.Validate();
        if (model.LoadCases.Count == 0) errors.Add("There are no load cases.");
        if (errors.Count > 0) throw new ModelValidationException(errors);

        properties ??= m => new MemberProperties(model.Section.Area, model.Section.I(m.Axis), model.Material.E, model.Section.DeadLoad);

        var nodeIndex = model.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(t => t.Id, t => t.i);
        var dofCount = 3 * model.Nodes.Count;
        var restrained = new bool[dofCount];
        for (var i = 0; i < model.Nodes.Count; i++)
        {
            var (rx, ry, rz) = model.Nodes[i].Support.Restraints();
            restrained[3 * i] = rx;
            restrained[3 * i + 1] = ry;
            restrained[3 * i + 2] = rz;
        }

        var elements = model.Members.Select(m => Element.Create(m, model, nodeIndex, properties(m))).ToList();

        // Assemble the full stiffness matrix (with +=, unlike the original's off-diagonal :=).
        var k = new double[dofCount, dofCount];
        foreach (var e in elements)
        {
            var kg = e.GlobalStiffness();
            var dofs = e.Dofs;
            for (var a = 0; a < 6; a++)
                for (var b = 0; b < 6; b++)
                    k[dofs[a], dofs[b]] += kg[a, b];
        }

        // Partition: free DOFs first (the original's RL/CRL renumbering).
        var free = Enumerable.Range(0, dofCount).Where(d => !restrained[d]).ToArray();
        var kff = new double[free.Length, free.Length];
        for (var a = 0; a < free.Length; a++)
            for (var b = 0; b < free.Length; b++)
                kff[a, b] = k[free[a], free[b]];
        var chol = Cholesky.TryFactor(kff, out var failed)
            ?? throw new UnstableStructureException(UnstableMessage(model, free[failed]));

        var cases = model.LoadCases.Select(lc => SolveCase(model, lc, elements, k, free, restrained, chol, nodeIndex)).ToList();

        var combos = new List<ResultSet>();
        foreach (var c in model.Combinations)
        {
            var parts = model.LoadCases.Select((lc, i) => (Set: cases[i], Factor: c.FactorOf(lc.Id))).Where(p => p.Factor != 0).ToList();
            if (parts.Count == 0) parts = [(cases[0], 0.0)]; // all factors zero: a valid, empty result
            combos.Add(ResultSet.Combine(c.Name, c.LimitState == LimitState.Uls ? ResultKind.Uls : ResultKind.Sls, parts));
        }

        return new FrameResults { Cases = cases, Combinations = combos };
    }

    private static ResultSet SolveCase(FrameModel model, LoadCase lc, List<Element> elements, double[,] k, int[] free,
        bool[] restrained, Cholesky chol, Dictionary<int, int> nodeIndex)
    {
        var dofCount = restrained.Length;

        // Nodal loads, global, N and N·mm. Several loads on one node add up.
        var f = new double[dofCount];
        foreach (var l in model.NodalLoads.Where(l => l.LoadCaseId == lc.Id))
        {
            var i = nodeIndex[l.NodeId];
            f[3 * i] += l.Fx * 1e3;
            f[3 * i + 1] += l.Fy * 1e3;
            f[3 * i + 2] += l.Mz * 1e6;
        }

        // Member loads: sum every record on the member (the original kept only one), plus self-weight.
        var qGlobal = new (double X, double Y)[elements.Count];
        var memberIndex = model.Members.Select((m, i) => (m.Id, i)).ToDictionary(t => t.Id, t => t.i);
        foreach (var l in model.MemberLoads.Where(l => l.LoadCaseId == lc.Id))
        {
            var i = memberIndex[l.MemberId];
            qGlobal[i] = (qGlobal[i].X + l.Qx, qGlobal[i].Y + l.Qy); // kN/m = N/mm
        }
        if (lc.IncludeSelfWeight)
            for (var i = 0; i < elements.Count; i++)
                qGlobal[i] = (qGlobal[i].X, qGlobal[i].Y - elements[i].SelfWeight);

        var aml = new double[elements.Count][];
        var qLocal = new (double X, double Y)[elements.Count];
        for (var i = 0; i < elements.Count; i++)
        {
            var e = elements[i];
            qLocal[i] = e.ToLocal(qGlobal[i].X, qGlobal[i].Y);
            aml[i] = e.FixedEndActions(qLocal[i].X, qLocal[i].Y);
            var eq = e.ToGlobal(aml[i]); // equivalent nodal loads = −Tᵀ·AML
            for (var a = 0; a < 6; a++) f[e.Dofs[a]] -= eq[a];
        }

        // Solve Kff·Df = Ff; restrained DOFs stay 0.
        var ff = free.Select(d => f[d]).ToArray();
        var df = chol.Solve(ff);
        var d = new double[dofCount];
        for (var a = 0; a < free.Length; a++) d[free[a]] = df[a];

        // Reactions: R = K·D − F at the restrained DOFs.
        double? Reaction(int dof)
        {
            if (!restrained[dof]) return null;
            var r = -f[dof];
            for (var b = 0; b < dofCount; b++) r += k[dof, b] * d[b];
            return r;
        }

        var nodes = model.Nodes.Select((n, i) => new NodeResult
        {
            NodeId = n.Id,
            Ux = d[3 * i],
            Uy = d[3 * i + 1],
            Rz = d[3 * i + 2],
            Rx = Reaction(3 * i) / 1e3,
            Ry = Reaction(3 * i + 1) / 1e3,
            Mz = Reaction(3 * i + 2) / 1e6,
        }).ToList();

        // Member-end actions: AM = AML + k_local·T·d.
        var members = elements.Select((e, i) =>
        {
            var dl = e.ToLocal(e.Dofs.Select(x => d[x]).ToArray());
            var am = e.LocalStiffness().Multiply(dl);
            for (var a = 0; a < 6; a++) am[a] += aml[i][a];
            return new MemberResult(e.Member.Id, e.Member.Axis, e.L, e.EA, e.EI, am, dl, qLocal[i].X, qLocal[i].Y);
        }).ToList();

        return new ResultSet { Name = lc.Name, Kind = ResultKind.LoadCase, Nodes = nodes, Members = members };
    }

    private static string UnstableMessage(FrameModel model, int dof)
    {
        var node = model.Nodes[dof / 3];
        var what = (dof % 3) switch { 0 => "move horizontally", 1 => "move vertically", _ => "rotate" };
        return $"The structure is unstable (a mechanism): it is free to {what} at node {node.Id}. Check the supports.";
    }

    /// <summary>One member in solver units (N, mm) with its direction cosines and global DOF numbers.</summary>
    private sealed class Element
    {
        public required Member Member { get; init; }
        public required int[] Dofs { get; init; }
        public double L { get; init; }
        public double C { get; init; }
        public double S { get; init; }
        public double EA { get; init; }
        public double EI { get; init; }
        public double SelfWeight { get; init; } // N/mm

        public static Element Create(Member m, FrameModel model, Dictionary<int, int> nodeIndex, MemberProperties p)
        {
            if (!(p.Area > 0) || !(p.I > 0) || !(p.E > 0) || !(p.SelfWeight >= 0))
                throw new ModelValidationException([$"Member {m.Id}: A, I and E must be greater than 0 and the self-weight cannot be negative."]);
            var a = model.FindNode(m.NodeI)!;
            var b = model.FindNode(m.NodeJ)!;
            var dx = (b.X - a.X) * 1000;
            var dy = (b.Y - a.Y) * 1000;
            var l = Math.Sqrt(dx * dx + dy * dy);
            int i = nodeIndex[m.NodeI], j = nodeIndex[m.NodeJ];
            return new Element
            {
                Member = m,
                Dofs = [3 * i, 3 * i + 1, 3 * i + 2, 3 * j, 3 * j + 1, 3 * j + 2],
                L = l,
                C = dx / l,
                S = dy / l,
                EA = p.E * p.Area,
                EI = p.E * p.I * 1e6,
                SelfWeight = p.SelfWeight,
            };
        }

        /// <summary>Local stiffness (SCM1 = EA/L, SCM2 = 4EI/L, SCM3 = 6EI/L², SCM4 = 12EI/L³).</summary>
        public double[,] LocalStiffness()
        {
            double l = L, a = EA / l, b4 = 4 * EI / l, b2 = 2 * EI / l, c = 6 * EI / (l * l), s = 12 * EI / (l * l * l);
            return new double[,]
            {
                { a, 0, 0, -a, 0, 0 },
                { 0, s, c, 0, -s, c },
                { 0, c, b4, 0, -c, b2 },
                { -a, 0, 0, a, 0, 0 },
                { 0, -s, -c, 0, s, -c },
                { 0, c, b2, 0, -c, b4 },
            };
        }

        /// <summary>Tᵀ·k·T: the original's SMD, for any member angle.</summary>
        public double[,] GlobalStiffness()
        {
            var kl = LocalStiffness();
            var t = Rotation();
            var kg = new double[6, 6];
            for (var i = 0; i < 6; i++)
                for (var j = 0; j < 6; j++)
                {
                    double sum = 0;
                    for (var p = 0; p < 6; p++)
                        for (var q = 0; q < 6; q++)
                            sum += t[p, i] * kl[p, q] * t[q, j];
                    kg[i, j] = sum;
                }
            return kg;
        }

        public double[,] Rotation()
        {
            var t = new double[6, 6];
            for (var o = 0; o < 6; o += 3)
            {
                t[o, o] = C; t[o, o + 1] = S;
                t[o + 1, o] = -S; t[o + 1, o + 1] = C;
                t[o + 2, o + 2] = 1;
            }
            return t;
        }

        public (double X, double Y) ToLocal(double gx, double gy) => (C * gx + S * gy, -S * gx + C * gy);

        public double[] ToLocal(double[] global) => Rotation().Multiply(global);

        public double[] ToGlobal(double[] local)
        {
            var t = Rotation();
            var g = new double[6];
            for (var i = 0; i < 6; i++)
                for (var j = 0; j < 6; j++)
                    g[i] += t[j, i] * local[j];
            return g;
        }

        /// <summary>Actions that hold the ends of the fully fixed member under a local uniform load (the original's AML).</summary>
        public double[] FixedEndActions(double qx, double qy)
        {
            var l = L;
            return [-qx * l / 2, -qy * l / 2, -qy * l * l / 12, -qx * l / 2, -qy * l / 2, qy * l * l / 12];
        }
    }

    private static double[] Multiply(this double[,] m, double[] v)
    {
        var r = new double[m.GetLength(0)];
        for (var i = 0; i < r.Length; i++)
            for (var j = 0; j < v.Length; j++)
                r[i] += m[i, j] * v[j];
        return r;
    }
}
