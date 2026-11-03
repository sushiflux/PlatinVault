using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace PlatinVault.Desktop.Updates;

/// <summary>
/// Reads and verifies the embedded Authenticode signature of an executable with Windows' own trust provider (WinVerifyTrust,
/// WINTRUST_ACTION_GENERIC_VERIFY_V2): file hash, certificate chain up to a trusted root, code-signing purpose, timestamp (a signature stays
/// valid after its certificate expired when it was timestamped) and revocation of the whole chain, exactly as Windows decides it.
/// The leaf certificate is taken from the same verification (WTHelper* functions), so the identity returned belongs to the signature that
/// was checked. The .NET alternative <c>X509Certificate.CreateFromSignedFile</c> is obsolete (SYSLIB0057) and does not verify anything.
/// All the decisions about WHAT to do with the result live in <see cref="UpdateSignaturePolicy"/>; this class only talks to Windows.
/// </summary>
internal sealed class WinTrustVerifier : ISignatureVerifier
{
    public SignatureInfo Verify(string path, bool checkRevocation) => VerifyFile(path, checkRevocation);

    /// <summary>The "Product name" of the file's version resource (covered by the Authenticode hash); null when there is none.</summary>
    public string? ProductName(string path)
    {
        try
        {
            return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustEProviderUnknown = unchecked((int)0x800B0001);

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;

    // dwProvFlags, values from the Windows SDK (um/WinTrust.h). Do NOT mix them up: 0x200 is WTD_HASH_ONLY_FLAG (only the file hash is
    // checked, no certificate chain, no trusted root) - a self-signed file then verifies as valid.
    internal const uint WtdRevocationCheckNone = 0x00000010;
    internal const uint WtdRevocationCheckChainExcludeRoot = 0x00000080;
    internal const uint WtdHashOnlyFlag = 0x00000200;
    internal const uint WtdDisableMd2Md4 = 0x00002000;

    /// <summary>The provider flags of a verification: MD2/MD4 signatures are never accepted; revocation of the chain (without the root) only when asked.</summary>
    internal static uint ProviderFlags(bool checkRevocation) =>
        WtdDisableMd2Md4 | (checkRevocation ? WtdRevocationCheckChainExcludeRoot : WtdRevocationCheckNone);

    /// <summary>fdwRevocationChecks of a verification.</summary>
    internal static uint RevocationChecks(bool checkRevocation) => checkRevocation ? WtdRevokeWholeChain : WtdRevokeNone;

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static SignatureInfo VerifyFile(string path, bool checkRevocation)
    {
        var fileInfo = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = Marshal.StringToCoTaskMemUni(path) };
        var filePointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(fileInfo, filePointer, false);
        var data = new WinTrustData
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            dwUIChoice = WtdUiNone,
            fdwRevocationChecks = RevocationChecks(checkRevocation),
            dwUnionChoice = WtdChoiceFile,
            pFile = filePointer,
            dwStateAction = WtdStateActionVerify,
            dwProvFlags = ProviderFlags(checkRevocation),
        };
        var action = GenericVerifyV2;
        var noWindow = new IntPtr(-1);
        try
        {
            var result = WinVerifyTrust(noWindow, ref action, ref data);
            try
            {
                if (result == 0) return SignatureInfo.Valid(ReadSigner(data.hWVTStateData));
                if (result is TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown) return SignatureInfo.Unsigned();
                return SignatureInfo.Invalid(result);
            }
            finally
            {
                // Always release the state WinVerifyTrust keeps for the helper functions
                data.dwStateAction = WtdStateActionClose;
                WinVerifyTrust(noWindow, ref action, ref data);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(fileInfo.pcwszFilePath);
            Marshal.FreeCoTaskMem(filePointer);
        }
    }

    /// <summary>The leaf certificate of the first signature of the verification held in <paramref name="state"/>; null when it cannot be read.</summary>
    private static PublisherIdentity? ReadSigner(IntPtr state)
    {
        if (state == IntPtr.Zero) return null;
        var provider = WTHelperProvDataFromStateData(state);
        if (provider == IntPtr.Zero) return null;
        var signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
        if (signer == IntPtr.Zero) return null;
        var providerCert = WTHelperGetProvCertFromChain(signer, 0);
        if (providerCert == IntPtr.Zero) return null;

        // CRYPT_PROVIDER_CERT { DWORD cbStruct; PCCERT_CONTEXT pCert; ... } and CERT_CONTEXT { DWORD type; BYTE* pbCertEncoded; DWORD cbCertEncoded; ... }:
        // the pointer fields sit at the next pointer-aligned offset
        var certContext = Marshal.ReadIntPtr(providerCert, IntPtr.Size);
        if (certContext == IntPtr.Zero) return null;
        var encoded = Marshal.ReadIntPtr(certContext, IntPtr.Size);
        var length = Marshal.ReadInt32(certContext, IntPtr.Size * 2);
        if (encoded == IntPtr.Zero || length <= 0) return null;
        var bytes = new byte[length];
        Marshal.Copy(encoded, bytes, 0, length);
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(bytes);
            return PublisherIdentity.From(certificate.SubjectName);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provData, uint signerIndex, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certIndex);
}
