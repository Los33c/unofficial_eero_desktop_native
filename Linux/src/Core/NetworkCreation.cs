using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public sealed class WiredNetworkSetup
    {
        public bool CreationUncertain { get; private set; }
        public string CreatedUrl { get; private set; }
        private string serial, location;
        public async Task Create(EeroApi api, object body, string serialNumber, string nodeLocation)
        {
            if (CreatedUrl != null || CreationUncertain) throw new InvalidOperationException("A creation request was already sent. Review the network in your account before starting another setup.");
            serial = RuntimeContracts.Serial(serialNumber); MainWindow.ValidateName(nodeLocation); location = nodeLocation.Trim();
            CreationUncertain = true;
            object created = await api.Send("POST", "/2.3/networks", body);
            Uri resource = EeroApi.Resource(J.S(created, "url"));
            if (!System.Text.RegularExpressions.Regex.IsMatch(resource.AbsolutePath, @"^/2\.[23]/networks/[A-Za-z0-9_-]+$") || resource.Query.Length != 0) throw new InvalidOperationException("The server did not return a valid created network. Refresh your account before continuing.");
            CreatedUrl = resource.AbsolutePath; CreationUncertain = false;
        }
        public async Task<string> Claim(EeroApi api)
        {
            if (CreatedUrl == null) throw new InvalidOperationException("Create a network first.");
            string destination = CreatedUrl;
            await api.Send("POST", "/2.2/eeros?requireSession=true", J.Obj("serial", serial, "location", location, "network", destination), true);
            CreatedUrl = serial = location = null;
            return destination;
        }
    }
}
