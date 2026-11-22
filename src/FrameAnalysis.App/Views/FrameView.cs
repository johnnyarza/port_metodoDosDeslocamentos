using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Drawing;
using FrameAnalysis.Core.Model;
using Wpf.Ui.Appearance;

namespace FrameAnalysis.App.Views;

/// <summary>What the frame drawing shows on top of the frame.</summary>
public enum FrameDrawMode
{
    /// <summary>Supports, loads and numbering only.</summary>
    Model,
    Deformed,
    Axial,
    Shear,
    Moment,
    Reactions,
}

/// <summary>A set of loads the drawing can show: one load case (factor 1) or a combination (its factors).</summary>
public sealed record LoadDisplay(string Name, Func<int, double> Factor, bool IsNone = false)
{
    public override string ToString() => Name;
}

/// <summary>
/// Draws the frame: members, nodes, supports, the chosen loads and, once solved, the deformed shape, the N, V and M
/// diagrams or the reactions of one result set. Geometry comes from <see cref="FramePlot"/> (model coordinates);
/// this control only maps it to the screen and paints it.
/// Mouse: wheel zooms about the cursor, drag pans, double-click fits, click picks a node or a member.
/// </summary>
public sealed class FrameView : FrameworkElement
{
    // ---- Properties ----

    public static readonly DependencyProperty ModelProperty = Register(nameof(Model), typeof(FrameModel), null);
    public static readonly DependencyProperty ResultProperty = Register(nameof(Result), typeof(ResultSet), null);
    public static readonly DependencyProperty ModeProperty = Register(nameof(Mode), typeof(FrameDrawMode), FrameDrawMode.Model);
    public static readonly DependencyProperty LoadsProperty = Register(nameof(Loads), typeof(LoadDisplay), null);
    public static readonly DependencyProperty ShowLabelsProperty = Register(nameof(ShowLabels), typeof(bool), true);
    public static readonly DependencyProperty ShowValuesProperty = Register(nameof(ShowValues), typeof(bool), true);
    public static readonly DependencyProperty IsStaleProperty = Register(nameof(IsStale), typeof(bool), false);
    public static readonly DependencyProperty RevisionProperty = Register(nameof(Revision), typeof(int), 0);

    public static readonly DependencyProperty SelectedMemberIdProperty = DependencyProperty.Register(nameof(SelectedMemberId), typeof(int?),
        typeof(FrameView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty SelectedNodeIdProperty = DependencyProperty.Register(nameof(SelectedNodeId), typeof(int?),
        typeof(FrameView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(FrameView), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    public FrameModel? Model { get => (FrameModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public ResultSet? Result { get => (ResultSet?)GetValue(ResultProperty); set => SetValue(ResultProperty, value); }
    public FrameDrawMode Mode { get => (FrameDrawMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public LoadDisplay? Loads { get => (LoadDisplay?)GetValue(LoadsProperty); set => SetValue(LoadsProperty, value); }
    public bool ShowLabels { get => (bool)GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }
    public bool ShowValues { get => (bool)GetValue(ShowValuesProperty); set => SetValue(ShowValuesProperty, value); }
    public bool IsStale { get => (bool)GetValue(IsStaleProperty); set => SetValue(IsStaleProperty, value); }

    /// <summary>Bump to redraw after the model's objects were edited in place.</summary>
    public int Revision { get => (int)GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }

    public int? SelectedMemberId { get => (int?)GetValue(SelectedMemberIdProperty); set => SetValue(SelectedMemberIdProperty, value); }
    public int? SelectedNodeId { get => (int?)GetValue(SelectedNodeIdProperty); set => SetValue(SelectedNodeIdProperty, value); }

    // ---- Look (screen pixels) ----

    private const double DiagramPx = 55;       // largest diagram ordinate
    private const double DeformedPx = 55;      // largest drawn displacement
    private const double SupportSize = 11;
    private const double ArrowPx = 42;         // point loads and reactions
    private const double LoadRowPx = 26;       // distributed-load arrows
    private const double HitNodePx = 8;
    private const double HitMemberPx = 6;

    private static readonly Color Positive = Color.FromRgb(0x2F, 0x80, 0xD4);
    private static readonly Color Negative = Color.FromRgb(0xD9, 0x4A, 0x3F);
    private static readonly Color LoadColor = Color.FromRgb(0xE0, 0x80, 0x10);
    private static readonly Color ReactionColor = Color.FromRgb(0x2E, 0xA0, 0x5C);

    // ---- View state ----

    private double _zoom = 1;               // relative to the fitted scale
    private Vector _pan;                    // screen offset added after fitting
    private Point? _pressAt;
    private Vector _panAtPress;
    private bool _dragging;
    private readonly List<(int Id, Point P)> _hitNodes = [];
    private readonly List<(int Id, Point A, Point B)> _hitMembers = [];

    // Mapping of the last render: screen = _origin + (x, −y)·_scale.
    private double _scale = 1;
    private Point _origin;
    private double _fitScale;
    private Point2 _fitCentre;
    private Point _fitCentreScreen;

    public FrameView()
    {
        ClipToBounds = true;
        Focusable = true;
        Loaded += (_, _) => ApplicationThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => ApplicationThemeManager.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged(ApplicationTheme theme, Color accent) => InvalidateVisual();

    /// <summary>Back to the whole frame in view.</summary>
    public void Fit()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    private Point ToScreen(Point2 p) => new(_origin.X + p.X * _scale, _origin.Y - p.Y * _scale);

    private Point2 ToModel(Point s) => new((s.X - _origin.X) / _scale, (_origin.Y - s.Y) / _scale);

    /// <summary>A model direction as a screen unit vector.</summary>
    private static Vector ScreenDir(Point2 d)
    {
        var v = new Vector(d.X, -d.Y);
        if (v.Length > 0) v.Normalize();
        return v;
    }

    // ---- Rendering ----

    private sealed class Palette(FrameworkElement owner)
    {
        public Brush Text { get; } = Find(owner, "TextFillColorPrimaryBrush", Brushes.Black);
        public Brush Secondary { get; } = Find(owner, "TextFillColorSecondaryBrush", Brushes.DimGray);
        public Brush Tertiary { get; } = Find(owner, "TextFillColorTertiaryBrush", Brushes.Gray);
        public Brush Caution { get; } = Find(owner, "SystemFillColorCautionBrush", Brushes.DarkGoldenrod);
        public Brush Accent { get; } = Find(owner, "AccentFillColorDefaultBrush", Brushes.DodgerBlue);
        public Brush Card { get; } = Find(owner, "CardBackgroundFillColorDefaultBrush", Brushes.White);
        public Brush SolidBackground { get; } = Find(owner, "ApplicationBackgroundBrush", Brushes.White);

        private static Brush Find(FrameworkElement owner, string key, Brush fallback) => owner.TryFindResource(key) as Brush ?? fallback;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Size(ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size)); // hit-testable everywhere
        _hitNodes.Clear();
        _hitMembers.Clear();
        var pal = new Palette(this);
        var model = Model;
        var bounds = model is null ? null : FramePlot.Bounds(model);
        if (model is null || bounds is null || size.Width < 50 || size.Height < 50)
        {
            _fitScale = 0;
            DrawText(dc, "Add nodes and members to see the frame here.", new Point(16, 14), pal.Secondary, 13);
            return;
        }

        var members = FramePlot.Members(model);
        var result = Mode is FrameDrawMode.Model ? null : Result;
        var faint = result is not null;

        // Loads go with the frame and with the reactions (a free-body diagram); on diagrams they would only clutter.
        var showLoads = Loads is { IsNone: false } && Mode is FrameDrawMode.Model or FrameDrawMode.Reactions;
        var plotLoads = showLoads ? FramePlot.Loads(model, Loads!.Factor) : null;

        // Legend lines first: the frame is fitted below them.
        var legend = new List<(string Text, Brush Brush)>();
        if (showLoads) legend.Add(($"Loads: {Loads!.Name}", pal.Secondary));
        if (plotLoads is { SelfWeight: not 0 })
            legend.Add(($"+ self-weight {Num(plotLoads.SelfWeight)} kN/m downward on every member ({model.Section.Designation})", pal.Secondary));
        var resultLine = legend.Count;
        if (Mode != FrameDrawMode.Model) legend.Add(result is null ? ("Solve to see results.", pal.Secondary) : ("", pal.Text));
        if (result is not null && IsStale) legend.Add(("Results are out of date: press Solve.", pal.Caution));

        var edge = Mode == FrameDrawMode.Model ? 64.0 : 86.0; // diagrams, labels and reaction arrows reach further out
        Layout(bounds.Value, size, edge, top: 10 + 17 * legend.Count + (Mode == FrameDrawMode.Model ? 50 : 80));

        // Selected member underneath, as a halo.
        foreach (var f in members.Where(f => f.MemberId == SelectedMemberId))
            dc.DrawLine(Pen(WithOpacity(pal.Accent, 0.4), 7), ToScreen(f.I), ToScreen(f.J));

        // Members.
        var memberPen = faint ? Pen(pal.Tertiary, 1.5) : Pen(pal.Text, 2.5);
        if (Mode == FrameDrawMode.Deformed && result is not null) memberPen = Pen(pal.Tertiary, 1.2, dashed: true);
        foreach (var f in members)
        {
            var (a, b) = (ToScreen(f.I), ToScreen(f.J));
            dc.DrawLine(memberPen, a, b);
            _hitMembers.Add((f.MemberId, a, b));
        }

        if (plotLoads is not null) DrawLoads(dc, model, members, plotLoads, pal);

        if (result is not null)
        {
            legend[resultLine] = Mode switch
            {
                FrameDrawMode.Deformed => DrawDeformed(dc, model, result, pal),
                FrameDrawMode.Reactions => DrawReactions(dc, model, result, pal),
                _ => DrawDiagram(dc, model, result, Quantity(Mode), pal),
            };
        }

        // Supports and nodes on top.
        foreach (var n in model.Nodes)
        {
            var p = ToScreen(new Point2(n.X, n.Y));
            if (n.Support.IsSupport()) DrawSupport(dc, model, n, p, pal);
            dc.DrawEllipse(pal.Text, null, p, 3.2, 3.2);
            if (n.Id == SelectedNodeId) dc.DrawEllipse(null, Pen(pal.Accent, 2.5), p, 8, 8);
            _hitNodes.Add((n.Id, p));
        }

        if (ShowLabels)
        {
            foreach (var n in model.Nodes)
                DrawText(dc, n.Id.ToString(CultureInfo.CurrentCulture), ToScreen(new Point2(n.X, n.Y)) + new Vector(7, -19), pal.Secondary, 11);
            foreach (var f in members)
            {
                var mid = ToScreen(f.At(f.Length / 2));
                var side = ScreenDir(f.AxisY) * 14;
                DrawTag(dc, f.MemberId.ToString(CultureInfo.CurrentCulture), mid + side, pal);
            }
        }

        var y = 8.0;
        foreach (var (text, brush) in legend)
        {
            DrawText(dc, text, new Point(12, y), brush, 12);
            y += 17;
        }
        DrawAxes(dc, new Point(18, size.Height - 18), pal);
    }

    /// <summary>
    /// Fits the node box into the space left by the margins, then applies the user's zoom and pan:
    /// screen = fitCentreScreen + pan + (p − fitCentre)·scale, with y flipped.
    /// </summary>
    private void Layout((Point2 Min, Point2 Max) b, Size size, double edge, double top)
    {
        var w = b.Max.X - b.Min.X;
        var h = b.Max.Y - b.Min.Y;
        var availW = Math.Max(size.Width - 2 * edge, 20);
        var availH = Math.Max(size.Height - top - edge, 20);
        // A line of nodes has no height (or width): only the other direction limits the scale. At least 1 m fits.
        var span = Math.Max(Math.Max(w, h), 1);
        _fitScale = Math.Min(availW / Math.Max(w, span * 0.01), availH / Math.Max(h, span * 0.01));
        _fitCentre = new Point2((b.Min.X + b.Max.X) / 2, (b.Min.Y + b.Max.Y) / 2);
        _fitCentreScreen = new Point(edge + availW / 2, top + availH / 2);
        _scale = _fitScale * _zoom;
        var c = _fitCentreScreen + _pan;
        _origin = new Point(c.X - _fitCentre.X * _scale, c.Y + _fitCentre.Y * _scale);
    }

    private static DiagramQuantity Quantity(FrameDrawMode mode) => mode switch
    {
        FrameDrawMode.Axial => DiagramQuantity.Axial,
        FrameDrawMode.Shear => DiagramQuantity.Shear,
        _ => DiagramQuantity.Moment,
    };

    private (string, Brush) DrawDiagram(DrawingContext dc, FrameModel model, ResultSet set, DiagramQuantity q, Palette pal)
    {
        var (title, unit) = q switch
        {
            DiagramQuantity.Axial => ("Axial force N (kN), tension +", "kN"),
            DiagramQuantity.Shear => ("Shear V (kN), V = dM/dx", "kN"),
            _ => ("Bending moment M (kN·m), drawn on the tension side", "kN·m"),
        };
        var max = FramePlot.MaxAbs(set, q);
        if (!(max > 1e-9)) return ($"{title}: zero everywhere in {set.Name}", pal.Text);

        var scale = DiagramPx / _scale / max;
        var pos = new SolidColorBrush(Positive);
        var neg = new SolidColorBrush(Negative);
        var posFill = new SolidColorBrush(Color.FromArgb(70, Positive.R, Positive.G, Positive.B));
        var negFill = new SolidColorBrush(Color.FromArgb(70, Negative.R, Negative.G, Negative.B));
        var diagrams = FramePlot.Diagram(model, set, q, scale);

        foreach (var d in diagrams)
        {
            foreach (var (run, sign) in FramePlot.SignRuns(d.Stations))
            {
                var isPos = sign >= 0;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(ToScreen(run[0].Base), true, true);
                    foreach (var s in run) ctx.LineTo(ToScreen(s.Tip), true, false);
                    ctx.LineTo(ToScreen(run[^1].Base), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(isPos ? posFill : negFill, null, g);
                var outline = new StreamGeometry();
                using (var ctx = outline.Open())
                {
                    ctx.BeginFigure(ToScreen(run[0].Base), false, false);
                    foreach (var s in run) ctx.LineTo(ToScreen(s.Tip), true, true);
                    ctx.LineTo(ToScreen(run[^1].Base), true, false);
                }
                outline.Freeze();
                dc.DrawGeometry(null, Pen(isPos ? pos : neg, 1.6), outline);
            }
        }

        if (ShowValues)
        {
            foreach (var d in diagrams)
            {
                var selected = d.MemberId == SelectedMemberId;
                foreach (var s in d.Labels)
                {
                    if (Math.Abs(s.Value) < max * 1e-6) continue;
                    var tip = ToScreen(s.Tip);
                    var outward = tip - ToScreen(s.Base);
                    if (outward.Length < 1e-9) continue;
                    outward.Normalize();
                    DrawValue(dc, Num(s.Value), tip + outward * 4, outward, s.Value >= 0 ? pos : neg, pal, selected);
                }
            }
        }
        return ($"{title}: {set.Name}  (largest |value| {Num(max)} {unit})", pal.Text);
    }


    private (string, Brush) DrawDeformed(DrawingContext dc, FrameModel model, ResultSet set, Palette pal)
    {
        var max = FramePlot.MaxDisplacement(set);
        if (!(max > 1e-12)) return ($"Deformed shape: no displacement in {set.Name}", pal.Text);
        var mag = FramePlot.NiceMagnification(max, DeformedPx / _scale);
        var pen = Pen(new SolidColorBrush(Positive), 2.2);
        foreach (var (_, pts) in FramePlot.Deformed(model, set, mag))
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(ToScreen(pts[0]), false, false);
                for (var k = 1; k < pts.Count; k++) ctx.LineTo(ToScreen(pts[k]), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        if (ShowValues)
        {
            foreach (var n in model.Nodes)
            {
                var r = set.Nodes.FirstOrDefault(x => x.NodeId == n.Id);
                if (r is null) continue;
                var moved = new Point2(n.X + mag * r.Ux / 1000, n.Y + mag * r.Uy / 1000);
                var p = ToScreen(moved);
                dc.DrawEllipse(pen.Brush, null, p, 3, 3);
                var d = Math.Sqrt(r.Ux * r.Ux + r.Uy * r.Uy);
                if (d < max * 1e-3) continue;
                DrawValue(dc, $"{Mm(r.Ux)}, {Mm(r.Uy)} mm", p + new Vector(6, 4), new Vector(1, 0), pen.Brush, pal, n.Id == SelectedNodeId);
            }
        }
        return ($"Deformed shape ×{mag.ToString("#,0.##", CultureInfo.CurrentCulture)}: {set.Name}  (largest {Mm(max)} mm" +
                (ShowValues ? "; node labels are ux, uy" : "") + ")", pal.Text);
    }

    private (string, Brush) DrawReactions(DrawingContext dc, FrameModel model, ResultSet set, Palette pal)
    {
        var brush = new SolidColorBrush(ReactionColor);
        foreach (var n in model.Nodes.Where(n => n.Support.IsSupport()))
        {
            var r = set.Nodes.FirstOrDefault(x => x.NodeId == n.Id);
            if (r is null) continue;
            var p = ToScreen(new Point2(n.X, n.Y));
            // Vertical reactions sit under the node, on the support side: pushing up into it, or pulling down from it.
            if (r.Rx is { } rx && Math.Abs(rx) > 1e-9) DrawForce(dc, p, new Vector(Math.Sign(rx), 0), $"{Num(Math.Abs(rx))} kN", brush, pal, gap: 10);
            if (r.Ry is { } ry && Math.Abs(ry) > 1e-9)
                DrawForce(dc, p, new Vector(0, -Math.Sign(ry)), $"{Num(Math.Abs(ry))} kN", brush, pal, gap: 10, pull: ry < 0);
            if (r.Mz is { } mz && Math.Abs(mz) > 1e-9) DrawMoment(dc, p, mz, $"{Num(Math.Abs(mz))} kN·m", brush, pal, 24);
        }
        return ($"Reactions (kN, kN·m) from the supports on the frame: {set.Name}", pal.Text);
    }

    // ---- Loads ----

    private void DrawLoads(DrawingContext dc, FrameModel model, IReadOnlyList<MemberFrame> members, PlotLoads loads, Palette pal)
    {
        var brush = new SolidColorBrush(LoadColor);
        foreach (var l in loads.Nodal)
        {
            var n = model.FindNode(l.NodeId);
            if (n is null) continue;
            var p = ToScreen(new Point2(n.X, n.Y));
            if (l.Fx != 0) DrawForce(dc, p, new Vector(Math.Sign(l.Fx), 0), $"{Num(Math.Abs(l.Fx))} kN", brush, pal, gap: 4);
            if (l.Fy != 0) DrawForce(dc, p, new Vector(0, -Math.Sign(l.Fy)), $"{Num(Math.Abs(l.Fy))} kN", brush, pal, gap: 4);
            if (l.Mz != 0) DrawMoment(dc, p, l.Mz, $"{Num(Math.Abs(l.Mz))} kN·m", brush, pal, 17);
        }
        foreach (var l in loads.Member)
        {
            var f = members.FirstOrDefault(x => x.MemberId == l.MemberId);
            if (f.Length <= 0) continue;
            if (l.Qy != 0) DrawLine(dc, f, new Vector(0, -Math.Sign(l.Qy)), $"{Num(Math.Abs(l.Qy))} kN/m", brush, pal);
            if (l.Qx != 0) DrawLine(dc, f, new Vector(Math.Sign(l.Qx), 0), $"{Num(Math.Abs(l.Qx))} kN/m", brush, pal);
        }
    }

    /// <summary>A uniform load in a global direction (<paramref name="dir"/>, screen unit vector): a row of arrows ending on the member.</summary>
    private void DrawLine(DrawingContext dc, MemberFrame f, Vector dir, string label, Brush brush, Palette pal)
    {
        var (a, b) = (ToScreen(f.I), ToScreen(f.J));
        var along = b - a;
        var lengthPx = along.Length;
        if (lengthPx < 4) return;
        along.Normalize();
        // A load along the member's own line would hide behind it: shift the row sideways.
        var shift = Math.Abs(Vector.CrossProduct(along, dir)) < 0.3 ? new Vector(-along.Y, along.X) * 12 : new Vector();
        var count = Math.Clamp((int)(lengthPx / 28), 2, 16);
        var pen = Pen(brush, 1.3);
        var tails = new List<Point>();
        for (var k = 0; k <= count; k++)
        {
            var tip = a + along * (lengthPx * k / count) + shift - dir * 2;
            var tail = tip - dir * LoadRowPx;
            dc.DrawLine(pen, tail, tip);
            DrawHead(dc, tip, dir, brush, 6);
            tails.Add(tail);
        }
        dc.DrawLine(pen, tails[0], tails[^1]);
        var mid = new Point((tails[0].X + tails[^1].X) / 2, (tails[0].Y + tails[^1].Y) / 2);
        DrawValue(dc, label, mid - dir * 3, -dir, brush, pal, false);
    }

    /// <summary>
    /// A force arrow pointing along <paramref name="dir"/> (screen unit vector): pushing, with its head at the node,
    /// or <paramref name="pull"/>ing, with its tail at the node.
    /// </summary>
    private void DrawForce(DrawingContext dc, Point node, Vector dir, string label, Brush brush, Palette pal, double gap, bool pull = false)
    {
        var (tail, tip) = pull ? (node + dir * gap, node + dir * (gap + ArrowPx)) : (node - dir * (gap + ArrowPx), node - dir * gap);
        dc.DrawLine(Pen(brush, 2), tail, tip);
        DrawHead(dc, tip, dir, brush, 8);
        var end = pull ? tip : tail; // label at the far end
        var outward = pull ? dir : -dir;
        DrawValue(dc, label, end + outward * 3, outward, brush, pal, false);
    }

    /// <summary>A moment as a ¾ arc around the node, arrow head showing the sense (counter-clockwise positive).</summary>
    private void DrawMoment(DrawingContext dc, Point node, double value, string label, Brush brush, Palette pal, double radius)
    {
        // Screen angles, measured clockwise because screen y points down; the arc spans from 135° to 405°.
        const double from = 135 * Math.PI / 180, to = 405 * Math.PI / 180;
        var ccw = value > 0;
        Point At(double t) => node + new Vector(Math.Cos(t), Math.Sin(t)) * radius;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(from), false, false);
            ctx.ArcTo(At(to), new Size(radius, radius), 0, true, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, Pen(brush, 1.8), g);
        // Going clockwise on screen from 135° to 405°; counter-clockwise (on screen, as in the model) means the head is at the start.
        var end = ccw ? from : to;
        var tangent = ccw ? new Vector(Math.Sin(end), -Math.Cos(end)) : new Vector(-Math.Sin(end), Math.Cos(end));
        DrawHead(dc, At(end), tangent, brush, 7);
        DrawValue(dc, label, node + new Vector(radius + 4, -radius - 2), new Vector(1, -1), brush, pal, false);
    }

    private static void DrawHead(DrawingContext dc, Point tip, Vector dir, Brush brush, double size)
    {
        var n = new Vector(-dir.Y, dir.X);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(tip - dir * size + n * size * 0.45, true, false);
            ctx.LineTo(tip - dir * size - n * size * 0.45, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(brush, null, g);
    }

    // ---- Supports ----

    private void DrawSupport(DrawingContext dc, FrameModel model, Node n, Point p, Palette pal)
    {
        var pen = Pen(pal.Text, 1.4);
        var s = SupportSize;
        // "Down" is towards the ground. Pins and vertical rollers sit below the node; a horizontal roller sits beside
        // it, away from its members; a fixed support sits opposite to where its members go.
        var away = AwayFromMembers(model, n);
        var down = n.Support switch
        {
            SupportType.RollerHorizontal => new Vector(away.X >= 0 ? 1 : -1, 0),
            SupportType.Fixed => Snap(away),
            _ => new Vector(0, 1),
        };
        var side = new Vector(-down.Y, down.X);

        if (n.Support == SupportType.Fixed)
        {
            var c = p;
            DrawGround(dc, c, down, side, s * 1.5, pen, hatch: true);
            dc.DrawLine(Pen(pal.Text, 3), c - side * s * 1.5, c + side * s * 1.5);
            return;
        }

        // Pin and rollers: a triangle with its apex on the node.
        var baseCentre = p + down * s * 1.25;
        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(p, true, true);
            ctx.LineTo(baseCentre + side * s, true, false);
            ctx.LineTo(baseCentre - side * s, true, false);
        }
        tri.Freeze();
        dc.DrawGeometry(pal.Card, pen, tri);

        var ground = baseCentre;
        if (n.Support is SupportType.RollerVertical or SupportType.RollerHorizontal)
        {
            const double r = 2.6;
            foreach (var k in new[] { -0.55, 0.55 })
                dc.DrawEllipse(pal.Card, pen, baseCentre + side * s * k + down * r, r, r);
            ground = baseCentre + down * (2 * r + 0.5);
        }
        DrawGround(dc, ground, down, side, s * 1.35, pen, hatch: true);
    }

    private static void DrawGround(DrawingContext dc, Point c, Vector down, Vector side, double half, Pen pen, bool hatch)
    {
        dc.DrawLine(pen, c - side * half, c + side * half);
        if (!hatch) return;
        var thin = new Pen(pen.Brush, 1);
        for (var t = -half; t <= half + 1e-9; t += half / 3)
        {
            var a = c + side * t;
            dc.DrawLine(thin, a, a + down * 6 - side * 5);
        }
    }

    /// <summary>Screen direction pointing away from the node's members (down when it has none or they cancel out).</summary>
    private static Vector AwayFromMembers(FrameModel model, Node n)
    {
        var sum = new Vector();
        foreach (var m in model.Members)
        {
            var other = m.NodeI == n.Id ? model.FindNode(m.NodeJ) : m.NodeJ == n.Id ? model.FindNode(m.NodeI) : null;
            if (other is null) continue;
            sum += ScreenDir(new Point2(other.X - n.X, other.Y - n.Y));
        }
        if (sum.Length < 1e-6) return new Vector(0, 1);
        sum.Normalize();
        return -sum;
    }

    private static Vector Snap(Vector v) => Math.Abs(v.X) > Math.Abs(v.Y) + 1e-9 ? new Vector(Math.Sign(v.X), 0) : new Vector(0, v.Y >= 0 ? 1 : -1);

    // ---- Text and helpers ----

    private void DrawAxes(DrawingContext dc, Point o, Palette pal)
    {
        var pen = Pen(pal.Tertiary, 1.2);
        dc.DrawLine(pen, o, o + new Vector(26, 0));
        DrawHead(dc, o + new Vector(28, 0), new Vector(1, 0), pal.Tertiary, 6);
        dc.DrawLine(pen, o, o + new Vector(0, -26));
        DrawHead(dc, o + new Vector(0, -28), new Vector(0, -1), pal.Tertiary, 6);
        DrawText(dc, "X", o + new Vector(31, -8), pal.Tertiary, 11);
        DrawText(dc, "Y", o + new Vector(-4, -46), pal.Tertiary, 11);
    }

    private FormattedText Text(string s, Brush brush, double size, bool bold = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawText(DrawingContext dc, string s, Point at, Brush brush, double size) => dc.DrawText(Text(s, brush, size), at);

    /// <summary>A value placed beside <paramref name="anchor"/> on the <paramref name="outward"/> side, on a backing plate.</summary>
    private void DrawValue(DrawingContext dc, string s, Point anchor, Vector outward, Brush brush, Palette pal, bool bold)
    {
        var t = Text(s, brush, 11, bold);
        // Move the box so its edge nearest the anchor touches it.
        var x = anchor.X + (outward.X > 0.3 ? 0 : outward.X < -0.3 ? -t.Width : -t.Width / 2);
        var y = anchor.Y + (outward.Y > 0.3 ? 0 : outward.Y < -0.3 ? -t.Height : -t.Height / 2);
        var box = new Rect(x - 2, y, t.Width + 4, t.Height);
        dc.DrawRoundedRectangle(WithOpacity(pal.SolidBackground, 0.75), null, box, 3, 3);
        dc.DrawText(t, new Point(x, y));
    }

    private void DrawTag(DrawingContext dc, string s, Point centre, Palette pal)
    {
        var t = Text(s, pal.Text, 11, bold: true);
        var box = new Rect(centre.X - t.Width / 2 - 4, centre.Y - t.Height / 2, t.Width + 8, t.Height);
        dc.DrawRoundedRectangle(pal.SolidBackground, Pen(pal.Tertiary, 1), box, 4, 4);
        dc.DrawText(t, new Point(box.X + 4, box.Y));
    }

    private static Pen Pen(Brush brush, double width, bool dashed = false)
    {
        var p = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (dashed) p.DashStyle = new DashStyle([5, 4], 0);
        p.Freeze();
        return p;
    }

    private static Brush WithOpacity(Brush b, double opacity)
    {
        var c = b.Clone();
        c.Opacity = opacity;
        c.Freeze();
        return c;
    }

    private static string Num(double v)
    {
        if (Math.Abs(v) < 0.005) return "0";
        return v.ToString(Math.Abs(v) >= 100 ? "0.0" : "0.00", CultureInfo.CurrentCulture);
    }

    /// <summary>Displacements (mm): three decimals below 1 mm, since frame displacements are often small.</summary>
    private static string Mm(double v)
    {
        if (Math.Abs(v) < 0.0005) return "0";
        return v.ToString(Math.Abs(v) >= 100 ? "0.0" : Math.Abs(v) >= 1 ? "0.00" : "0.000", CultureInfo.CurrentCulture);
    }

    // ---- Mouse ----

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_fitScale <= 0) return;
        var at = e.GetPosition(this);
        var under = ToModel(at);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.2, 50);
        // Keep the model point under the cursor where it is: solve Layout's mapping for the pan.
        var scale = _fitScale * _zoom;
        _pan = at - _fitCentreScreen - new Vector((under.X - _fitCentre.X) * scale, -(under.Y - _fitCentre.Y) * scale);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ClickCount == 2)
        {
            Fit();
            e.Handled = true;
            return;
        }
        _pressAt = e.GetPosition(this);
        _panAtPress = _pan;
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        if (_pressAt is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragging && (at - start).Length > 4) _dragging = true;
            if (_dragging)
            {
                _pan = _panAtPress + (at - start);
                Cursor = Cursors.SizeAll;
                InvalidateVisual();
            }
            return;
        }
        Cursor = HitNode(at) is not null || HitMember(at) is not null ? Cursors.Hand : null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var at = e.GetPosition(this);
        if (_pressAt is not null && !_dragging)
        {
            if (HitNode(at) is { } node) SelectedNodeId = node;
            else if (HitMember(at) is { } member) SelectedMemberId = member;
        }
        _pressAt = null;
        _dragging = false;
        Cursor = null;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private int? HitNode(Point at)
    {
        var best = _hitNodes.Where(n => (n.P - at).Length <= HitNodePx).OrderBy(n => (n.P - at).Length).FirstOrDefault();
        return best == default ? null : best.Id;
    }

    private int? HitMember(Point at)
    {
        int? id = null;
        var bestDistance = HitMemberPx;
        foreach (var (mid, a, b) in _hitMembers)
        {
            var ab = b - a;
            var t = Math.Clamp(Vector.Multiply(at - a, ab) / Math.Max(ab.LengthSquared, 1e-9), 0, 1);
            var d = (at - (a + ab * t)).Length;
            if (d <= bestDistance)
            {
                bestDistance = d;
                id = mid;
            }
        }
        return id;
    }
}
