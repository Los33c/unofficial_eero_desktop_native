using System;
using System.IO;
using System.Reflection;
using Gtk;

namespace EeroDesktop
{
    internal static class ApplicationIcon
    {
        // Installed packages put the icon in the hicolor theme. Portable runs (tarball, dev builds)
        // extract the embedded icon to the user's cache and add it to the icon search path.
        public static void Install()
        {
            try
            {
                var theme = IconTheme.GetForDisplay(Gdk.Display.GetDefault());
                if (theme.HasIcon(Program.AppId)) return;
                string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (String.IsNullOrEmpty(cache) || !Path.IsPathRooted(cache)) cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                string root = Path.Combine(cache, "eero-desktop", "icons");
                string target = Path.Combine(root, "hicolor", "256x256", "apps", Program.AppId + ".png");
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EeroDesktop.AppIcon.png"))
                    using (var file = File.Create(target)) stream.CopyTo(file);
                }
                theme.AddSearchPath(root);
            }
            catch (Exception) { }
        }
        public static byte[] Png()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EeroDesktop.AppIcon.png"))
            using (var copy = new MemoryStream()) { stream.CopyTo(copy); return copy.ToArray(); }
        }
    }

    // Saves a window's rendering to PNG once it has been laid out (used by --render for screenshots).
    internal static class Rendering
    {
        public static void SaveWhenReady(Gtk.Window window, string path, int width, int height, System.Action done)
        {
            window.SetDefaultSize(width, height);
            window.Present();
            int attempts = 0;
            GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, 400, delegate
            {
                attempts++;
                if (!window.GetMapped() && attempts < 25) return true;
                // Give images, fonts and the first layout pass a moment to settle.
                if (attempts < 4) return true;
                try { Save(window, path); }
                catch (Exception e) { Console.Error.WriteLine("Render failed: " + e.Message); }
                done();
                return false;
            });
        }
        public static void Save(Gtk.Widget widget, string path)
        {
            int w = widget.GetWidth(), h = widget.GetHeight();
            var paintable = WidgetPaintable.New(widget);
            var snapshot = Snapshot.New();
            paintable.Snapshot(snapshot, w, h);
            var node = snapshot.ToNode();
            if (node == null) throw new InvalidOperationException("Nothing was drawn.");
            var native = widget.GetNative();
            var renderer = native.GetRenderer();
            var texture = renderer.RenderTexture(node, null);
            if (!texture.SaveToPng(path)) throw new IOException("Could not write " + path);
        }
    }
}
