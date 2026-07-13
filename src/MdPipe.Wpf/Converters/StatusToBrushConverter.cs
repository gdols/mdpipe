using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MdPipe.Wpf.ViewModels;

namespace MdPipe.Wpf.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Muted = new((Color)ColorConverter.ConvertFromString("#64748B"));
    private static readonly SolidColorBrush Accent = new((Color)ColorConverter.ConvertFromString("#2563EB"));
    private static readonly SolidColorBrush Success = new((Color)ColorConverter.ConvertFromString("#16A34A"));
    private static readonly SolidColorBrush Danger = new((Color)ColorConverter.ConvertFromString("#DC2626"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        FileStatus.Pending => Muted,
        FileStatus.Converting => Accent,
        FileStatus.Done => Success,
        FileStatus.Error => Danger,
        _ => Muted
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
