using System;
using System.Globalization;
using System.Linq;
using Gtk;

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
        private void AddSeriesCharts(Widget target, object data, bool bytes)
        {
            var host = target as Box;
            if (host == null) throw new ArgumentException("Charts need a box to draw into.");
            var series = J.List(J.Get(data, "series"));
            if (series.Count == 0)
            {
                host.Append(Card(Text(data == null ? "eero hasn't returned any activity for this period. Choose Refresh at the top right to try again." : bytes ? "No data usage was recorded for this period." : "Nothing was blocked in this period.", 14, "#64738A"), 18));
                return;
            }
            foreach (object item in series.Take(12))
            {
                var values = J.List(J.Get(item, "values")).Take(60).ToList();
                var panel = VBox();
                var words = VBox(); var title = Text(SeriesTitle(item, bytes), 16, "#64738A", true); title.SetMarginBottom(0); words.Append(title);
                double total = J.Get(item, "sum") == null ? values.Sum(v => Number(J.Get(v, "value"))) : Number(J.Get(item, "sum"));
                var amount = Text(Amount(total, bytes) + (bytes ? "" : total == 1 ? " time" : " times"), 28, "#274CDB", true); amount.Margin(0, 2, 0, 0); words.Append(amount);
                Widget stats = null;
                if (values.Count > 1)
                {
                    object busiest = values.OrderByDescending(v => Number(J.Get(v, "value"))).First(); DateTime peak;
                    string peakDay = DateTime.TryParse(J.S(busiest, "time"), out peak) ? peak.ToString("ddd, MMM d") : J.S(busiest, "time");
                    var right = VBox(); right.SetHalign(Gtk.Align.End); right.SetValign(Gtk.Align.End);
                    var avg = Text("Daily average " + Amount(total / values.Count, bytes), 12, "#64738A"); avg.SetMarginBottom(0); avg.SetXalign(1); right.Append(avg);
                    var top = Text((bytes ? "Busiest day " : "Most on ") + peakDay, 12, "#64738A"); top.Margin(0, 2, 0, 0); top.SetXalign(1); right.Append(top);
                    stats = right;
                }
                var head = Split(words, stats); if (stats != null) stats.SetValign(Gtk.Align.End);
                panel.Append(head);
                double maximum = values.Count == 0 ? 1 : Math.Max(1, values.Max(v => Number(J.Get(v, "value"))));
                // One equal-width slot per day: the value, a Cairo-drawn bar in the accent color, then the date.
                var chart = Grid.New(); chart.SetColumnHomogeneous(true); chart.SetSizeRequest(-1, 196); chart.SetMarginTop(14); chart.SetHexpand(true);
                int slot = 0;
                foreach (object value in values)
                {
                    double n = Number(J.Get(value, "value")); string date = J.S(value, "time"), full = date; DateTime parsed;
                    if (DateTime.TryParse(date, out parsed)) { date = parsed.ToString(values.Count > 14 ? "dd" : "MMM d"); full = parsed.ToString("dddd, MMMM d"); }
                    var column = VBox(); column.SetValign(Gtk.Align.End); column.Margin(3, 0, 3, 0); column.SetTooltipText(full + ": " + Amount(n, bytes));
                    if (values.Count <= 14) column.Append(ChartLabel(Amount(n, bytes), 0, 4));
                    column.Append(ChartBar((int)Math.Round(Math.Max(2, 140 * n / maximum))));
                    column.Append(ChartLabel(date, 8, 0));
                    column.Name(full + ": " + Amount(n, bytes));
                    chart.Attach(column, slot++, 0, 1, 1);
                }
                panel.Append(chart); host.Append(Card(panel));
            }
        }
        private static Label ChartLabel(string text, int top, int bottom)
        {
            var label = Label.New(text); label.AddCssClass("fs-10"); label.AddCssClass("c-muted"); label.SetXalign(0.5f);
            label.SetEllipsize(Pango.EllipsizeMode.End); label.SetSingleLineMode(true); label.Margin(0, top, 0, bottom);
            return label;
        }
        // A rounded bar drawn with Cairo; the accent style class makes GetColor follow libadwaita's light/dark accent.
        private static Widget ChartBar(int height)
        {
            var area = DrawingArea.New(); area.AddCssClass("c-accent"); area.SetContentHeight(height); area.SetSizeRequest(3, height); area.SetHexpand(true);
            area.SetDrawFunc((widget, cr, width, h) =>
            {
                Gdk.RGBA color; widget.GetColor(out color);
                double barWidth = Math.Min(width, 72), x = (width - barWidth) / 2, radius = Math.Min(3, Math.Min(barWidth, h) / 2.0);
                cr.SetSourceRgba(color.Red, color.Green, color.Blue, color.Alpha);
                cr.NewSubPath();
                cr.Arc(x + barWidth - radius, radius, radius, -Math.PI / 2, 0);
                cr.Arc(x + barWidth - radius, h - radius, radius, 0, Math.PI / 2);
                cr.Arc(x + radius, h - radius, radius, Math.PI / 2, Math.PI);
                cr.Arc(x + radius, radius, radius, Math.PI, 3 * Math.PI / 2);
                cr.ClosePath();
                cr.Fill();
            });
            return area;
        }
    }
}
