using System.Globalization;
using System.Security.Cryptography.X509Certificates;

namespace PlatinVault.Desktop.Updates;

/// <summary>What a signature check of an executable found.</summary>
internal enum SignatureState
{
    /// <summary>An Authenticode signature that Windows trusts (chain, timestamp, revocation, file hash).</summary>
    Valid,

    /// <summary>The file carries no (embedded) signature at all.</summary>
    Unsigned,

    /// <summary>There is a signature, but Windows does not accept it (altered file, untrusted or revoked certificate, no revocation answer ...).</summary>
    Invalid,
}

/// <summary>
/// Result of <see cref="ISignatureVerifier.Verify"/>. <see cref="ErrorCode"/> is the HRESULT from WinVerifyTrust for <see cref="SignatureState.Invalid"/>;
/// <see cref="Signer"/> is the leaf certificate's identity and only set for <see cref="SignatureState.Valid"/>.
/// </summary>
internal sealed record SignatureInfo(SignatureState State, int? ErrorCode = null, PublisherIdentity? Signer = null)
{
    public static SignatureInfo Unsigned() => new(SignatureState.Unsigned);

    public static SignatureInfo Valid(PublisherIdentity? signer) => new(SignatureState.Valid, null, signer);

    public static SignatureInfo Invalid(int errorCode) => new(SignatureState.Invalid, errorCode);

    /// <summary>"0x800B010C" - how an error code is named in messages and the log.</summary>
    public string CodeText => ErrorCode is { } code ? "0x" + ((uint)code).ToString("X8", CultureInfo.InvariantCulture) : "-";
}

/// <summary>The thin Windows part: reads and verifies the Authenticode signature of a file.</summary>
internal interface ISignatureVerifier
{
    /// <summary>
    /// Verifies the embedded signature. <paramref name="checkRevocation"/>: ask the certificate's CRL/OCSP servers for the whole chain (needs the
    /// network); without it the chain, the trusted root, the timestamp and the file hash are still checked.
    /// </summary>
    SignatureInfo Verify(string path, bool checkRevocation);

    /// <summary>The "Product name" of the file's version resource, null when there is none.</summary>
    string? ProductName(string path);
}

/// <summary>
/// Who signed: the publisher of a certificate, NOT its thumbprint. Certificates are renewed (new key, new serial, often a new issuing CA) while
/// the subject stays the same, so the identity compared between the running program and the downloaded installer is the subject's
/// organisation, common name and country. <see cref="Subject"/> keeps the whole subject for messages and the log.
/// </summary>
internal sealed record PublisherIdentity(string? Organization, string? CommonName, string? Country, string Subject)
{
    private const string OrganizationOid = "2.5.4.10";
    private const string CommonNameOid = "2.5.4.3";
    private const string CountryOid = "2.5.4.6";

    public static PublisherIdentity From(X500DistinguishedName subject)
    {
        string? organization = null, commonName = null, country = null;
        foreach (var rdn in subject.EnumerateRelativeDistinguishedNames())
        {
            if (rdn.HasMultipleElements) continue;
            var value = rdn.GetSingleElementValue();
            switch (rdn.GetSingleElementType().Value)
            {
                case OrganizationOid: organization ??= value; break;
                case CommonNameOid: commonName ??= value; break;
                case CountryOid: country ??= value; break;
            }
        }
        return new PublisherIdentity(Clean(organization), Clean(commonName), Clean(country), subject.Name);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Same publisher: organisation and common name are equal (ignoring case) and the country is equal when both name one. A subject
    /// with neither organisation nor common name has no identity and matches nothing.
    /// </summary>
    public bool IsSamePublisherAs(PublisherIdentity other)
    {
        if (Organization is null && CommonName is null) return false;
        if (other.Organization is null && other.CommonName is null) return false;
        return Same(Organization, other.Organization) && Same(CommonName, other.CommonName)
               && (Country is null || other.Country is null || Same(Country, other.Country));
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
