using System;
using System.Text;
using Windows.UI.Xaml.Data;
using PlayListManager;

namespace Repeater.Converters
{
    public class RepeatModeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var s = value.ToString();
            var ss = SplitString(s);
            return ss;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            var s = (string) value;
            var ms = MergeString(s);
            var mode = (RepeatModes)Enum.Parse(typeof (RepeatModes), ms);
            return mode;
        }

        public static string SplitString(string s)
        {
            if (s.Length < 1)
            {
                return "";
            }
            var sb = new StringBuilder();
            sb.Append(s[0]);
            for (var i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsUpper(c))
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string MergeString(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                if (c == ' ')
                {
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
