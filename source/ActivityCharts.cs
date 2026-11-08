using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private static double Number(object value) { double n; return Double.TryParse(J.S(value), NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? Math.Max(0, n) : 0; }
        private static string Amount(double value, bool bytes)
        {
            if (!bytes) return value.ToString("N0");
            if (value >= 1000000000) return (value / 1000000000).ToString("0.##") + " GB";
            if (value >= 1000000) return (value / 1000000).ToString("0.##") + " MB";
            if (value >= 1000) return (value / 1000).ToString("0.##") + " kB";
            return value.ToString("0") + " B";
        }
        // Plain titles for the series eero returns in usage and security insight charts.
        private static string SeriesTitle(object item, bool bytes)
        {
            string type = J.First(item, "insight_type_name", "type", "insight_type"), key = type.ToLowerInvariant();
            if (key == "download") return "Downloaded";
            if (key == "upload") return "Uploaded";
            if (!bytes && key.Length > 0 && !key.Contains("block")) return Human(type) + " blocked";
            return key.Length > 0 ? Human(type) : (bytes ? "Data used" : "Blocked");
        }
        private void AddSeriesCharts(Panel target, object data, bool bytes)
        {
            var series = J.List(J.Get(data, "series"));
            if (series.Count == 0)
            {
                target.Children.Add(Card(Text(data == null ? "eero hasn't returned any activity for this period. Choose Refresh at the top right to try again." : bytes ? "No data usage was recorded for this period." : "Nothing was blocked in this period.", 14, "#64738A"), 18));
                return;
            }
            foreach (object item in series.Take(12))
            {
                var values = J.List(J.Get(item, "values")).Take(60).ToList();
                var panel = new StackPanel();
                var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var words = new StackPanel(); var title = Text(SeriesTitle(item, bytes), 16, "#64738A", true); title.Margin = new Thickness(0); words.Children.Add(title);
                double total = J.Get(item, "sum") == null ? values.Sum(v => Number(J.Get(v, "value"))) : Number(J.Get(item, "sum"));
                var amount = Text(Amount(total, bytes) + (bytes ? "" : total == 1 ? " time" : " times"), 28, "#274CDB", true); amount.Margin = new Thickness(0, 2, 0, 0); words.Children.Add(amount); head.Children.Add(words);
                if (values.Count > 1)
                {
                    object busiest = values.OrderByDescending(v => Number(J.Get(v, "value"))).First(); DateTime peak;
                    string peakDay = DateTime.TryParse(J.S(busiest, "time"), out peak) ? peak.ToString("ddd, MMM d") : J.S(busiest, "time");
                    var stats = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
                    var avg = Text("Daily average " + Amount(total / values.Count, bytes), 12, "#64738A"); avg.Margin = new Thickness(0); avg.TextAlignment = TextAlignment.Right; stats.Children.Add(avg);
                    var top = Text((bytes ? "Busiest day " : "Most on ") + peakDay, 12, "#64738A"); top.Margin = new Thickness(0, 2, 0, 0); top.TextAlignment = TextAlignment.Right; stats.Children.Add(top);
                    Grid.SetColumn(stats, 1); head.Children.Add(stats);
                }
                panel.Children.Add(head);
                double maximum = values.Count == 0 ? 1 : Math.Max(1, values.Max(v => Number(J.Get(v, "value"))));
                var chart = new UniformGrid { Columns = Math.Max(1, values.Count), Height = 196, Margin = new Thickness(0, 14, 0, 0) };
                foreach (object value in values)
                {
                    double n = Number(J.Get(value, "value")); string date = J.S(value, "time"), full = date; DateTime parsed;
                    if (DateTime.TryParse(date, out parsed)) { date = parsed.ToString(values.Count > 14 ? "dd" : "MMM d"); full = parsed.ToString("dddd, MMMM d"); }
                    var column = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 3, 0), ToolTip = full + ": " + Amount(n, bytes), Background = System.Windows.Media.Brushes.Transparent };
                    if (values.Count <= 14) column.Children.Add(new TextBlock { Text = Amount(n, bytes), FontSize = 10, Foreground = Brush("#64738A"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis });
                    column.Children.Add(new Rectangle { Fill = Brush("#274CDB"), Height = Math.Max(2, 140 * n / maximum), MinWidth = 3, MaxWidth = 72, HorizontalAlignment = HorizontalAlignment.Stretch, RadiusX = 3, RadiusY = 3 });
                    column.Children.Add(new TextBlock { Text = date, FontSize = 10, Foreground = Brush("#64738A"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }); chart.Children.Add(column);
                }
                panel.Children.Add(chart); target.Children.Add(Card(panel));
            }
        }
    }
}
