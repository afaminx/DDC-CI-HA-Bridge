using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SolarMonitorBrightness;

internal static class TokenProtector
{
    private const string Prefix = "dpapi:";

    public static string Protect(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return "";
        }

        var bytes = Encoding.UTF8.GetBytes(token);
        var input = CreateBlob(bytes);

        try
        {
            if (!CryptProtectData(ref input, "DDC/CI HA-Bridge Home Assistant Token", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The token could not be protected.");
            }

            try
            {
                var protectedBytes = ReadBlob(output);
                return Prefix + Convert.ToBase64String(protectedBytes);
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
        }
    }

    public static string Unprotect(string protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken))
        {
            return "";
        }

        if (!protectedToken.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return protectedToken;
        }

        var bytes = Convert.FromBase64String(protectedToken[Prefix.Length..]);
        var input = CreateBlob(bytes);

        try
        {
            if (!CryptUnprotectData(ref input, out var description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The token could not be read.");
            }

            try
            {
                if (description != IntPtr.Zero)
                {
                    LocalFree(description);
                }

                return Encoding.UTF8.GetString(ReadBlob(output));
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var blob = new DataBlob
        {
            Size = bytes.Length,
            Data = Marshal.AllocHGlobal(bytes.Length)
        };

        Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
        return blob;
    }

    private static byte[] ReadBlob(DataBlob blob)
    {
        var bytes = new byte[blob.Size];
        Marshal.Copy(blob.Data, bytes, 0, blob.Size);
        return bytes;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }
}
