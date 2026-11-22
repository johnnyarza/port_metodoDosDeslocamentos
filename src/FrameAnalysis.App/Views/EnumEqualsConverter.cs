using System.Globalization;
using System.Windows.Data;

namespace FrameAnalysis.App.Views;

/// <summary>Binds a group of radio buttons to one enum property: checked when the value equals the parameter.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Equals(value, parameter);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
