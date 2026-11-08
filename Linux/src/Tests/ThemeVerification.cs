// GTK counterpart of the Windows ThemeVerification helper. WPF exposed each control's brush; GTK keeps
// colors inside its CSS engine, so these checks render the widget and inspect the pixels instead.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Gtk;

namespace EeroDesktop
{
    public static class ThemeVerification
    {
        // Rendered RGBA pixels of one widget, composited over the window background of the current theme.
        public sealed class Pixels
        {
            public int Width, Height; public byte[] Rgb;
            public IEnumerable<int[]> All() { for (int i = 0; i + 2 < Rgb.Length; i += 3) yield return new[] { (int)Rgb[i], Rgb[i + 1], Rgb[i + 2] }; }
        }
        private static IEnumerable<Widget> Descendants(Widget node)
        {
            for (Widget child = node.GetFirstChild(); child != null; child = child.GetNextSibling())
            {
                yield return child;
                foreach (Widget item in Descendants(child)) yield return item;
            }
        }
        // Approximate libadwaita window backgrounds; translucent surfaces are judged as they appear on the page.
        private static int[] Base() { return UiPreferences.Dark ? new[] { 0x22, 0x22, 0x26 } : new[] { 0xFA, 0xFA, 0xFB }; }
        public static Pixels Capture(Widget widget)
        {
            if (widget == null || !widget.GetMapped()) return null;
            int w = widget.GetWidth(), h = widget.GetHeight(); if (w < 2 || h < 2) return null;
            var native = widget.GetNative(); if (native == null) return null;
            var paintable = WidgetPaintable.New(widget); var snapshot = Snapshot.New();
            paintable.Snapshot(snapshot, w, h);
            var node = snapshot.ToNode(); if (node == null) return null;
            // Render the widget's whole box: without a viewport GSK crops to what was drawn (a lone text caret, say).
            var viewport = Graphene.Rect.Alloc().Init(0, 0, w, h);
            var texture = native.GetRenderer().RenderTexture(node, viewport);
            var downloader = Gdk.TextureDownloader.New(texture); downloader.SetFormat(Gdk.MemoryFormat.R8g8b8a8);
            nuint stride; var bytes = downloader.DownloadBytes(out stride);
            int tw = texture.GetWidth(), th = texture.GetHeight(); int size = (int)bytes.GetSize();
            var raw = new byte[size]; Marshal.Copy(bytes.GetRegion(1, 0, (nuint)size), raw, 0, size);
            int[] b = Base(); var rgb = new byte[tw * th * 3];
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    int s = y * (int)stride + x * 4, d = (y * tw + x) * 3; double a = raw[s + 3] / 255.0;
                    for (int c = 0; c < 3; c++) rgb[d + c] = (byte)Math.Round(raw[s + c] * a + b[c] * (1 - a));
                }
            return new Pixels { Width = tw, Height = th, Rgb = rgb };
        }
        // A surface counts as white when most of it renders near-white (text alone never covers that much).
        private static bool White(Widget widget)
        {
            Pixels p = Capture(widget); if (p == null) return false;
            int total = 0, white = 0;
            foreach (int[] c in p.All()) { total++; if (c[0] > 235 && c[1] > 235 && c[2] > 235) white++; }
            bool found = total > 0 && white > total / 2;
            string shots = Environment.GetEnvironmentVariable("EERO_TEST_SHOTS");
            if (found && !String.IsNullOrEmpty(shots)) { try { Rendering.Save(widget, System.IO.Path.Combine(shots, "offender-" + (++shotCount) + ".png")); Rendering.Save((Widget)widget.GetNative(), System.IO.Path.Combine(shots, "offender-" + shotCount + "-window.png")); } catch (Exception) { } }
            if (found) LastOffender =widget.GetType().Name + " ." + String.Join(".", widget.GetCssClasses() ?? new string[0]) + " " + widget.GetWidth() + "x" + widget.GetHeight() + " (" + (100 * white / total) + "% near-white)";
            return found;
        }
        // Describes the last widget the white-surface checks flagged, for failure reports.
        public static string LastOffender;
        private static int shotCount;
        private static bool IsControl(Widget w)
        {
            return w is Entry || w is PasswordEntry || w is SearchEntry || w is SpinButton || w is DropDown || w is TextView || w is ColumnView || w is ListView || w is Scrollbar;
        }
        public static bool HasWhiteControlSurface(Widget root)
        {
            return Descendants(root).Where(IsControl).Any(White);
        }
        public static bool HasWhiteCardSurface(Widget root)
        {
            return Descendants(root).Where(w => w.HasCssClass("card") || w.HasCssClass("boxed-list") || w.HasCssClass("view")).Any(White);
        }
        private static double Luminance(int[] c)
        {
            double[] k = { 0.2126, 0.7152, 0.0722 }; double sum = 0;
            for (int i = 0; i < 3; i++) { double v = c[i] / 255.0; v = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); sum += v * k[i]; }
            return sum;
        }
        // WCAG contrast between a control's background (its most common color) and its strongest text pixel.
        public static double Contrast(Widget control)
        {
            Pixels p = Capture(control); if (p == null) return 0;
            var counts = new Dictionary<int, int>(); var colors = new Dictionary<int, int[]>();
            foreach (int[] c in p.All()) { int key = (c[0] << 16) | (c[1] << 8) | c[2]; int n; counts.TryGetValue(key, out n); counts[key] = n + 1; colors[key] = c; }
            if (counts.Count == 0) return 0;
            double background = Luminance(colors[counts.OrderByDescending(x => x.Value).First().Key]);
            double text = colors.Values.Select(Luminance).OrderByDescending(l => Math.Abs(l - background)).First();
            return (Math.Max(text, background) + 0.05) / (Math.Min(text, background) + 0.05);
        }
    }
}
