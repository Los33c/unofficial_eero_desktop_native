using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    internal static partial class Tests
    {
        private sealed class StorageFake : HttpMessageHandler
        {
            public bool Seen; public HttpStatusCode Status = HttpStatusCode.OK;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            {
                Assert(request.Method == HttpMethod.Put && request.RequestUri.Host == "sample.s3.us-east-1.amazonaws.com", "portal upload uses returned HTTPS S3 PUT");
                Assert(!request.Headers.Contains("Cookie") && !request.Headers.Contains("Authorization"), "eero authentication is never forwarded to asset storage");
                Assert(request.Headers.GetValues("x-amz-server-side-encryption").Single() == "aws:kms" && request.Headers.GetValues("x-amz-server-side-encryption-aws-kms-key-id").Single() == "test-kms-key", "portal upload preserves server KMS encryption headers");
                Assert(request.Content.Headers.ContentType.MediaType == "image/png" && (await request.Content.ReadAsByteArrayAsync()).Length == 8, "portal upload sends exact bytes and MIME type");
                Seen = true; return new HttpResponseMessage(Status);
            }
        }
        private static async Task ExtendedChecks()
        {
            Assert(ExtendedContracts.InviteCode("https://join.eero.com/invite?invite_code=sample_code-123") == "sample_code-123", "invitation link code is parsed without visiting the link");
            Assert(J.Stringify(ExtendedContracts.InviteResponse("sample_code-123", false)) == "{\"invite_code\":\"sample_code-123\",\"accept\":false}", "decline invitation uses API boolean response contract");
            bool invalid = false; try { ExtendedContracts.InviteCode("https://join.eero.com.evil.example/?code=sample_code-123"); } catch (ArgumentException) { invalid = true; } Assert(invalid, "lookalike invitation hosts are rejected");
            invalid = false; try { ExtendedContracts.Identifier("1/../../account"); } catch (ArgumentException) { invalid = true; } Assert(invalid, "membership identifiers cannot alter API paths");
            object validate = ExtendedContracts.License("TEST-LICENSE"), activate = ExtendedContracts.License("TEST-LICENSE", "17");
            Assert(J.Get(validate, "network_id") == null && J.S(validate, "software_key") == "TEST-LICENSE", "license validation does not activate for a network");
            Assert(!J.B(activate, "auto_renew") && J.S(activate, "network_id") == "17", "license activation defaults to renewal disabled");
            object isp = ExtendedContracts.Pppoe(true, "user", "private-test-password", true, "100");
            Assert(J.S(isp, "pppoe", "credentials", "username") == "user" && J.S(isp, "vlan") == "100", "existing-network PPPoE and VLAN contract");
            Assert(J.Get(ExtendedContracts.Pppoe(true, "", "", false, ""), "pppoe", "credentials") == null, "retaining PPPoE credentials never submits empty passwords");
            invalid = false; try { ExtendedContracts.Pppoe(true, "", "", true, "100"); } catch (ArgumentException) { invalid = true; } Assert(invalid, "new PPPoE credentials must be complete");
            invalid = false; try { ExtendedContracts.Pppoe(false, "", "", false, "4095"); } catch (ArgumentException) { invalid = true; } Assert(invalid, "invalid ISP VLAN IDs are rejected");
            Assert(J.List(J.Get(ExtendedContracts.DnsPerformance("1.1.1.1\n2606:4700:4700::1111", 53), "servers")).Count == 2, "DNS performance accepts IPv4 and IPv6 resolver entries");
            invalid = false; try { ExtendedContracts.DnsPerformance("https://resolver.example", 53); } catch (ArgumentException) { invalid = true; } Assert(invalid, "DNS performance rejects URLs before making requests");
            byte[] png = { 137, 80, 78, 71, 13, 10, 26, 10 };
            Assert(ExtendedContracts.PortalMime("logo.png", png, true) == "image/png", "portal upload verifies MIME from the file signature");
            invalid = false; try { ExtendedContracts.PortalMime("terms.pdf", png, false); } catch (ArgumentException) { invalid = true; } Assert(invalid, "renaming a non-PDF does not bypass terms file validation");
            object job = ExtendedContracts.ImportJob("uploads/test/logo.png", true, "5");
            Assert(J.S(job, "import_job_type") == "CaptivePortalLogo" && J.S(job, "metadata") == "{\"configuration-id\":\"5\"}", "portal import uses API job type and serialized configuration metadata");
            foreach (string url in new[] { "http://sample.s3.amazonaws.com/x", "https://sample.s3.amazonaws.com.evil.example/x", "https://api-user.e2ro.com/2.2/account", "https://user@sample.s3.amazonaws.com/x", "https://127.0.0.1/x", "https://sample.s3.amazonaws.com:444/x" })
            { invalid = false; try { ExtendedContracts.UploadUrl(url); } catch (InvalidDataException) { invalid = true; } Assert(invalid, "untrusted storage URL rejected: " + url); }
            object upload = J.Obj("signed_upload_url", "https://sample.s3.us-east-1.amazonaws.com/logo?signature=fake", "kms_key_id", "test-kms-key");
            var storage = new StorageFake(); await PortalUpload.Send(upload, png, "image/png", storage); Assert(storage.Seen, "isolated asset upload completed");
            storage = new StorageFake { Status = HttpStatusCode.Redirect }; invalid = false;
            try { await PortalUpload.Send(upload, png, "image/png", storage); } catch (InvalidOperationException) { invalid = true; } Assert(invalid, "redirected uploads fail before import creation");
        }
        // GTK half of the theme checks (needs a display; see GtkChecks). WPF compared brushes; here the
        // controls are rendered and the contrast is measured between their background and text pixels.
        private static void ThemeChecks(Adw.Application app, MainWindow preview)
        {
            var store = Gtk.StringList.New(new[] { "Automatic", "Manual" });
            var table = Gtk.ColumnView.New(Gtk.SingleSelection.New(store)); table.AddCssClass("data-table");
            var factory = Gtk.SignalListItemFactory.New();
            factory.OnSetup += (sender, args) => { var item = (Gtk.ListItem)args.Object; var label = Gtk.Label.New(""); label.SetXalign(0); item.SetChild(label); };
            factory.OnBind += (sender, args) => { var item = (Gtk.ListItem)args.Object; var text = item.GetItem() as Gtk.StringObject; var label = item.GetChild() as Gtk.Label; if (text != null && label != null) label.SetText(text.GetString()); };
            table.AppendColumn(Gtk.ColumnViewColumn.New("Mode", factory));
            var entry = Gtk.Entry.New(); entry.SetText("Example");
            var password = Gtk.PasswordEntry.New(); password.SetText("sample");
            var dropdown = Gtk.DropDown.NewFromStrings(new[] { "Automatic", "Manual" }); dropdown.SetSelected(0);
            var controls = new Gtk.Widget[] { entry, password, dropdown, table };
            var panel = Gtk.Box.New(Gtk.Orientation.Vertical, 12); panel.SetMarginTop(24); panel.SetMarginBottom(24); panel.SetMarginStart(24); panel.SetMarginEnd(24);
            foreach (var control in controls) panel.Append(control);
            var frame = Adw.ApplicationWindow.New(app); frame.SetDefaultSize(800, 600); frame.SetContent(panel);
            frame.Present(); PumpUntil(() => frame.GetMapped() && table.GetHeight() > 0, 10000); Pump(150);
            try
            {
                foreach (string theme in new[] { "Dark", "Light", "Dark" })
                {
                    SwitchTheme(theme, preview); Pump(100);
                    Assert(controls.All(c => ThemeVerification.Contrast(c) >= 4.5), theme + " form text has readable contrast after theme switching");
                    if (theme == "Dark") NoWhite(ThemeVerification.HasWhiteControlSurface(frame), "dark fields, password boxes, dropdowns and tables have no white fallback backgrounds");
                }
                // Linux-only: prove the pixel checks above are not vacuous by forcing the faults they look for.
                var faults = Gtk.CssProvider.New();
                faults.LoadFromString(".selftest-white { background: #ffffff; color: #17263c; } .selftest-dim { color: #34343a; }");
                Gtk.StyleContext.AddProviderForDisplay(Gdk.Display.GetDefault(), faults, 900);
                try
                {
                    var whiteField = Gtk.Entry.New(); whiteField.SetText("White field"); whiteField.AddCssClass("selftest-white");
                    var whiteCard = Gtk.Box.New(Gtk.Orientation.Vertical, 0); whiteCard.AddCssClass("card"); whiteCard.AddCssClass("selftest-white"); whiteCard.SetSizeRequest(160, 60); whiteCard.Append(Gtk.Label.New("White card"));
                    var dimField = Gtk.Entry.New(); dimField.SetText("Dim text"); dimField.AddCssClass("selftest-dim");
                    panel.Append(whiteField); panel.Append(whiteCard); panel.Append(dimField); Pump(150);
                    Assert(ThemeVerification.HasWhiteControlSurface(frame) && ThemeVerification.HasWhiteCardSurface(frame), "white-surface detector flags a forced white field and card in dark mode");
                    Assert(ThemeVerification.Contrast(dimField) < 4.5 && ThemeVerification.Contrast(entry) >= 4.5, "contrast meter flags dim text on a dark field");
                }
                finally { Gtk.StyleContext.RemoveProviderForDisplay(Gdk.Display.GetDefault(), faults); }
            }
            finally { frame.Destroy(); }
            Isolate("theme preview page", delegate
            {
                preview.PreviewPage("Theme preview"); Pump(80);
                NoWhite(ThemeVerification.HasWhiteControlSurface(preview.Window), "selected and alternating sample table rows retain dark surfaces");
            });
        }
    }
}
