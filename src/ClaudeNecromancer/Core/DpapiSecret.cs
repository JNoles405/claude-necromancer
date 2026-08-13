using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeNecromancer.Core;

/// <summary>
/// Encrypts small secrets with Windows DPAPI, scoped to the current user account.
///
/// Used for the claude.ai session cookie, which is a live credential: anyone holding it can read
/// the account's conversations. DPAPI means the config file on disk is useless to another user
/// account or to anyone who copies it off the machine.
///
/// P/Invoked directly rather than via the System.Security.Cryptography.ProtectedData package, to
/// keep the app buildable with no NuGet restore at all.
/// </summary>
public static class DpapiSecret
{
    private const uint CryptprotectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, uint dwFlags, out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, uint dwFlags, out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        return Convert.ToBase64String(Transform(bytes, protect: true));
    }

    public static string? Unprotect(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        try
        {
            var bytes = Transform(Convert.FromBase64String(base64), protect: false);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            Log.Warn($"Stored credential could not be decrypted: {ex.Message}");
            return null;
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inBlob = new DataBlob();
        var outBlob = new DataBlob();

        try
        {
            inBlob.pbData = Marshal.AllocHGlobal(input.Length);
            inBlob.cbData = input.Length;
            Marshal.Copy(input, 0, inBlob.pbData, input.Length);

            var ok = protect
                ? CryptProtectData(ref inBlob, "Claude Necromancer", IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, CryptprotectUiForbidden, out outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, CryptprotectUiForbidden, out outBlob);

            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }
}
