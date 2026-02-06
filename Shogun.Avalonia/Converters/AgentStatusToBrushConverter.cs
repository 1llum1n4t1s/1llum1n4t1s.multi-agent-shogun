using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Shogun.Avalonia.Converters;

/// <summary>
/// エージェントステータス文字列をバッジ背景色に変換するコンバーター。
/// idle=灰, active=緑, done=青, error=赤。
/// </summary>
public class AgentStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToLowerInvariant() switch
        {
            "active" => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),  // green
            "done"   => new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),  // blue
            "error"  => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),  // red
            _        => new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)),  // grey (idle)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
