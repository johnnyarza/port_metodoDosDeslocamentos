namespace FrameAnalysis.Core.Model;

/// <summary>
/// Which section axis bends in the frame plane (the global X–Y plane, where the forces are referenced).
/// Major: the web lies in the frame plane, so the member bends about its x–x axis (Ix, result goes to Mfx).
/// Minor: the flanges lie in the frame plane, so it bends about y–y (Iy, result goes to Mfy).
/// </summary>
public enum BendingAxis
{
    Major,
    Minor,
}

/// <summary>A straight member from <see cref="NodeI"/> to <see cref="NodeJ"/>. Local x runs from I to J.</summary>
public sealed class Member
{
    public int Id { get; set; }
    public int NodeI { get; set; }
    public int NodeJ { get; set; }
    public BendingAxis Axis { get; set; } = BendingAxis.Major;
}
