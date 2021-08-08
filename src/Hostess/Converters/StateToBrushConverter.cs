﻿using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Hostess.Converters
{
    public class StateToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = (value is bool?) ? (bool?)value : null;
            return flag.HasValue ? flag.Value ? Brushes.DarkGreen : Brushes.DarkRed : Brushes.DarkOrange;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
