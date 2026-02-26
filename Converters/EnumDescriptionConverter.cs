using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System;
using Avalonia.Data.Converters;

namespace SystemTools.Converters;

public class EnumDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            var fieldInfo = enumValue.GetType().GetField(enumValue.ToString());
            var descriptionAttribute = fieldInfo?.GetCustomAttribute<DescriptionAttribute>();
            
            if (descriptionAttribute != null)
            {
                return descriptionAttribute.Description;
            }

            return enumValue switch
            {
                Models.ComponentSettings.NetworkDetectMode.Auto => "自动",
                Models.ComponentSettings.NetworkDetectMode.Icmp => "ICMP模式",
                Models.ComponentSettings.NetworkDetectMode.Http => "HTTP模式",
                _ => enumValue.ToString()
            };
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}