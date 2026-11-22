using System.Text.Json;
using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Data;
using FrameAnalysis.Core.Model;
using static FrameAnalysis.Tests.TestFrames;

namespace FrameAnalysis.Tests;

/// <summary>The links between nodes, members and loads (Firebird's job in the original) and the JSON file.</summary>
public class ModelTests
{
    [Fact]
    public void TwoNodesAtTheSamePoint_AreRejected()
    {
        var m = Empty();
        m.AddNode(1, 2);
        Assert.Throws<ModelException>(() => m.AddNode(1.00001, 2));
    }

    [Fact]
    public void BadMembers_AreRejected()
    {
        var m = Empty();
        var a = m.AddNode(0, 0, SupportType.Fixed);
        var b = m.AddNode(3, 0);
        Assert.Throws<ModelException>(() => m.AddMember(a.Id, 99));
        Assert.Throws<ModelException>(() => m.AddMember(a.Id, a.Id));
        m.AddMember(a.Id, b.Id);
        // Bug 6 of the guide: two members between the same nodes. Rejected in either direction.
        Assert.Throws<ModelException>(() => m.AddMember(a.Id, b.Id));
        Assert.Throws<ModelException>(() => m.AddMember(b.Id, a.Id));
    }

    [Fact]
    public void NodeUsedByAMember_CannotBeDeleted()
    {
        var m = Empty();
        var a = m.AddNode(0, 0, SupportType.Fixed);
        var b = m.AddNode(3, 0);
        var mem = m.AddMember(a.Id, b.Id);
        var ex = Assert.Throws<ModelException>(() => m.RemoveNode(b.Id));
        Assert.Contains($"member {mem.Id}", ex.Message);
        Assert.Equal(2, m.Nodes.Count);

        m.RemoveMember(mem.Id);
        Assert.Null(m.WhyNodeCannotBeRemoved(b.Id));
    }

    [Fact]
    public void DeletingANode_DeletesItsPointLoads()
    {
        var m = Empty();
        var a = m.AddNode(0, 0, SupportType.Fixed);
        var d = m.AddLoadCase("D", LoadCategory.D);
        m.AddNodalLoad(a.Id, d.Id, 1, 2, 3);
        m.RemoveNode(a.Id);
        Assert.Empty(m.NodalLoads);
    }

    [Fact]
    public void DeletingAMember_DeletesItsLoads()
    {
        var m = Beam(5, SupportType.Pin, SupportType.RollerVertical, -3, out _);
        m.RemoveMember(m.Members[0].Id);
        Assert.Empty(m.MemberLoads);
    }

    [Fact]
    public void DeletingALoadCase_DeletesItsLoadsAndFactors()
    {
        var m = Beam(5, SupportType.Pin, SupportType.RollerVertical, -3, out var d);
        var l = m.AddLoadCase("L", LoadCategory.L);
        m.AddNodalLoad(1, d.Id, 0, -1, 0);
        m.Combinations.Add(new LoadCombination { Name = "C", Factors = { [d.Id] = 1.25, [l.Id] = 1.5 } });
        m.RemoveLoadCase(d.Id);
        Assert.Empty(m.MemberLoads);
        Assert.Empty(m.NodalLoads);
        Assert.False(m.Combinations[0].Factors.ContainsKey(d.Id));
        Assert.True(m.Combinations[0].Factors.ContainsKey(l.Id));
    }

    [Fact]
    public void SelfWeight_OnlyOnOneDeadCase()
    {
        var m = Empty();
        Assert.Throws<ModelException>(() => m.AddLoadCase("L", LoadCategory.L, includeSelfWeight: true));
        m.AddLoadCase("D1", LoadCategory.D, includeSelfWeight: true);
        Assert.Throws<ModelException>(() => m.AddLoadCase("D2", LoadCategory.D, includeSelfWeight: true));
        Assert.Throws<ModelException>(() => m.AddLoadCase("d1", LoadCategory.D));
    }

    [Fact]
    public void Validate_FindsBrokenLinks_InAModelBuiltWithoutTheEditMethods()
    {
        var m = Empty();
        m.Nodes.Add(new Node { Id = 1, X = 0, Y = 0, Support = SupportType.Fixed });
        m.Nodes.Add(new Node { Id = 2, X = 0, Y = 0 });
        m.Nodes.Add(new Node { Id = 3, X = 9, Y = 9 });
        m.Members.Add(new Member { Id = 1, NodeI = 1, NodeJ = 7 });
        m.LoadCases.Add(new LoadCase { Id = 1, Name = "D", Category = LoadCategory.D });
        m.MemberLoads.Add(new MemberLoad { MemberId = 5, LoadCaseId = 1, Qy = -1 });
        m.NodalLoads.Add(new NodalLoad { NodeId = 1, LoadCaseId = 4 });
        m.Combinations.Add(new LoadCombination { Name = "C", Factors = { [8] = 1 } });

        var errors = m.Validate();
        Assert.Contains(errors, e => e.Contains("same point"));
        Assert.Contains(errors, e => e.Contains("node 7"));
        Assert.Contains(errors, e => e.Contains("member 5"));
        Assert.Contains(errors, e => e.Contains("load case 4"));
        Assert.Contains(errors, e => e.Contains("load case 8"));
        Assert.Contains(errors, e => e.Contains("Node 3 is not connected"));
    }

    [Fact]
    public void Json_RoundTrip_KeepsTheModelAndItsResults()
    {
        var m = Beam(6, SupportType.Fixed, SupportType.RollerVertical, -8, out var d);
        d.IncludeSelfWeight = true;
        m.Members[0].Axis = BendingAxis.Minor;
        var l = m.AddLoadCase("L", LoadCategory.L);
        m.AddNodalLoad(2, l.Id, 1, -2, 0.5);
        m.Combinations.Add(new LoadCombination { Name = "C", LimitState = LimitState.Sls, Factors = { [d.Id] = 1, [l.Id] = 1 } });

        var json = FrameModelJson.Serialize(m);
        var (back, problems) = FrameModelJson.Load(json);
        Assert.Empty(problems);
        Assert.Contains("\"RollerVertical\"", json);
        Assert.Equal(BendingAxis.Minor, back.Members[0].Axis);
        Assert.True(back.LoadCases[0].IncludeSelfWeight);
        Assert.Equal(LimitState.Sls, back.Combinations[0].LimitState);

        var a = FrameSolver.Analyze(m).Combination("C");
        var b = FrameSolver.Analyze(back).Combination("C");
        Near(a.Members[0].ForcesAt(2).M, b.Members[0].ForcesAt(2).M, 1e-12);
        Near(a.Node(2).Uy, b.Node(2).Uy, 1e-12);
    }

    [Fact]
    public void Json_WithBrokenLinks_LoadsButReportsThem()
    {
        var m = Beam(6, SupportType.Pin, SupportType.RollerVertical, -8, out _);
        var json = FrameModelJson.Serialize(m).Replace("\"nodeJ\": 2", "\"nodeJ\": 42");
        var (_, problems) = FrameModelJson.Load(json);
        Assert.Contains(problems, p => p.Contains("node 42"));
    }

    [Fact]
    public void Json_NotAFrameFile_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => FrameModelJson.Deserialize("{\"version\": 9, \"model\": {}}"));
        Assert.ThrowsAny<JsonException>(() => FrameModelJson.Deserialize("not json"));
        Assert.ThrowsAny<JsonException>(() => FrameModelJson.Deserialize("{\"version\": 1, \"model\": {\"nodes\": [null]}}"));
    }

    [Fact]
    public void Json_NullLists_AreReadAsEmpty()
    {
        var model = FrameModelJson.Deserialize("{\"version\": 1, \"model\": {\"nodes\": null, \"combinations\": [{\"name\": \"C\", \"factors\": null}]}}");
        Assert.Empty(model.Nodes);
        Assert.Empty(model.Combinations[0].Factors);
    }
}
