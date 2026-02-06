using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Shogun.Avalonia.Converters;

/// <summary>
/// AI処理中フラグを色付きドットのブラシに変換するコンバーター。
/// true（処理中）= 緑、false（待機中）= 灰。
/// </summary>
public class StatusDotColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isProcessing)
        {
            return isProcessing
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))  // green
                : new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)); // grey
        }
        return new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
