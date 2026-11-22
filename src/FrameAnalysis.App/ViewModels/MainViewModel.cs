using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameAnalysis.Core.Analysis;
using FrameAnalysis.Core.Data;
using FrameAnalysis.Core.Loads;
using FrameAnalysis.Core.Model;
using Microsoft.Win32;

namespace FrameAnalysis.App.ViewModels;

public sealed record NodeRow(int Node, double Ux, double Uy, double Rz, string Rx, string Ry, string Mz);

public sealed record MemberRow(int Member, double Length, string Axis, double NI, double VI, double MI, double NJ, double VJ, double MJ,
    double MaxM, double XAtMaxM);

public sealed record StationRow(double X, double N, double V, double M, double U, double Vd, double Rotation);

/// <summary>One result set as seen by the S16 check of one member (unsigned values).</summary>
public sealed record EnvelopeRow(string Result, string Kind, double Tf, double Cf, double Vf, double Mf, double Ma, double Mb, double Mc,
    double Mi, double Mj, string Curvature);

/// <summary>
/// The whole test UI: inputs (the frame model) on the left, results on the right. Grid edits write straight into the
/// model's objects; adds and deletes go through <see cref="FrameModel"/> so the links stay consistent.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private FrameModel _model = new();
    private string? _filePath;

    public MainViewModel() => NewModel();

    // ---- Inputs ----

    public FrameSection Section => _model.Section;
    public FrameMaterial Material => _model.Material;

    [ObservableProperty] private ObservableCollection<Node> _nodes = [];
    [ObservableProperty] private ObservableCollection<Member> _members = [];
    [ObservableProperty] private ObservableCollection<LoadCase> _loadCases = [];
    [ObservableProperty] private ObservableCollection<NodalLoad> _nodalLoads = [];
    [ObservableProperty] private ObservableCollection<MemberLoad> _memberLoads = [];
    [ObservableProperty] private ObservableCollection<LoadCombination> _combinations = [];
    [ObservableProperty] private IReadOnlyList<int> _nodeIds = [];
    [ObservableProperty] private IReadOnlyList<int> _memberIds = [];

    public SupportType[] SupportTypes { get; } = Enum.GetValues<SupportType>();
    public BendingAxis[] BendingAxes { get; } = Enum.GetValues<BendingAxis>();
    public LoadCategory[] Categories { get; } = Enum.GetValues<LoadCategory>();
    public LimitState[] LimitStates { get; } = Enum.GetValues<LimitState>();

    [ObservableProperty] private Node? _selectedNode;
    [ObservableProperty] private Member? _selectedInputMember;
    [ObservableProperty] private LoadCase? _selectedLoadCase;
    [ObservableProperty] private NodalLoad? _selectedNodalLoad;
    [ObservableProperty] private MemberLoad? _selectedMemberLoad;
    [ObservableProperty] private LoadCombination? _selectedCombination;

    [ObservableProperty] private string _newNodeX = "0";
    [ObservableProperty] private string _newNodeY = "0";
    [ObservableProperty] private SupportType _newNodeSupport = SupportType.Free;
    [ObservableProperty] private int? _newMemberI;
    [ObservableProperty] private int? _newMemberJ;
    [ObservableProperty] private BendingAxis _newMemberAxis = BendingAxis.Major;
    [ObservableProperty] private string _newCaseName = "";
    [ObservableProperty] private LoadCategory _newCaseCategory = LoadCategory.D;

    /// <summary>Raised when the load cases change, so the window can rebuild the factor columns of the combinations grid.</summary>
    public event EventHandler? LoadCasesChanged;

    // ---- Messages ----

    [ObservableProperty] private bool _hasMessage;
    [ObservableProperty] private string _messageTitle = "";
    [ObservableProperty] private string _messageText = "";
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private string _windowTitle = "Frame Analysis";

    private void ShowError(string title, string text)
    {
        MessageTitle = title;
        MessageText = text;
        MessageIsError = true;
        HasMessage = true;
    }

    private void ShowInfo(string title, string text)
    {
        MessageTitle = title;
        MessageText = text;
        MessageIsError = false;
        HasMessage = true;
    }

    private void Try(string title, Action action)
    {
        try
        {
            action();
            HasMessage = false;
        }
        catch (ModelException ex)
        {
            ShowError(title, ex.Message);
        }
    }

    // ---- Results ----

    [ObservableProperty] private FrameResults? _results;
    [ObservableProperty] private IReadOnlyList<ResultSet> _resultSets = [];
    [ObservableProperty] private ResultSet? _selectedResult;
    [ObservableProperty] private IReadOnlyList<NodeRow> _nodeRows = [];
    [ObservableProperty] private IReadOnlyList<MemberRow> _memberRows = [];
    [ObservableProperty] private MemberRow? _selectedMemberRow;
    [ObservableProperty] private IReadOnlyList<StationRow> _stationRows = [];
    [ObservableProperty] private IReadOnlyList<EnvelopeRow> _envelopeRows = [];
    [ObservableProperty] private S16DesignForces? _designForces;
    [ObservableProperty] private bool _isStale;
    [ObservableProperty] private string _resultStatus = "Not solved yet.";

    public bool HasResults => Results is not null;

    partial void OnResultsChanged(FrameResults? value) => OnPropertyChanged(nameof(HasResults));

    /// <summary>Called by the window whenever the user edits an input, so old results are flagged.</summary>
    public void MarkInputsChanged()
    {
        if (Results is not null) IsStale = true;
    }

    partial void OnSelectedResultChanged(ResultSet? value)
    {
        var keep = SelectedMemberRow?.Member;
        if (value is null)
        {
            NodeRows = [];
            MemberRows = [];
            return;
        }
        NodeRows = value.Nodes.Select(n => new NodeRow(n.NodeId, n.Ux, n.Uy, n.Rz, Fmt(n.Rx), Fmt(n.Ry), Fmt(n.Mz))).ToList();
        MemberRows = value.Members.Select(m =>
        {
            var f = m.EndForces;
            var (max, x) = m.MaxAbsMoment;
            return new MemberRow(m.MemberId, m.Length, m.Axis == BendingAxis.Major ? "Major" : "Minor",
                f.NI, f.VI, f.MI, f.NJ, f.VJ, f.MJ, max, x);
        }).ToList();
        SelectedMemberRow = MemberRows.FirstOrDefault(r => r.Member == keep) ?? MemberRows.FirstOrDefault();
        UpdateMemberDetail();
    }

    partial void OnSelectedMemberRowChanged(MemberRow? value) => UpdateMemberDetail();

    private void UpdateMemberDetail()
    {
        if (SelectedResult is null || SelectedMemberRow is null || Results is null)
        {
            StationRows = [];
            EnvelopeRows = [];
            DesignForces = null;
            return;
        }
        var m = SelectedResult.Member(SelectedMemberRow.Member);
        var xs = Enumerable.Range(0, 9).Select(i => m.Length * i / 8).Append(m.MaxAbsMoment.X).Distinct().Order();
        StationRows = xs.Select(x =>
        {
            var f = m.ForcesAt(x);
            var d = m.DisplacementsAt(x);
            return new StationRow(x, f.N, f.V, f.M, d.U, d.V, d.Rotation);
        }).ToList();
        DesignForces = S16DesignForces.From(SelectedResult, m.MemberId);
        EnvelopeRows = Results.All.Select(set =>
        {
            var s = S16DesignForces.From(set, m.MemberId);
            var kind = set.Kind switch { ResultKind.Uls => "ULS", ResultKind.Sls => "SLS", _ => "Case" };
            return new EnvelopeRow(set.Name, kind, s.Tf, s.Cf, s.Vf, s.Mf, s.Ma, s.Mb, s.Mc, s.Mi, s.Mj, s.DoubleCurvature ? "Double" : "Single");
        }).ToList();
    }

    private static string Fmt(double? v) => v is null ? "" : v.Value.ToString("0.000", CultureInfo.CurrentCulture);

    [RelayCommand]
    private void Solve()
    {
        try
        {
            Results = FrameSolver.Analyze(_model);
            ResultSets = Results.All.ToList();
            var keep = SelectedResult?.Name;
            SelectedResult = ResultSets.FirstOrDefault(r => r.Name == keep) ?? ResultSets.FirstOrDefault();
            IsStale = false;
            ResultStatus = $"Solved at {DateTime.Now:HH:mm:ss}: {Results.Cases.Count} load cases, {Results.Combinations.Count} combinations.";
            HasMessage = false;
        }
        catch (ModelValidationException ex)
        {
            ShowError("The frame cannot be solved", string.Join(Environment.NewLine, ex.Errors));
        }
        catch (UnstableStructureException ex)
        {
            ShowError("Unstable structure", ex.Message);
        }
    }

    // ---- Model lifetime ----

    private void Load(FrameModel model, string? path)
    {
        _model = model;
        _filePath = path;
        OnPropertyChanged(nameof(Section));
        OnPropertyChanged(nameof(Material));
        Refresh();
        Results = null;
        ResultSets = [];
        SelectedResult = null;
        IsStale = false;
        ResultStatus = "Not solved yet.";
        WindowTitle = path is null ? "Frame Analysis" : $"Frame Analysis – {Path.GetFileName(path)}";
    }

    /// <summary>Re-reads the model's lists after an add or delete.</summary>
    private void Refresh()
    {
        Nodes = new(_model.Nodes);
        Members = new(_model.Members);
        LoadCases = new(_model.LoadCases);
        NodalLoads = new(_model.NodalLoads);
        MemberLoads = new(_model.MemberLoads);
        Combinations = new(_model.Combinations);
        NodeIds = _model.Nodes.Select(n => n.Id).ToList();
        MemberIds = _model.Members.Select(m => m.Id).ToList();
        foreach (var c in _model.Combinations)
            foreach (var lc in _model.LoadCases)
                c.Factors.TryAdd(lc.Id, 0); // every factor cell needs a value to bind to
        LoadCasesChanged?.Invoke(this, EventArgs.Empty);
        MarkInputsChanged();
    }

    /// <summary>A new frame starts with D (with self-weight), L, S and W cases and the NBCC combinations.</summary>
    [RelayCommand]
    private void NewModel()
    {
        var m = new FrameModel
        {
            Section = DefaultSection(),
            Material = new FrameMaterial { Name = "GR40.20/G40.21 350W", E = 200_000 },
        };
        m.AddLoadCase("D", LoadCategory.D, includeSelfWeight: true);
        m.AddLoadCase("L", LoadCategory.L);
        m.AddLoadCase("S", LoadCategory.S);
        m.AddLoadCase("W", LoadCategory.W);
        m.Combinations.AddRange(NbccCombinations.CreateDefaults(m.LoadCases));
        Load(m, null);
        HasMessage = false;
    }

    /// <summary>W250x49 from WBeamS16App's catalog; stands in for the task's active section.</summary>
    private static FrameSection DefaultSection() =>
        new() { Designation = "W250x49", Area = 6260, Ix = 70.6, Iy = 15.1, DeadLoad = 0.481 };

    /// <summary>A 6 m × 4 m fixed-base portal frame with a load in every case.</summary>
    [RelayCommand]
    private void LoadExample()
    {
        NewModel();
        var m = _model;
        var a = m.AddNode(0, 0, SupportType.Fixed);
        var b = m.AddNode(6, 0, SupportType.Fixed);
        var c = m.AddNode(0, 4);
        var d = m.AddNode(6, 4);
        m.AddMember(a.Id, c.Id);
        m.AddMember(b.Id, d.Id);
        var beam = m.AddMember(c.Id, d.Id);
        int Case(string name) => m.LoadCases.First(x => x.Name == name).Id;
        m.AddMemberLoad(beam.Id, Case("D"), 0, -5);
        m.AddMemberLoad(beam.Id, Case("L"), 0, -8);
        m.AddMemberLoad(beam.Id, Case("S"), 0, -4);
        m.AddNodalLoad(c.Id, Case("W"), 10, 0, 0);
        Load(m, null);
        ShowInfo("Example loaded", "Fixed-base portal frame, W250x49 (major axis) throughout. Press Solve.");
    }

    [RelayCommand]
    private void Open()
    {
        var dlg = new OpenFileDialog { Filter = FileFilter };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var (model, problems) = FrameModelJson.Load(File.ReadAllText(dlg.FileName));
            Load(model, dlg.FileName);
            if (problems.Count > 0) ShowError("The file has problems to fix before solving", string.Join(Environment.NewLine, problems));
            else HasMessage = false;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            ShowError("Could not open the file", ex.Message);
        }
    }

    [RelayCommand]
    private void Save()
    {
        var path = _filePath;
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = FileFilter, FileName = "frame.frame.json" };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }
        try
        {
            File.WriteAllText(path, FrameModelJson.Serialize(_model));
            _filePath = path;
            WindowTitle = $"Frame Analysis – {Path.GetFileName(path)}";
            var problems = _model.Validate();
            if (problems.Count > 0) ShowError("Saved, but the frame has problems to fix before solving", string.Join(Environment.NewLine, problems));
            else ShowInfo("Saved", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not save the file", ex.Message);
        }
    }

    private const string FileFilter = "Frame files (*.frame.json)|*.frame.json|JSON files (*.json)|*.json|All files (*.*)|*.*";

    // ---- Edits ----

    [RelayCommand]
    private void AddNode() => Try("Cannot add the node", () =>
    {
        if (!TryParse(NewNodeX, out var x) || !TryParse(NewNodeY, out var y))
            throw new ModelException("Type the X and Y coordinates in m.");
        _model.AddNode(x, y, NewNodeSupport);
        Refresh();
        SelectedNode = Nodes.LastOrDefault();
    });

    [RelayCommand]
    private void DeleteNode() => Try("Cannot delete the node", () =>
    {
        if (SelectedNode is null) return;
        _model.RemoveNode(SelectedNode.Id); // blocked while a member uses it
        Refresh();
    });

    [RelayCommand]
    private void AddMember() => Try("Cannot add the member", () =>
    {
        if (NewMemberI is null || NewMemberJ is null) throw new ModelException("Pick the start and end nodes.");
        _model.AddMember(NewMemberI.Value, NewMemberJ.Value, NewMemberAxis);
        Refresh();
        SelectedInputMember = Members.LastOrDefault();
    });

    [RelayCommand]
    private void DeleteMember() => Try("Cannot delete the member", () =>
    {
        if (SelectedInputMember is null) return;
        var loads = _model.MemberLoads.Count(l => l.MemberId == SelectedInputMember.Id);
        if (loads > 0 && !Confirm($"Member {SelectedInputMember.Id} carries {loads} distributed load(s). Delete it and its loads?")) return;
        _model.RemoveMember(SelectedInputMember.Id);
        Refresh();
    });

    [RelayCommand]
    private void AddLoadCase() => Try("Cannot add the load case", () =>
    {
        _model.AddLoadCase(NewCaseName, NewCaseCategory);
        NewCaseName = "";
        Refresh();
    });

    [RelayCommand]
    private void DeleteLoadCase() => Try("Cannot delete the load case", () =>
    {
        if (SelectedLoadCase is null) return;
        var loads = _model.NodalLoads.Count(l => l.LoadCaseId == SelectedLoadCase.Id) + _model.MemberLoads.Count(l => l.LoadCaseId == SelectedLoadCase.Id);
        if (!Confirm($"Delete load case \"{SelectedLoadCase.Name}\", its {loads} load(s) and its factors in every combination?")) return;
        _model.RemoveLoadCase(SelectedLoadCase.Id);
        Refresh();
    });

    [RelayCommand]
    private void AddNodalLoad() => Try("Cannot add the point load", () =>
    {
        var node = SelectedNode?.Id ?? _model.Nodes.FirstOrDefault()?.Id ?? throw new ModelException("Add a node first.");
        var lc = _model.LoadCases.FirstOrDefault()?.Id ?? throw new ModelException("Add a load case first.");
        _model.AddNodalLoad(node, lc, 0, 0, 0);
        Refresh();
        SelectedNodalLoad = NodalLoads.LastOrDefault();
    });

    [RelayCommand]
    private void DeleteNodalLoad()
    {
        if (SelectedNodalLoad is null) return;
        _model.NodalLoads.Remove(SelectedNodalLoad);
        Refresh();
    }

    [RelayCommand]
    private void AddMemberLoad() => Try("Cannot add the distributed load", () =>
    {
        var member = SelectedInputMember?.Id ?? _model.Members.FirstOrDefault()?.Id ?? throw new ModelException("Add a member first.");
        var lc = _model.LoadCases.FirstOrDefault()?.Id ?? throw new ModelException("Add a load case first.");
        _model.AddMemberLoad(member, lc, 0, 0);
        Refresh();
        SelectedMemberLoad = MemberLoads.LastOrDefault();
    });

    [RelayCommand]
    private void DeleteMemberLoad()
    {
        if (SelectedMemberLoad is null) return;
        _model.MemberLoads.Remove(SelectedMemberLoad);
        Refresh();
    }

    [RelayCommand]
    private void AddCombination()
    {
        var n = 1;
        while (_model.Combinations.Exists(c => c.Name == $"Combination {n}")) n++;
        _model.Combinations.Add(new LoadCombination { Name = $"Combination {n}", LimitState = LimitState.Uls });
        Refresh();
        SelectedCombination = Combinations.LastOrDefault();
    }

    [RelayCommand]
    private void DeleteCombination()
    {
        if (SelectedCombination is null) return;
        _model.Combinations.Remove(SelectedCombination);
        Refresh();
    }

    [RelayCommand]
    private void ResetCombinations()
    {
        if (_model.Combinations.Count > 0 && !Confirm("Replace every combination with the NBCC defaults for the current load cases?")) return;
        _model.Combinations.Clear();
        _model.Combinations.AddRange(NbccCombinations.CreateDefaults(_model.LoadCases));
        Refresh();
    }

    private static bool Confirm(string text) =>
        MessageBox.Show(text, "Frame Analysis", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static bool TryParse(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v) && double.IsFinite(v);
}
