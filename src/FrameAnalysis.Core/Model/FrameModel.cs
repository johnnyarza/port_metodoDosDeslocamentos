namespace FrameAnalysis.Core.Model;

/// <summary>Thrown when an edit would break the model (missing node, duplicate member, node still in use, ...).</summary>
public sealed class ModelException(string message) : Exception(message);

/// <summary>
/// A plane frame: geometry, the section and material from the host task, loads and combinations.
/// Units: m, kN, kN/m, kN·m; the section in handbook units (mm², 10⁶ mm⁴, kN/m) and E in MPa.
/// The edit methods keep the references consistent (the job Firebird's keys did in the original);
/// <see cref="Validate"/> checks a model built any other way, e.g. loaded from JSON or edited in a grid.
/// </summary>
public sealed class FrameModel
{
    /// <summary>Two nodes closer than this (m) are the same point; a member shorter than this has no length.</summary>
    public const double CoordinateTolerance = 1e-4;

    public FrameSection Section { get; set; } = new();
    public FrameMaterial Material { get; set; } = new();
    public List<Node> Nodes { get; set; } = [];
    public List<Member> Members { get; set; } = [];
    public List<LoadCase> LoadCases { get; set; } = [];
    public List<NodalLoad> NodalLoads { get; set; } = [];
    public List<MemberLoad> MemberLoads { get; set; } = [];
    public List<LoadCombination> Combinations { get; set; } = [];

    public Node? FindNode(int id) => Nodes.Find(n => n.Id == id);
    public Member? FindMember(int id) => Members.Find(m => m.Id == id);
    public LoadCase? FindLoadCase(int id) => LoadCases.Find(c => c.Id == id);

    public double LengthOf(Member m)
    {
        var a = FindNode(m.NodeI) ?? throw new ModelException($"Member {m.Id}: node {m.NodeI} does not exist.");
        var b = FindNode(m.NodeJ) ?? throw new ModelException($"Member {m.Id}: node {m.NodeJ} does not exist.");
        return Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
    }

    // ---- Edits (each keeps the model consistent or throws ModelException) ----

    public Node AddNode(double x, double y, SupportType support = SupportType.Free)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ModelException("Node coordinates must be numbers.");
        var clash = Nodes.Find(n => SamePoint(n.X, n.Y, x, y));
        if (clash is not null) throw new ModelException($"Node {clash.Id} is already at ({x:G}, {y:G}).");
        var node = new Node { Id = NextId(Nodes.Select(n => n.Id)), X = x, Y = y, Support = support };
        Nodes.Add(node);
        return node;
    }

    /// <summary>Why the node cannot be deleted, or null when it can. A node used by a member is blocked.</summary>
    public string? WhyNodeCannotBeRemoved(int nodeId)
    {
        var users = Members.Where(m => m.NodeI == nodeId || m.NodeJ == nodeId).Select(m => m.Id).ToList();
        return users.Count == 0 ? null
            : $"Node {nodeId} is used by member{(users.Count > 1 ? "s" : "")} {string.Join(", ", users)}. Delete or reconnect them first.";
    }

    /// <summary>Removes a node that no member uses, together with the point loads on it.</summary>
    public void RemoveNode(int nodeId)
    {
        if (WhyNodeCannotBeRemoved(nodeId) is { } reason) throw new ModelException(reason);
        if (Nodes.RemoveAll(n => n.Id == nodeId) == 0) throw new ModelException($"Node {nodeId} does not exist.");
        NodalLoads.RemoveAll(l => l.NodeId == nodeId);
    }

    public Member AddMember(int nodeI, int nodeJ, BendingAxis axis = BendingAxis.Major)
    {
        var member = new Member { Id = NextId(Members.Select(m => m.Id)), NodeI = nodeI, NodeJ = nodeJ, Axis = axis };
        if (MemberProblem(member, Members) is { } problem) throw new ModelException(problem);
        Members.Add(member);
        return member;
    }

    /// <summary>Removes a member and the loads on it. Its nodes stay.</summary>
    public void RemoveMember(int memberId)
    {
        if (Members.RemoveAll(m => m.Id == memberId) == 0) throw new ModelException($"Member {memberId} does not exist.");
        MemberLoads.RemoveAll(l => l.MemberId == memberId);
    }

    public LoadCase AddLoadCase(string name, LoadCategory category, bool includeSelfWeight = false)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ModelException("A load case needs a name.");
        if (LoadCases.Exists(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ModelException($"Load case \"{name}\" already exists.");
        if (includeSelfWeight && category != LoadCategory.D) throw new ModelException("Self-weight can only be part of a dead (D) load case.");
        if (includeSelfWeight && LoadCases.Exists(c => c.IncludeSelfWeight))
            throw new ModelException("Self-weight is already included in another load case.");
        var lc = new LoadCase { Id = NextId(LoadCases.Select(c => c.Id)), Name = name.Trim(), Category = category, IncludeSelfWeight = includeSelfWeight };
        LoadCases.Add(lc);
        return lc;
    }

    /// <summary>Removes a load case, its loads and its factors in every combination.</summary>
    public void RemoveLoadCase(int loadCaseId)
    {
        if (LoadCases.RemoveAll(c => c.Id == loadCaseId) == 0) throw new ModelException($"Load case {loadCaseId} does not exist.");
        NodalLoads.RemoveAll(l => l.LoadCaseId == loadCaseId);
        MemberLoads.RemoveAll(l => l.LoadCaseId == loadCaseId);
        foreach (var c in Combinations) c.Factors.Remove(loadCaseId);
    }

    public NodalLoad AddNodalLoad(int nodeId, int loadCaseId, double fx, double fy, double mz)
    {
        var load = new NodalLoad { NodeId = nodeId, LoadCaseId = loadCaseId, Fx = fx, Fy = fy, Mz = mz };
        if (NodalLoadProblem(load) is { } problem) throw new ModelException(problem);
        NodalLoads.Add(load);
        return load;
    }

    public MemberLoad AddMemberLoad(int memberId, int loadCaseId, double qx, double qy)
    {
        var load = new MemberLoad { MemberId = memberId, LoadCaseId = loadCaseId, Qx = qx, Qy = qy };
        if (MemberLoadProblem(load) is { } problem) throw new ModelException(problem);
        MemberLoads.Add(load);
        return load;
    }

    // ---- Validation ----

    /// <summary>Every problem that stops the model from being solved. Empty when it can be solved.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (!(Section.Area > 0)) errors.Add("Section: the area must be greater than 0.");
        foreach (var axis in Members.Select(m => m.Axis).Distinct())
            if (!(Section.I(axis) > 0)) errors.Add($"Section: {(axis == BendingAxis.Major ? "Ix" : "Iy")} must be greater than 0.");
        if (!(Section.DeadLoad >= 0)) errors.Add("Section: the dead load cannot be negative.");
        if (!(Material.E > 0)) errors.Add("Material: E must be greater than 0.");

        Duplicates(Nodes.Select(n => n.Id), "node", errors);
        for (var a = 0; a < Nodes.Count; a++)
        {
            var n = Nodes[a];
            if (!double.IsFinite(n.X) || !double.IsFinite(n.Y)) errors.Add($"Node {n.Id}: coordinates must be numbers.");
            for (var b = a + 1; b < Nodes.Count; b++)
                if (SamePoint(n.X, n.Y, Nodes[b].X, Nodes[b].Y))
                    errors.Add($"Nodes {n.Id} and {Nodes[b].Id} are at the same point.");
        }
        if (Nodes.Count > 0 && !Nodes.Exists(n => n.Support.IsSupport())) errors.Add("The frame needs at least one support.");

        Duplicates(Members.Select(m => m.Id), "member", errors);
        for (var a = 0; a < Members.Count; a++)
            if (MemberProblem(Members[a], Members.Take(a)) is { } problem) errors.Add(problem);
        if (Members.Count == 0) errors.Add("The frame has no members.");
        foreach (var n in Nodes)
            if (!Members.Exists(m => m.NodeI == n.Id || m.NodeJ == n.Id))
                errors.Add($"Node {n.Id} is not connected to any member.");

        Duplicates(LoadCases.Select(c => c.Id), "load case", errors);
        foreach (var c in LoadCases)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) errors.Add($"Load case {c.Id} needs a name.");
            if (c.IncludeSelfWeight && c.Category != LoadCategory.D) errors.Add($"Load case \"{c.Name}\": self-weight can only be part of a dead (D) case.");
        }
        foreach (var g in LoadCases.Where(c => !string.IsNullOrWhiteSpace(c.Name)).GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Load case name \"{g.Key}\" is used more than once.");
        if (LoadCases.Count(c => c.IncludeSelfWeight) > 1) errors.Add("Self-weight is included in more than one load case.");

        foreach (var l in NodalLoads)
            if (NodalLoadProblem(l) is { } problem) errors.Add(problem);
        foreach (var l in MemberLoads)
            if (MemberLoadProblem(l) is { } problem) errors.Add(problem);

        foreach (var c in Combinations)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) errors.Add("A load combination needs a name.");
            foreach (var (caseId, factor) in c.Factors)
            {
                if (FindLoadCase(caseId) is null) errors.Add($"Combination \"{c.Name}\" refers to load case {caseId}, which does not exist.");
                if (!double.IsFinite(factor)) errors.Add($"Combination \"{c.Name}\": factors must be numbers.");
            }
        }
        foreach (var g in Combinations.Where(c => !string.IsNullOrWhiteSpace(c.Name)).GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Combination name \"{g.Key}\" is used more than once.");

        return errors;
    }

    private string? MemberProblem(Member m, IEnumerable<Member> others)
    {
        var a = FindNode(m.NodeI);
        var b = FindNode(m.NodeJ);
        if (a is null) return $"Member {m.Id}: node {m.NodeI} does not exist.";
        if (b is null) return $"Member {m.Id}: node {m.NodeJ} does not exist.";
        if (m.NodeI == m.NodeJ) return $"Member {m.Id}: both ends are node {m.NodeI}.";
        if (SamePoint(a.X, a.Y, b.X, b.Y)) return $"Member {m.Id} has no length.";
        var twin = others.FirstOrDefault(o => o.Id != m.Id
            && ((o.NodeI == m.NodeI && o.NodeJ == m.NodeJ) || (o.NodeI == m.NodeJ && o.NodeJ == m.NodeI)));
        return twin is null ? null : $"Members {twin.Id} and {m.Id} both connect nodes {m.NodeI} and {m.NodeJ}.";
    }

    private string? NodalLoadProblem(NodalLoad l)
    {
        if (FindNode(l.NodeId) is null) return $"A point load refers to node {l.NodeId}, which does not exist.";
        if (FindLoadCase(l.LoadCaseId) is null) return $"A point load on node {l.NodeId} refers to load case {l.LoadCaseId}, which does not exist.";
        if (!double.IsFinite(l.Fx) || !double.IsFinite(l.Fy) || !double.IsFinite(l.Mz)) return $"The point load on node {l.NodeId} must be numbers.";
        return null;
    }

    private string? MemberLoadProblem(MemberLoad l)
    {
        if (FindMember(l.MemberId) is null) return $"A distributed load refers to member {l.MemberId}, which does not exist.";
        if (FindLoadCase(l.LoadCaseId) is null) return $"A distributed load on member {l.MemberId} refers to load case {l.LoadCaseId}, which does not exist.";
        if (!double.IsFinite(l.Qx) || !double.IsFinite(l.Qy)) return $"The distributed load on member {l.MemberId} must be numbers.";
        return null;
    }

    private static void Duplicates(IEnumerable<int> ids, string what, List<string> errors)
    {
        foreach (var g in ids.GroupBy(i => i).Where(g => g.Count() > 1))
            errors.Add($"There is more than one {what} {g.Key}.");
    }

    private static bool SamePoint(double x1, double y1, double x2, double y2) =>
        Math.Abs(x1 - x2) < CoordinateTolerance && Math.Abs(y1 - y2) < CoordinateTolerance;

    private static int NextId(IEnumerable<int> ids) => ids.DefaultIfEmpty(0).Max() + 1;
}
