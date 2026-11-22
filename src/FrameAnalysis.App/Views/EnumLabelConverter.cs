using System.Globalization;
using System.Windows.Data;
using FrameAnalysis.Core.Model;

namespace FrameAnalysis.App.Views;

/// <summary>Readable names for the enums shown in combo boxes.</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        SupportType s => s.Label(),
        BendingAxis a => a == BendingAxis.Major ? "Major (x–x, Ix)" : "Minor (y–y, Iy)",
        LimitState l => l == LimitState.Uls ? "ULS" : "SLS",
        LoadCategory c => c switch
        {
            LoadCategory.D => "D (dead)",
            LoadCategory.L => "L (live)",
            LoadCategory.S => "S (snow)",
            LoadCategory.W => "W (wind)",
            _ => c.ToString(),
        },
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
