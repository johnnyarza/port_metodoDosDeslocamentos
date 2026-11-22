namespace FrameAnalysis.Core.Model;

/// <summary>
/// The section every member uses. In the integrated app it comes from the task's active W section,
/// in the same handbook units as WBeamS16App's section table.
/// </summary>
public sealed class FrameSection
{
    public string Designation { get; set; } = "";
    /// <summary>Gross area, mm².</summary>
    public double Area { get; set; }
    /// <summary>Moment of inertia about the major axis, 10⁶ mm⁴.</summary>
    public double Ix { get; set; }
    /// <summary>Moment of inertia about the minor axis, 10⁶ mm⁴.</summary>
    public double Iy { get; set; }
    /// <summary>Self-weight, kN/m.</summary>
    public double DeadLoad { get; set; }

    /// <summary>Moment of inertia for bending in the frame plane, 10⁶ mm⁴.</summary>
    public double I(BendingAxis axis) => axis == BendingAxis.Major ? Ix : Iy;
}

/// <summary>The material every member uses. In the integrated app it is the task's steel grade.</summary>
public sealed class FrameMaterial
{
    public string Name { get; set; } = "";
    /// <summary>Elastic modulus, MPa.</summary>
    public double E { get; set; }
}
