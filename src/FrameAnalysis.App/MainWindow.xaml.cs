using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using FrameAnalysis.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FrameAnalysis.App;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.LoadCasesChanged += (_, _) => RebuildFactorColumns();
        RebuildFactorColumns();
        Loaded += (_, _) => SystemThemeWatcher.Watch(this);

        // Any edit the user makes in the inputs flags the results as out of date. Bindings that fill the boxes do not
        // count: only a focused text box, an open or focused combo box, or a clicked check box.
        Inputs.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) =>
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox { IsKeyboardFocused: true }) _vm.MarkInputsChanged();
        }));
        Inputs.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) =>
        {
            if (e.OriginalSource is ComboBox c && (c.IsDropDownOpen || c.IsKeyboardFocusWithin)) _vm.MarkInputsChanged();
            ScheduleRedraw();
        }));
        Inputs.AddHandler(ToggleButton.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is CheckBox) _vm.MarkInputsChanged();
            ScheduleRedraw();
        }));

        // Grid cells write to the model when the edit is committed (Enter, Tab or leaving the cell), which is when the
        // cell's text box loses focus; repaint once the binding has done its work.
        Inputs.AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => ScheduleRedraw()));
    }

    private bool _redrawQueued;

    /// <summary>Repaints the drawing after pending bindings have updated the model; several requests make one repaint.</summary>
    private void ScheduleRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _redrawQueued = false;
            _vm.Redraw();
        });
    }

    private void FitDrawing_Click(object sender, RoutedEventArgs e) => FrameDrawing.Fit();

    public void OpenFile(string path) => _vm.OpenFile(path);

    /// <summary>One factor column per load case in the combinations grid, bound to Factors[caseId].</summary>
    private void RebuildFactorColumns()
    {
        const int fixedColumns = 3; // Run, Name, Limit state
        while (CombinationGrid.Columns.Count > fixedColumns) CombinationGrid.Columns.RemoveAt(fixedColumns);
        foreach (var lc in _vm.LoadCases)
        {
            CombinationGrid.Columns.Add(new DataGridTextColumn
            {
                Header = $"{lc.Name} ({lc.Category})",
                Binding = new Binding($"Factors[{lc.Id}]") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
                Width = new DataGridLength(80),
            });
        }
    }
}
