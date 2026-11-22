namespace FrameAnalysis.Core.Model;

/// <summary>How a node is supported. Rollers are horizontal or vertical only (the user's decision).</summary>
public enum SupportType
{
    Free,
    Pin,
    Fixed,
    /// <summary>Roller with a vertical reaction (free to move horizontally).</summary>
    RollerVertical,
    /// <summary>Roller with a horizontal reaction (free to move vertically).</summary>
    RollerHorizontal,
}

public static class SupportTypes
{
    /// <summary>Which global DOFs (u, v, θ) the support restrains.</summary>
    public static (bool X, bool Y, bool Rz) Restraints(this SupportType s) => s switch
    {
        SupportType.Free => (false, false, false),
        SupportType.Pin => (true, true, false),
        SupportType.Fixed => (true, true, true),
        SupportType.RollerVertical => (false, true, false),
        SupportType.RollerHorizontal => (true, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    public static bool IsSupport(this SupportType s) => s != SupportType.Free;

    public static string Label(this SupportType s) => s switch
    {
        SupportType.Free => "Free",
        SupportType.Pin => "Pin",
        SupportType.Fixed => "Fixed",
        SupportType.RollerVertical => "Roller (vertical reaction)",
        SupportType.RollerHorizontal => "Roller (horizontal reaction)",
        _ => s.ToString(),
    };
}

/// <summary>A frame node. Coordinates in m, global X right and Y up. Ids need not be contiguous.</summary>
public sealed class Node
{
    public int Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public SupportType Support { get; set; } = SupportType.Free;
}
