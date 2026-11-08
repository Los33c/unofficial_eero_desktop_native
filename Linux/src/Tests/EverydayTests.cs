using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    internal static partial class Tests
    {
        private sealed class StartupFake : IStartupStore
        {
            public string Value; public int Writes;
            public string Read() { return Value; }
            public void Write(string value) { Value = value; Writes++; }
        }
        private static void EverydayChecks(string scratch)
        {
            Assert(J.Stringify(EverydayContracts.BackupOrder(new[] { "backup-2", "backup-1" })) == "{\"rearranged_ids\":[\"backup-2\",\"backup-1\"]}", "backup priorities preserve every UUID in the chosen order");
            bool invalid = false; try { EverydayContracts.BackupOrder(new[] { "same", "same" }); } catch (ArgumentException) { invalid = true; } Assert(invalid, "duplicate backup priority entries rejected before request");
            invalid = false; try { EverydayContracts.BackupOrder(new string[0]); } catch (ArgumentException) { invalid = true; } Assert(invalid, "empty backup reorder rejected");
            foreach (string symptom in new[] { "internet_slow", "internet_offline", "connection_drops", "eeros_red" }) Assert(J.S(EverydayContracts.Symptom(symptom), "symptom") == symptom && J.Get(EverydayContracts.Symptom(symptom), "device") == null, "network-wide diagnostics symptom " + symptom);
            invalid = false; try { EverydayContracts.Symptom("reboot"); } catch (ArgumentException) { invalid = true; } Assert(invalid, "diagnostics cannot substitute a restart action");
            Assert(J.List(J.Get(EverydayContracts.Offers(new[] { "product-1", "product-1", "product-2" }), "product_ids")).Count == 2, "offer request uses unique product_ids from eligible plans");
            Assert(J.Stringify(EverydayContracts.Referral("network-1", "product-2")) == "{\"product_id\":\"product-2\",\"network_id\":\"network-1\"}", "referral URL and email use API product_id/network_id contract");
            invalid = false; try { EverydayContracts.Offers(new[] { "bad\nproduct" }); } catch (ArgumentException) { invalid = true; } Assert(invalid, "control characters in returned product IDs rejected");
            var credentials = EverydayContracts.GuardianLogin(J.Obj("guardian_id", "sample-user", "guardian_secret", "sample-secret"));
            Assert(credentials["Guardian username"] == "sample-user" && credentials["Guardian password"] == "sample-secret", "Guardian account bridge uses returned provider credentials");
            invalid = false; try { EverydayContracts.GuardianLogin(J.Obj("guardian_id", "only-user")); } catch (InvalidOperationException) { invalid = true; } Assert(invalid, "incomplete Guardian credentials cannot appear as successful sign-in");
            credentials = EverydayContracts.ThreadCredentials(J.Obj("name", "Sample Thread", "master_key", "sample-key", "active_operational_dataset", "sample-dataset", "unrelated_secret", "excluded"));
            Assert(credentials.Count == 2 && !credentials.ContainsKey("unrelated secret"), "Thread reveal includes only explicit supported credential fields");

            // Linux start-at-login runs an absolute executable or AppImage path (Windows: a quoted .exe path).
            string executable = "/opt/Sample Folder/eero Desktop/eero-desktop";
            Assert(StartupRegistration.Command(executable) == "\"" + executable + "\" --background", "startup executable path is quoted with a fixed background argument");
            foreach (string path in new[] { "relative-app", "https://example.com/app", "/opt/test/bad\"app", "/opt/test/bad\napp", "/opt/test/eero-desktop.exe", "/opt/test/$(id)", "/opt/test\\app" })
            { invalid = false; try { StartupRegistration.Command(path); } catch (ArgumentException) { invalid = true; } Assert(invalid, "unsafe startup path rejected: " + path.Replace('\n', ' ')); }
            var startup = new StartupFake { Value = "previous-registration" }; bool saved = false;
            StartupRegistration.Apply(startup, executable, true, delegate { saved = true; });
            Assert(saved && startup.Value == StartupRegistration.Command(executable), "startup registration and preference save complete together");
            string before = startup.Value; invalid = false;
            try { StartupRegistration.Apply(startup, executable, false, delegate { throw new IOException("simulated save failure"); }); } catch (IOException) { invalid = true; }
            Assert(invalid && startup.Value == before, "failed preference save restores the previous startup registration");
            StartupRegistration.Apply(startup, executable, false, delegate { }); Assert(startup.Value == null, "disabling startup removes only this app's entry");
            // Linux counterpart of the Windows Run key: the XDG autostart entry, written to a scratch file here.
            string autostartFile = Path.Combine(scratch, "autostart", "eero-desktop.desktop"); var autostart = new AutostartStore(autostartFile);
            StartupRegistration.Apply(autostart, executable, true, delegate { });
            string entry = File.ReadAllText(autostartFile);
            Assert(autostart.Read() == StartupRegistration.Command(executable) && entry.StartsWith("[Desktop Entry]\n") && entry.Contains("\nType=Application\n") && entry.Contains("\nExec=" + StartupRegistration.Command(executable) + "\n"), "XDG autostart entry stores the startup command");
            StartupRegistration.Apply(autostart, executable, false, delegate { });
            Assert(!File.Exists(autostartFile) && autostart.Read() == null, "disabling startup deletes the XDG autostart entry");

            string journalPath = Path.Combine(scratch, "notifications.dat"); var journal = new NotificationJournal(journalPath);
            object first = J.Obj("uuid", "notice-1", "body", "sample-sensitive-body"); object second = J.Obj("uuid", "notice-2", "body", "second-body");
            Assert(journal.Observe("account-A", "/2.2/networks/1", new[] { first }).Count == 0, "first notification check seeds without announcing old history"); journal.Save();
            string encrypted = Encoding.UTF8.GetString(File.ReadAllBytes(journalPath));
            Assert(!encrypted.Contains("sample-sensitive-body") && !encrypted.Contains("notice-1") && !encrypted.Contains("account-A"), "persisted notification cursor is keyring-key encrypted and contains no plaintext account/events");
            journal = new NotificationJournal(journalPath);
            Assert(journal.Observe("account-A", "/2.2/networks/1", new[] { second, first }).Count == 1, "new notifications after process restart are detected");
            Assert(journal.Observe("account-A", "/2.2/networks/1", new[] { second, first }).Count == 0, "repeated notification history is not announced again");
            Assert(journal.Observe("account-B", "/2.2/networks/1", new[] { second, first }).Count == 0, "notification cursors are isolated by account");
            Assert(journal.Observe("account-A", "/2.2/networks/2", new[] { second, first }).Count == 0, "notification cursors are isolated by network");
            object third = J.Obj("uuid", "notice-3"); var fresh = journal.Observe("account-A", "/2.2/networks/1", new[] { third, second, first });
            Assert(fresh.Count == 1 && J.S(fresh[0], "uuid") == "notice-3", "only unseen notification identifiers produce alerts");
            journal.Save(); journal = new NotificationJournal(journalPath); Assert(journal.Observe("account-A", "/2.2/networks/1", new[] { third, second, first }).Count == 0, "cursor persistence suppresses duplicates on a second restart");
            File.WriteAllText(journalPath, "corrupt-cache"); journal = new NotificationJournal(journalPath);
            Assert(journal.Observe("account-A", "/2.2/networks/1", new[] { first }).Count == 0, "corrupt notification cache recovers with a quiet initial seed");
            invalid = false; try { journal.Observe("account-A", "https://outside.example/2.2/networks/1", new[] { first }); } catch (ArgumentException) { invalid = true; } Assert(invalid, "notification history cannot be scoped to an untrusted API resource");
            // The Windows AppInstance (named mutex) checks are InstanceChecks in LinuxTests.cs (GApplication over D-Bus).
        }
    }
}
