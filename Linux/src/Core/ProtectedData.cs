using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace EeroDesktop
{
    public enum DataProtectionScope { CurrentUser = 0, LocalMachine = 1 }

    // Linux counterpart of Windows DPAPI. Data is sealed with AES-256-GCM under a
    // random per-user key that lives in the desktop keyring (Secret Service: GNOME
    // Keyring, KWallet, KeePassXC...). Files written with it are useless without
    // the user's unlocked keyring, like DPAPI files are without the Windows account.
    public static class ProtectedData
    {
        private const byte Version = 1;
        private const int NonceSize = 12, TagSize = 16, KeySize = 32;
        private static readonly object Sync = new object();
        private static byte[] cachedKey;
        // Self-tests replace the key source so they never touch the user's keyring.
        internal static Func<byte[]> KeyProvider = KeyringKey.LoadOrCreate;
        internal static void ResetCache() { lock (Sync) { if (cachedKey != null) Array.Clear(cachedKey, 0, cachedKey.Length); cachedKey = null; } }

        private static byte[] Key()
        {
            lock (Sync)
            {
                if (cachedKey == null)
                {
                    byte[] key;
                    try { key = KeyProvider(); }
                    catch (CryptographicException) { throw; }
                    catch (Exception e) { throw new CryptographicException("The desktop keyring is not available: " + e.Message, e); }
                    if (key == null || key.Length != KeySize) throw new CryptographicException("The desktop keyring returned an unusable key.");
                    cachedKey = key;
                }
                return cachedKey;
            }
        }
        public static byte[] Protect(byte[] userData, byte[] optionalEntropy, DataProtectionScope scope)
        {
            if (userData == null) throw new ArgumentNullException("userData");
            if (scope != DataProtectionScope.CurrentUser) throw new CryptographicException("Only per-user protection is available on Linux.");
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize), tag = new byte[TagSize], cipher = new byte[userData.Length];
            using (var aes = new AesGcm(Key(), TagSize)) aes.Encrypt(nonce, userData, cipher, tag, optionalEntropy ?? new byte[0]);
            var output = new byte[1 + NonceSize + TagSize + cipher.Length];
            output[0] = Version;
            Buffer.BlockCopy(nonce, 0, output, 1, NonceSize);
            Buffer.BlockCopy(tag, 0, output, 1 + NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, output, 1 + NonceSize + TagSize, cipher.Length);
            return output;
        }
        public static byte[] Unprotect(byte[] encryptedData, byte[] optionalEntropy, DataProtectionScope scope)
        {
            if (encryptedData == null) throw new ArgumentNullException("encryptedData");
            if (scope != DataProtectionScope.CurrentUser) throw new CryptographicException("Only per-user protection is available on Linux.");
            if (encryptedData.Length < 1 + NonceSize + TagSize || encryptedData[0] != Version) throw new CryptographicException("The protected data is not in a supported format.");
            byte[] nonce = new byte[NonceSize], tag = new byte[TagSize], cipher = new byte[encryptedData.Length - 1 - NonceSize - TagSize], clear = new byte[cipher.Length];
            Buffer.BlockCopy(encryptedData, 1, nonce, 0, NonceSize);
            Buffer.BlockCopy(encryptedData, 1 + NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(encryptedData, 1 + NonceSize + TagSize, cipher, 0, cipher.Length);
            try { using (var aes = new AesGcm(Key(), TagSize)) aes.Decrypt(nonce, cipher, tag, clear, optionalEntropy ?? new byte[0]); }
            catch (AuthenticationTagMismatchException e) { throw new CryptographicException("The protected data was changed or belongs to another user.", e); }
            return clear;
        }
    }

    // The per-user data key, stored as a Secret Service password through libsecret.
    internal static class KeyringKey
    {
        private const string Lib = "libsecret-1.so.0", GLib = "libglib-2.0.so.0";
        private const string SchemaName = "io.github.los33.EeroDesktop.DataKey";
        [StructLayout(LayoutKind.Sequential)] private struct GError { public uint Domain; public int Code; public IntPtr Message; }
        [DllImport(GLib)] private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);
        [DllImport(GLib)] private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);
        [DllImport(GLib)] private static extern void g_hash_table_unref(IntPtr table);
        [DllImport(GLib)] private static extern void g_error_free(IntPtr error);
        [DllImport(Lib)] private static extern IntPtr secret_schema_newv(IntPtr name, int flags, IntPtr attributeNamesAndTypes);
        [DllImport(Lib)] private static extern void secret_schema_unref(IntPtr schema);
        [DllImport(Lib)] private static extern int secret_password_storev_sync(IntPtr schema, IntPtr attributes, IntPtr collection, IntPtr label, IntPtr password, IntPtr cancellable, out IntPtr error);
        [DllImport(Lib)] private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
        [DllImport(Lib)] private static extern void secret_password_free(IntPtr password);

        public static byte[] LoadOrCreate()
        {
            string existing = Lookup();
            if (existing != null)
            {
                try { byte[] key = Convert.FromBase64String(existing); if (key.Length == 32) return key; }
                catch (FormatException) { }
            }
            byte[] created = RandomNumberGenerator.GetBytes(32);
            Store(Convert.ToBase64String(created));
            return created;
        }
        private static T WithSchema<T>(Func<IntPtr, IntPtr, T> body)
        {
            IntPtr glib = NativeLibrary.Load(GLib);
            IntPtr strHash = NativeLibrary.GetExport(glib, "g_str_hash"), strEqual = NativeLibrary.GetExport(glib, "g_str_equal");
            IntPtr name = Utf8("purpose"), value = Utf8("data-key"), schemaName = Utf8(SchemaName);
            IntPtr types = g_hash_table_new(strHash, strEqual), attributes = g_hash_table_new(strHash, strEqual), schema = IntPtr.Zero;
            try
            {
                g_hash_table_insert(types, name, IntPtr.Zero); // SECRET_SCHEMA_ATTRIBUTE_STRING
                schema = secret_schema_newv(schemaName, 0, types);
                g_hash_table_insert(attributes, name, value);
                return body(schema, attributes);
            }
            finally
            {
                if (schema != IntPtr.Zero) secret_schema_unref(schema);
                g_hash_table_unref(types); g_hash_table_unref(attributes);
                Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(value); Marshal.FreeHGlobal(schemaName);
            }
        }
        private static string Lookup()
        {
            return WithSchema(delegate(IntPtr schema, IntPtr attributes)
            {
                IntPtr error;
                IntPtr found = secret_password_lookupv_sync(schema, attributes, IntPtr.Zero, out error);
                Throw(error);
                if (found == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUTF8(found); }
                finally { secret_password_free(found); }
            });
        }
        private static void Store(string secret)
        {
            WithSchema(delegate(IntPtr schema, IntPtr attributes)
            {
                IntPtr label = Utf8("eero Desktop data protection key"), collection = Utf8("default"), password = Utf8(secret);
                try
                {
                    IntPtr error;
                    int ok = secret_password_storev_sync(schema, attributes, collection, label, password, IntPtr.Zero, out error);
                    Throw(error);
                    if (ok == 0) throw new CryptographicException("The desktop keyring refused to store the data key.");
                    return 0;
                }
                finally { Marshal.FreeHGlobal(label); Marshal.FreeHGlobal(collection); Marshal.FreeHGlobal(password); }
            });
        }
        private static void Throw(IntPtr error)
        {
            if (error == IntPtr.Zero) return;
            string message = Marshal.PtrToStringUTF8(Marshal.PtrToStructure<GError>(error).Message) ?? "unknown error";
            g_error_free(error);
            throw new CryptographicException("The desktop keyring is not available (" + message + "). Install and unlock GNOME Keyring, KWallet or another Secret Service provider, or sign in without Remember me.");
        }
        private static IntPtr Utf8(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            IntPtr p = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, p, bytes.Length); Marshal.WriteByte(p, bytes.Length, 0);
            return p;
        }
    }
}
