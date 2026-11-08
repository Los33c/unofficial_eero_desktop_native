using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public sealed class VpnCleanupException : Exception
    { public VpnCleanupException() : base("VPN startup failed and automatic disconnect failed. Remove the eero tunnel with \"sudo wg-quick down\" or in your network settings.") { } }
    internal static class VpnTunnelLifecycle
    {
        public static void Start(Action install, Func<bool> running, Func<bool> handshake, Action stop, Action confirmed, int attempts, Action wait)
        {
            bool installed = false;
            try
            {
                install(); installed = true;
                for (int i = 0; i < attempts; i++) { wait(); if (handshake()) { confirmed(); return; } }
                throw new InvalidOperationException("Guardian did not complete a VPN handshake. The tunnel was removed to restore your connection.");
            }
            catch
            {
                if (installed || running()) { try { stop(); } catch (Exception) { throw new VpnCleanupException(); } }
                throw;
            }
        }
    }
}
