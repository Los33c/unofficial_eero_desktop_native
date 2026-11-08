using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Gtk;

namespace EeroDesktop
{
    public static class ProductImages
    {
        // Prefixes cover regional/channel variants documented by eero. Match the
        // hardware code first; a display label must never override a known model.
        private static readonly Dictionary<string, string> Codes = new Dictionary<string, string> {
            { "A01", "eero-gen1" }, { "B01", "eero-pro" }, { "D01", "eero-beacon" }, { "J01", "eero-gen2" },
            { "K01", "eero-pro-6" }, { "N01", "eero-6" }, { "Q01", "eero-6-extender" }, { "R01", "eero-6-plus" },
            { "S01", "eero-pro-6e" }, { "T01", "eero-poe-6" }, { "U01", "eero-poe-gateway" }, { "V01", "eero-max-7" },
            { "PA1", "eero-7" }, { "ME1", "eero-pro-7" }, { "SN1", "eero-outdoor-7" }, { "NV01", "eero-poe-7" },
            { "RG4", "eero-signal-4g" }, { "RG5", "eero-signal-5g" }, { "FH01", "eero-telephone-adapter" }
        };
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
            { "eeromax7", "eero-max-7" }, { "eeropro7", "eero-pro-7" }, { "eero7", "eero-7" }, { "eerooutdoor7", "eero-outdoor-7" },
            { "eeropoe7", "eero-poe-7" }, { "eeropoe6", "eero-poe-6" }, { "eeropoegateway", "eero-poe-gateway" },
            { "eeropro6e", "eero-pro-6e" }, { "eeropro6", "eero-pro-6" }, { "eero6+", "eero-6-plus" }, { "eero6plus", "eero-6-plus" },
            { "eero6+extender", "eero-6-plus-extender" }, { "eero6plusextender", "eero-6-plus-extender" },
            { "eero6extender", "eero-6-extender" }, { "eero6", "eero-6" }, { "eerobeacon", "eero-beacon" },
            { "eeropro", "eero-pro" }, { "eeropro2ndgen", "eero-pro" }, { "eero1stgen", "eero-gen1" }, { "eero1stgeneration", "eero-gen1" },
            { "eero2ndgen", "eero-gen2" }, { "eero2ndgeneration", "eero-gen2" }, { "eerosignal4glte", "eero-signal-4g" },
            { "eerosignal4g", "eero-signal-4g" }, { "eerosignal5g", "eero-signal-5g" },
            { "eerotelephoneadapter", "eero-telephone-adapter" }, { "eerofh01", "eero-telephone-adapter" }
        };
        private static readonly Dictionary<string, Gdk.Texture> Cache = new Dictionary<string, Gdk.Texture>();
        public static string Resolve(string model, string code)
        {
            string normalized = Regex.Replace(code ?? "", "[^a-zA-Z0-9]", "").ToUpperInvariant();
            foreach (var pair in Codes) if (normalized.StartsWith(pair.Key, StringComparison.Ordinal) && normalized.Length >= pair.Key.Length + (pair.Key.Length == 4 ? 0 : 1)) return pair.Value;
            normalized = Regex.Replace(model ?? "", "[\\s_()\\-]", "").ToLowerInvariant();
            string result; if (Names.TryGetValue(normalized, out result)) return result;
            // Some API versions put the hardware number in the model field.
            string modelCode = Regex.Replace(model ?? "", "[^a-zA-Z0-9]", "").ToUpperInvariant();
            foreach (var pair in Codes) if (modelCode.StartsWith(pair.Key, StringComparison.Ordinal) && modelCode.Length >= pair.Key.Length + (pair.Key.Length == 4 ? 0 : 1) && Regex.IsMatch(modelCode, "^[A-Z]{1,2}[0-9][A-Z0-9]{1,6}$")) return pair.Value;
            return null;
        }
        public static string Resolve(object node) { return Resolve(J.First(node, "model", "model_name", "product_name"), J.First(node, "model_number", "model_code", "hardware_model")); }
        public static byte[] Bytes(string id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EeroDesktop.Products." + id + ".png"))
            {
                if (stream == null) return null;
                using (var copy = new MemoryStream()) { stream.CopyTo(copy); return copy.ToArray(); }
            }
        }
        public static Gdk.Texture Load(string id)
        {
            if (String.IsNullOrEmpty(id)) return null; Gdk.Texture image;
            if (Cache.TryGetValue(id, out image)) return image;
            byte[] png = Bytes(id); if (png == null) return null;
            using (var bytes = GLib.Bytes.New(png)) image = Gdk.Texture.NewFromBytes(bytes);
            Cache[id] = image; return image;
        }
        public static string[] Ids { get { return Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith("EeroDesktop.Products.") && n.EndsWith(".png")).Select(n => n.Substring(21, n.Length - 25)).OrderBy(n => n).ToArray(); } }
    }

    public sealed partial class MainWindow
    {
        private static Widget HardwareImage(object node, double size = 54)
        {
            var source = ProductImages.Load(ProductImages.Resolve(node));
            if (source == null) return Glyph("", size * 0.5);
            var image = Gtk.Picture.NewForPaintable(source); image.SetContentFit(ContentFit.Contain); image.SetCanShrink(true);
            image.SetSizeRequest((int)size, (int)size); image.SetValign(Gtk.Align.Center);
            string model = J.First(node, "model", "model_name", "product_name");
            image.SetTooltipText(model); image.SetAlternativeText("Product image: " + model);
            return image;
        }
        private static string ProfileSummary(object profile)
        {
            string count = J.Get(profile, "devices") != null ? J.List(J.Get(profile, "devices")).Count + " devices · " : J.Get(profile, "device_count") != null ? J.S(profile, "device_count") + " devices · " : "";
            return count + (J.B(profile, "paused") ? "Paused" : "Internet enabled");
        }
        private Widget CompactNode(object node)
        {
            var text = VBox(); text.SetValign(Gtk.Align.Center); text.SetHexpand(true);
            var name = Text(J.First(node, "location", "model"), 14, "#17263C", true); name.SetMarginBottom(2); text.Append(name);
            var subtitle = Text((J.B(node, "gateway") ? "Gateway · " : "") + J.S(node, "model"), 12, "#64738A"); subtitle.SetMarginBottom(4); text.Append(subtitle);
            bool online = IsOnline(node); string count = J.S(node, "connected_clients_count");
            var state = Text((online ? "● Online" : "○ " + (J.S(node, "status").NullIfEmpty() ?? "Unknown")) + (count.Length > 0 ? " · " + count + " devices" : ""), 11, online ? "#0D7959" : "#64738A"); state.SetMarginBottom(0); text.Append(state);
            var actions = HBox(4); actions.SetValign(Gtk.Align.Center);
            var open = Button("View details", async delegate { selectedNode = preview ? node : await Read(J.S(node, "url")); currentPage = "Home/eeros/Details"; RenderPage(); }); Quiet(open); actions.Append(open);
            var rename = Button("Rename", async delegate { string name2 = await Prompt("eero location", J.S(node, "location")); if (name2 == null) return; ValidateName(name2); await Change("PUT", J.S(node, "url"), J.Obj("location", name2.Trim()), true, "eero location updated."); }); Quiet(rename); actions.Append(rename);
            var content = HBox(12); var picture = HardwareImage(node, 52); picture.SetSizeRequest(52, 52); content.Append(picture); content.Append(text); content.Append(actions);
            var frame = Card(content, 12); frame.SetMarginBottom(8);
            return frame;
        }
        private void ProductGalleryPage()
        {
            Sub("Bundled manufacturer imagery · matched by hardware number or model name.");
            var grid = FlowBox.New(); grid.SetSelectionMode(SelectionMode.None); grid.SetMinChildrenPerLine(2); grid.SetMaxChildrenPerLine(4); grid.SetHomogeneous(true); grid.SetColumnSpacing(12); grid.SetRowSpacing(12);
            foreach (string id in ProductImages.Ids)
            {
                var sample = VBox(); var picture = Gtk.Picture.NewForPaintable(ProductImages.Load(id)); picture.SetContentFit(ContentFit.Contain); picture.SetSizeRequest(110, 90); sample.Append(picture); sample.Append(Text(id, 12));
                var card = Card(sample, 16); card.SetMarginBottom(0); grid.Append(card);
            }
            page.Append(grid);
        }
    }
}
