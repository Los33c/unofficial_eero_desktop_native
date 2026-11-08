using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private async Task CompleteExternalSignIn(Func<Task> action)
        {
            if (busy) throw new InvalidOperationException("Another sign-in operation is in progress. Wait for it to finish.");
            busy = true; if (controls != null) controls.SetSensitive(false);
            try { await action(); }
            finally { busy = false; if (controls != null) controls.SetSensitive(true); }
        }
        private async Task ProviderSignIn()
        {
            object data; using (var service = new SignInService()) data = await service.Providers();
            var choices = new Dictionary<string, string>();
            foreach (object provider in J.List(data)) { string name = J.S(provider, "name"), id = J.S(provider, "idp"); if (name.Length > 0 && id.Length > 0) choices[name] = id; }
            if (choices.Count == 0) throw new InvalidOperationException("eero did not return any sign-in providers.");
            Edit("Sign in through your internet provider", new[] { new Field("provider", "Provider", choices.Keys.OrderBy(x => x).First(), "choice", choices.Keys.OrderBy(x => x).ToArray()) }, async values =>
            {
                api.Remember = rememberBox == null || rememberBox.GetActive();
                ServiceBrowserWindow window = null;
                Func<Uri, Task> callback = async uri =>
                {
                    await CompleteExternalSignIn(async delegate {
                    var query = SignInContracts.Parameters(uri.Query); string candidate;
                    if (!query.TryGetValue("token", out candidate) || candidate.Length == 0) throw new InvalidOperationException("The provider did not return an eero sign-in token.");
                    VerifiedBrowserIdentity identity = await api.InspectBrowserSession(candidate, window.LifetimeToken); object verified = identity.Account;
                    window.LifetimeToken.ThrowIfCancellationRequested();
                    if (!await Confirm("Continue as " + J.First(verified, "name", "email", "phone") + "?\n" + J.First(verified, "email", "phone"))) throw new InvalidOperationException("Sign-in was cancelled.");
                    await api.ImportBrowserSession(identity.SessionCredential, window.LifetimeToken); ClearData(); await LoadAccount(); ShowShell(); SetNotice("Provider sign-in verified with eero."); });
                };
                window = new ServiceBrowserWindow(this, "Sign in with " + V(values, "provider"), SignInContracts.ProviderUrl(choices[V(values, "provider")]), null, null, false, SignInContracts.SsoReturn, callback, true);
                window.Present(); await Task.FromResult(0);
            }, "Continue", "Choose the provider associated with your eero account. Credentials are entered only on its sign-in page.");
        }
    }
}
