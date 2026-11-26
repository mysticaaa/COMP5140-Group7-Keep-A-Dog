using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Wjy.DogTraining
{
    public static class DogModelCredentials
    {
        public static bool TryLoad(string endpoint, out string key, string storageRoot = null)
        {
            key = "";
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            byte[] clear = null;
            try
            {
                if (!TryProvider(endpoint, out byte[] provider)) return false;
                string path = CredentialPath(provider, storageRoot);
                if (!File.Exists(path) || new FileInfo(path).Length > 65536) return false;
                clear = Protect(File.ReadAllBytes(path), provider, false);
                string loaded = new UTF8Encoding(false, true).GetString(clear);
                if (string.IsNullOrWhiteSpace(loaded) || loaded.Length > 512) return false;
                key = loaded;
                return true;
            }
            catch (Exception error) when (StorageFailure(error)) { return false; }
            finally { if (clear != null) Array.Clear(clear, 0, clear.Length); }
#else
            return false;
#endif
        }

        public static bool TrySave(string endpoint, string key, out string error, string storageRoot = null)
        {
            error = "";
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            byte[] clear = null;
            string temporary = null;
            try
            {
                if (!TryProvider(endpoint, out byte[] provider))
                { error = "接口地址无效，未保存密钥。"; return false; }
                key = key?.Trim();
                if (string.IsNullOrWhiteSpace(key) || key.Length > 512)
                { error = "请先填写有效的 API Key。"; return false; }
                clear = Encoding.UTF8.GetBytes(key);
                byte[] encrypted = Protect(clear, provider, true);
                string path = CredentialPath(provider, storageRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporary, encrypted);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception failure) when (StorageFailure(failure))
            { error = "未能在本机加密保存密钥，本次运行仍可使用。"; return false; }
            finally
            {
                if (clear != null) Array.Clear(clear, 0, clear.Length);
                if (temporary != null)
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (Exception failure) when (StorageFailure(failure)) { }
            }
#else
            error = "此平台暂不支持本机加密保存，请使用本次运行的密钥。";
            return false;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private static bool TryProvider(string endpoint, out byte[] provider)
        {
            provider = null;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            string authority = uri.Scheme.ToLowerInvariant() + "://" + uri.IdnHost.ToLowerInvariant() + ":" + uri.Port;
            using (var hash = SHA256.Create()) provider = hash.ComputeHash(Encoding.UTF8.GetBytes(authority));
            return true;
        }

        private static string CredentialPath(byte[] provider, string storageRoot) =>
            Path.Combine(storageRoot ?? Path.Combine(Application.persistentDataPath, "ModelCredentials"),
                BitConverter.ToString(provider).Replace("-", "").ToLowerInvariant() + ".bin");

        private static bool StorageFailure(Exception error) => error is IOException || error is UnauthorizedAccessException ||
            error is SecurityException || error is CryptographicException || error is ArgumentException ||
            error is NotSupportedException || error is DllNotFoundException || error is EntryPointNotFoundException;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob { public int Length; public IntPtr Data; }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob input, string description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static byte[] Protect(byte[] value, byte[] provider, bool encrypt)
        {
            var input = new DataBlob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
            var entropy = new DataBlob { Length = provider.Length };
            DataBlob output = default;
            try
            {
                entropy.Data = Marshal.AllocHGlobal(provider.Length);
                Marshal.Copy(value, 0, input.Data, value.Length);
                Marshal.Copy(provider, 0, entropy.Data, provider.Length);
                // UI_FORBIDDEN only: omitting LOCAL_MACHINE binds the blob to the current Windows user.
                bool success = encrypt
                    ? CryptProtectData(ref input, "CityDog model credential", ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!success || output.Length <= 0 || output.Length > 65536) throw new CryptographicException();
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                for (int index = 0; index < input.Length; index++) Marshal.WriteByte(input.Data, index, 0);
                Marshal.FreeHGlobal(input.Data);
                if (entropy.Data != IntPtr.Zero) Marshal.FreeHGlobal(entropy.Data);
                if (output.Data != IntPtr.Zero)
                {
                    if (output.Length <= 65536)
                        for (int index = 0; index < output.Length; index++) Marshal.WriteByte(output.Data, index, 0);
                    LocalFree(output.Data);
                }
            }
        }
#endif
    }
}
