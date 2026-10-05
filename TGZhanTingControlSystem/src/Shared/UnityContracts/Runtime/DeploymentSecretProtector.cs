using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TG.Control.Contracts
{
    public static class DeploymentSecretProtector
    {
        public const string Prefix = "dpapi-local-machine:";

        public static string Unprotect(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("DPAPI deployment secrets require Windows.");
            byte[] encrypted;
            try { encrypted = Convert.FromBase64String(value.Substring(Prefix.Length)); }
            catch (FormatException exception) { throw new InvalidOperationException("The protected terminal key is invalid.", exception); }

            var input = default(DataBlob);
            var output = default(DataBlob);
            try
            {
                input.Size = encrypted.Length;
                input.Data = Marshal.AllocHGlobal(encrypted.Length);
                Marshal.Copy(encrypted, 0, input.Data, encrypted.Length);
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not decrypt the terminal key.");
                var clear = new byte[output.Size];
                Marshal.Copy(output.Data, clear, 0, output.Size);
                return Encoding.UTF8.GetString(clear);
            }
            finally
            {
                if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob { public int Size; public IntPtr Data; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
