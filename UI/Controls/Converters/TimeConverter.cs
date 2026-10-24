using Core.Librarys;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace UI.Controls.Converters
{
    [ValueConversion(typeof(object), typeof(string))]
    public class TimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Time.ToString(int.Parse(value.ToString()));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //  单向转换器：防御性返回，避免 TwoWay 场景下抛异常导致崩溃
            return null;
        }
    }
}
