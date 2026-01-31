using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Shogun.Avalonia.Converters;

/// <summary>
/// 足軽の中央ペイン（0～N/2-1）用：インデックスと足軽総数から表示するかを判定。
/// </summary>
public class CenterPaneVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int totalAshigaru || parameter is not string idxStr || !int.TryParse(idxStr, out var index))
            return false;

        var halfAshigaru = (totalAshigaru + 1) / 2;
        return index < halfAshigaru;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 足軽の右ペイン（N/2～N-1）用：インデックスと足軽総数から表示するかを判定。
/// </summary>
public class RightPaneVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int totalAshigaru || parameter is not string idxStr || !int.TryParse(idxStr, out var index))
            return false;

        var rightCount = totalAshigaru / 2;
        var leftCount = totalAshigaru - rightCount;
        return index >= 0 && index < rightCount;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
