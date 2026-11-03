namespace PlatinVault.Desktop.Updates;

/// <summary>The verdict on a downloaded installer.</summary>
internal enum UpdateSignatureVerdict
{
    /// <summary>The running program is signed, the installer carries a valid signature of the same publisher and is a PlatinVault installer.</summary>
    Accepted,

    /// <summary>The running program carries no signature at all (builds before code signing, development run): only the SHA-256 check applies.</summary>
    AcceptedProgramUnsigned,

    /// <summary>The running program HAS a signature that Windows does not accept: nothing can be trusted, the update is not installed.</summary>
    ProgramSignatureInvalid,

    /// <summary>The program is signed, the installer is not.</summary>
    InstallerUnsigned,

    /// <summary>The installer has a signature Windows does not accept.</summary>
    InstallerInvalid,

    /// <summary>The installer is validly signed, but by someone else than the running program.</summary>
    PublisherChanged,

    /// <summary>
    /// The installer is validly signed by the same publisher, but is not a PlatinVault installer (its product name differs): a publisher
    /// such as SignPath Foundation signs many projects with one certificate, so the publisher alone proves nothing.
    /// </summary>
    WrongProduct,
}

/// <summary>
/// <paramref name="Detail"/> is the technical reason in English (certificate subjects, error code) for messages and the log;
/// <see cref="MayRun"/> is the only thing the caller must look at to decide whether the installer starts.
/// </summary>
internal sealed record UpdateSignatureDecision(UpdateSignatureVerdict Verdict, string Detail)
{
    public bool MayRun => Verdict is UpdateSignatureVerdict.Accepted or UpdateSignatureVerdict.AcceptedProgramUnsigned;
}

/// <summary>
/// The rule for running a downloaded installer (pure, unit-tested):
/// <list type="bullet">
/// <item>The running program has NO signature (builds before code signing, development runs): the old behaviour, the SHA-256 check against the
/// published file is all there is, and the log says so.</item>
/// <item>The running program has a signature Windows does not accept: the update is refused. Treating that like "unsigned" would let a failed
/// check switch the protection off.</item>
/// <item>The running program has a valid signature: the installer must have a valid signature of the same publisher AND the product name
/// <see cref="ExpectedProductName"/> (the version resource is covered by the signature, and the signing service enforces the product name
/// per project - the publisher alone is shared by every project that service signs).</item>
/// </list>
/// </summary>
internal static class UpdateSignaturePolicy
{
    /// <summary>The product name every PlatinVault binary carries (Directory.Build.props, checked by the signing service).</summary>
    public const string ExpectedProductName = "PlatinVault";

    public static UpdateSignatureDecision Decide(SignatureInfo running, SignatureInfo installer, string? installerProductName)
    {
        if (running.State == SignatureState.Unsigned)
        {
            return new UpdateSignatureDecision(UpdateSignatureVerdict.AcceptedProgramUnsigned,
                "the running program is not signed; only the SHA-256 check applies");
        }
        if (running.State == SignatureState.Invalid)
        {
            return new UpdateSignatureDecision(UpdateSignatureVerdict.ProgramSignatureInvalid,
                $"the signature of the running program is not valid (error {running.CodeText})");
        }

        switch (installer.State)
        {
            case SignatureState.Unsigned:
                return new UpdateSignatureDecision(UpdateSignatureVerdict.InstallerUnsigned,
                    $"the installer is not signed, but the running program is (signer: {Describe(running.Signer)})");
            case SignatureState.Invalid:
                return new UpdateSignatureDecision(UpdateSignatureVerdict.InstallerInvalid,
                    $"Windows does not accept the installer's signature (error {installer.CodeText})");
        }

        if (running.Signer is null || installer.Signer is null || !running.Signer.IsSamePublisherAs(installer.Signer))
        {
            return new UpdateSignatureDecision(UpdateSignatureVerdict.PublisherChanged,
                $"the installer is signed by {Describe(installer.Signer)}, the running program by {Describe(running.Signer)}");
        }
        if (!string.Equals(installerProductName?.Trim(), ExpectedProductName, StringComparison.Ordinal))
        {
            return new UpdateSignatureDecision(UpdateSignatureVerdict.WrongProduct,
                $"the installer is signed by {Describe(installer.Signer)}, but its product name is \"{installerProductName ?? "(none)"}\" instead of \"{ExpectedProductName}\"");
        }
        return new UpdateSignatureDecision(UpdateSignatureVerdict.Accepted, $"signed by {Describe(installer.Signer)}, product {ExpectedProductName}");
    }

    /// <summary>
    /// Checks both files with <paramref name="verifier"/> and decides. The running program is already installed, so it is verified
    /// WITHOUT the online revocation check (chain, trusted root, timestamp and hash only): an unreachable CRL/OCSP server must never turn a
    /// signed program into "invalid" - and, worse, switch the installer check off. The downloaded installer gets the full check.
    /// </summary>
    public static UpdateSignatureDecision Check(ISignatureVerifier verifier, string runningExe, string installer)
    {
        var running = verifier.Verify(runningExe, checkRevocation: false);
        // Nothing to compare with when the program is unsigned or broken: the installer is not even looked at (no network wait)
        if (running.State != SignatureState.Valid) return Decide(running, SignatureInfo.Unsigned(), null);
        var downloaded = verifier.Verify(installer, checkRevocation: true);
        return Decide(running, downloaded, downloaded.State == SignatureState.Valid ? verifier.ProductName(installer) : null);
    }

    private static string Describe(PublisherIdentity? signer) => signer is null ? "an unknown publisher" : $"\"{signer.Subject}\"";
}
