using FrameAnalysis.Core.Model;

namespace FrameAnalysis.Tests;

/// <summary>Small models shared by the tests. Section is W250x49 from WBeamS16App's catalog, E = 200 000 MPa.</summary>
internal static class TestFrames
{
    public const double Area = 6260;   // mm²
    public const double Ix = 70.6;     // 10⁶ mm⁴
    public const double Iy = 15.1;     // 10⁶ mm⁴
    public const double DeadLoad = 0.481; // kN/m
    public const double E = 200_000;   // MPa

    /// <summary>EI about the major axis, N·mm².</summary>
    public const double EIx = E * Ix * 1e6;

    public static FrameModel Empty() => new()
    {
        Section = new FrameSection { Designation = "W250x49", Area = Area, Ix = Ix, Iy = Iy, DeadLoad = DeadLoad },
        Material = new FrameMaterial { Name = "350W", E = E },
    };

    /// <summary>One member from (0,0) to (L,0) with the given supports and a uniform load qy (kN/m) in case "D".</summary>
    public static FrameModel Beam(double length, SupportType left, SupportType right, double qy, out LoadCase d)
    {
        var m = Empty();
        var a = m.AddNode(0, 0, left);
        var b = m.AddNode(length, 0, right);
        var member = m.AddMember(a.Id, b.Id);
        d = m.AddLoadCase("D", LoadCategory.D);
        if (qy != 0) m.AddMemberLoad(member.Id, d.Id, 0, qy);
        return m;
    }

    public static void Near(double expected, double actual, double tolerance, string? what = null)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"{what ?? "value"}: expected {expected:G6}, got {actual:G6} (tolerance {tolerance:G3})");
    }
}
