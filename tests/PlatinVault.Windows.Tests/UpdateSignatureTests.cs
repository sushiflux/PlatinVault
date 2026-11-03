using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Companion.Api.Desktop;
using PlatinVault.Desktop.Server;
using PlatinVault.Desktop.Updates;

namespace PlatinVault.Windows.Tests;

/// <summary>The rule for running a downloaded installer: signed program -> installer signed by the same publisher with the PlatinVault product name.</summary>
public sealed class UpdateSignaturePolicyTests
{
    private static PublisherIdentity Publisher(string subject) => PublisherIdentity.From(new X500DistinguishedName(subject));

    private static readonly PublisherIdentity Foundation = Publisher("CN=SignPath Foundation, O=SignPath Foundation, L=Lewes, S=Delaware, C=US");

    private const string Product = UpdateSignaturePolicy.ExpectedProductName;

    [Fact]
    public void Unsigned_running_program_keeps_the_checksum_only_behaviour()
    {
        var decision = UpdateSignaturePolicy.Decide(SignatureInfo.Unsigned(), SignatureInfo.Unsigned(), null);
        Assert.True(decision.MayRun);
        Assert.Equal(UpdateSignatureVerdict.AcceptedProgramUnsigned, decision.Verdict);
        // even a signed installer does not need a counterpart
        Assert.True(UpdateSignaturePolicy.Decide(SignatureInfo.Unsigned(), SignatureInfo.Valid(Foundation), "Anything").MayRun);
    }

    [Fact]
    public void A_running_program_whose_signature_is_broken_refuses_the_update_it_is_not_treated_as_unsigned()
    {
        var decision = UpdateSignaturePolicy.Decide(SignatureInfo.Invalid(unchecked((int)0x800B0109)), SignatureInfo.Valid(Foundation), Product);
        Assert.False(decision.MayRun);
        Assert.Equal(UpdateSignatureVerdict.ProgramSignatureInvalid, decision.Verdict);
        Assert.Contains("0x800B0109", decision.Detail);
    }

    [Fact]
    public void Signed_program_accepts_a_valid_installer_of_the_same_publisher_and_product()
    {
        var decision = UpdateSignaturePolicy.Decide(SignatureInfo.Valid(Foundation), SignatureInfo.Valid(Foundation), Product);
        Assert.True(decision.MayRun);
        Assert.Equal(UpdateSignatureVerdict.Accepted, decision.Verdict);
    }

    [Fact]
    public void Renewed_certificate_with_the_same_subject_is_the_same_publisher_whatever_its_issuer_or_address_details()
    {
        // The subject DN is what stays; locality/state may be written differently after a renewal, thumbprint and issuer are not looked at at all
        var renewed = Publisher("CN=signpath foundation, O=SignPath Foundation, L=Wilmington, S=Delaware, C=US");
        Assert.True(UpdateSignaturePolicy.Decide(SignatureInfo.Valid(Foundation), SignatureInfo.Valid(renewed), Product).MayRun);
    }

    [Theory]
    [InlineData("CN=Someone Else, O=Someone Else, C=US")]
    [InlineData("CN=SignPath Foundation, O=Other Organisation, C=US")]
    [InlineData("CN=SignPath Foundation, O=SignPath Foundation, C=DE")]
    public void Installer_from_another_publisher_is_refused(string otherSubject)
    {
        var decision = UpdateSignaturePolicy.Decide(SignatureInfo.Valid(Foundation), SignatureInfo.Valid(Publisher(otherSubject)), Product);
        Assert.False(decision.MayRun);
        Assert.Equal(UpdateSignatureVerdict.PublisherChanged, decision.Verdict);
        Assert.Contains("SignPath Foundation", decision.Detail);
    }

    [Theory]
    [InlineData("SomeOtherProject")]
    [InlineData("platinvault")]
    [InlineData("")]
    [InlineData(null)]
    public void Same_publisher_but_another_product_is_refused(string? productName)
    {
        // SignPath Foundation signs many open-source projects with one certificate: the publisher alone proves nothing
        var decision = UpdateSignaturePolicy.Decide(SignatureInfo.Valid(Foundation), SignatureInfo.Valid(Foundation), productName);
        Assert.False(decision.MayRun);
        Assert.Equal(UpdateSignatureVerdict.WrongProduct, decision.Verdict);
        Assert.Contains("PlatinVault", decision.Detail);
    }

    [Fact]
    public void Unsigned_or_invalid_installer_is_refused_when_the_program_is_signed()
    {
        var unsigned = UpdateSignaturePolicy.Decide(SignatureInfo.Valid(Foundation), SignatureInfo.Unsigned(), null);
        Assert.False(unsigned.MayRun);
        Assert.Equal(UpdateSignatureVerdict.InstallerUnsigned, unsigned.Verdict);

        var invalid = UpdateSignaturePolicy.Decide(SignatureInfo.Valid(Foundation), SignatureInfo.Invalid(unchecked((int)0x80096010)), null);
        Assert.False(invalid.MayRun);
        Assert.Equal(UpdateSignatureVerdict.InstallerInvalid, invalid.Verdict);
        Assert.Contains("0x80096010", invalid.Detail);
    }

    [Fact]
    public void A_signature_whose_signer_could_not_be_read_never_counts_as_the_same_publisher()
    {
        Assert.False(UpdateSignaturePolicy.Decide(SignatureInfo.Valid(Foundation), SignatureInfo.Valid(null), Product).MayRun);
        Assert.False(UpdateSignaturePolicy.Decide(SignatureInfo.Valid(null), SignatureInfo.Valid(Foundation), Product).MayRun);
    }

    [Fact]
    public void A_subject_without_organisation_and_common_name_matches_nothing()
    {
        var anonymous = Publisher("C=US");
        Assert.False(anonymous.IsSamePublisherAs(anonymous));
    }

    [Fact]
    public void Identity_is_read_from_the_subject()
    {
        Assert.Equal("SignPath Foundation", Foundation.Organization);
        Assert.Equal("SignPath Foundation", Foundation.CommonName);
        Assert.Equal("US", Foundation.Country);
    }

    [Fact]
    public void Check_verifies_the_running_program_without_revocation_and_the_installer_with_it()
    {
        var verifier = new FakeVerifier(new Dictionary<string, SignatureInfo>
        {
            [@"C:\app\PlatinVault.exe"] = SignatureInfo.Valid(Foundation),
            [@"C:\updates\Setup.exe"] = SignatureInfo.Valid(Foundation),
        }, products: new Dictionary<string, string?> { [@"C:\updates\Setup.exe"] = Product });
        var decision = UpdateSignaturePolicy.Check(verifier, @"C:\app\PlatinVault.exe", @"C:\updates\Setup.exe");
        Assert.True(decision.MayRun);
        // An unreachable CRL/OCSP server must not turn the installed program "invalid" (that would switch the installer check off)
        Assert.Equal([(@"C:\app\PlatinVault.exe", false), (@"C:\updates\Setup.exe", true)], verifier.Asked);
    }

    [Fact]
    public void Check_with_an_invalid_running_program_refuses_without_even_looking_at_the_installer()
    {
        var verifier = new FakeVerifier(new Dictionary<string, SignatureInfo>
        {
            [@"C:\app\PlatinVault.exe"] = SignatureInfo.Invalid(unchecked((int)0x800B0109)),
            [@"C:\updates\Setup.exe"] = SignatureInfo.Valid(Foundation),
        });
        var decision = UpdateSignaturePolicy.Check(verifier, @"C:\app\PlatinVault.exe", @"C:\updates\Setup.exe");
        Assert.Equal(UpdateSignatureVerdict.ProgramSignatureInvalid, decision.Verdict);
        Assert.Equal([(@"C:\app\PlatinVault.exe", false)], verifier.Asked);
    }

    [Fact]
    public void Check_with_an_unsigned_running_program_accepts_on_the_checksum_alone()
    {
        var verifier = new FakeVerifier(new Dictionary<string, SignatureInfo> { [@"C:\app\PlatinVault.exe"] = SignatureInfo.Unsigned() });
        Assert.Equal(UpdateSignatureVerdict.AcceptedProgramUnsigned, UpdateSignaturePolicy.Check(verifier, @"C:\app\PlatinVault.exe", @"C:\updates\Setup.exe").Verdict);
    }

    [Fact]
    public void Check_refuses_an_unsigned_installer_and_a_foreign_product()
    {
        var running = SignatureInfo.Valid(Foundation);
        var unsigned = new FakeVerifier(new Dictionary<string, SignatureInfo> { [@"C:\a.exe"] = running, [@"C:\s.exe"] = SignatureInfo.Unsigned() });
        Assert.False(UpdateSignaturePolicy.Check(unsigned, @"C:\a.exe", @"C:\s.exe").MayRun);

        var foreign = new FakeVerifier(new Dictionary<string, SignatureInfo> { [@"C:\a.exe"] = running, [@"C:\s.exe"] = SignatureInfo.Valid(Foundation) },
            new Dictionary<string, string?> { [@"C:\s.exe"] = "SomeOtherProject" });
        Assert.Equal(UpdateSignatureVerdict.WrongProduct, UpdateSignaturePolicy.Check(foreign, @"C:\a.exe", @"C:\s.exe").Verdict);
    }

    private sealed class FakeVerifier(Dictionary<string, SignatureInfo> answers, Dictionary<string, string?>? products = null) : ISignatureVerifier
    {
        public List<(string Path, bool Revocation)> Asked { get; } = [];

        public SignatureInfo Verify(string path, bool checkRevocation)
        {
            Asked.Add((path, checkRevocation));
            return answers[path];
        }

        public string? ProductName(string path) => products is not null && products.TryGetValue(path, out var name) ? name : null;
    }
}

/// <summary>The WinVerifyTrust wrapper against real files: a Microsoft-signed runtime library, a tampered copy, a self-signed impostor and an unsigned file.</summary>
public sealed class WinTrustVerifierTests
{
    // System.Private.CoreLib.dll ships with an embedded Microsoft Authenticode signature (every .NET runtime from Microsoft)
    private static readonly string SignedLibrary = typeof(object).Assembly.Location;

    private static readonly WinTrustVerifier Verifier = new();

    [Fact]
    public void Provider_flags_use_the_documented_windows_sdk_values_and_never_the_hash_only_flag()
    {
        // um/WinTrust.h: WTD_REVOCATION_CHECK_NONE 0x10, WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT 0x80, WTD_HASH_ONLY_FLAG 0x200, WTD_DISABLE_MD2_MD4 0x2000
        Assert.Equal(0x10u, WinTrustVerifier.WtdRevocationCheckNone);
        Assert.Equal(0x80u, WinTrustVerifier.WtdRevocationCheckChainExcludeRoot);
        Assert.Equal(0x200u, WinTrustVerifier.WtdHashOnlyFlag);
        Assert.Equal(0x2000u, WinTrustVerifier.WtdDisableMd2Md4);

        Assert.Equal(0x2080u, WinTrustVerifier.ProviderFlags(checkRevocation: true));
        Assert.Equal(0x2010u, WinTrustVerifier.ProviderFlags(checkRevocation: false));
        Assert.Equal(1u, WinTrustVerifier.RevocationChecks(true));
        Assert.Equal(0u, WinTrustVerifier.RevocationChecks(false));
        foreach (var revocation in new[] { true, false })
        {
            Assert.Equal(0u, WinTrustVerifier.ProviderFlags(revocation) & WinTrustVerifier.WtdHashOnlyFlag);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Embedded_microsoft_signature_is_valid_and_names_the_publisher(bool revocation)
    {
        var info = Verifier.Verify(SignedLibrary, revocation);
        if (info.State == SignatureState.Unsigned) Assert.Skip("This runtime carries no embedded signature (self-built?).");
        // Online: valid. Offline with the revocation check (CI without network): Windows answers with a revocation error - never "unsigned"
        if (revocation && info.State == SignatureState.Invalid)
        {
            var revocationErrors = new[] { 0x80092012u, 0x80092013u, 0x800B010Eu, 0x800B0101u };
            Assert.Contains((uint)info.ErrorCode!.Value, revocationErrors);
            return;
        }
        Assert.Equal(SignatureState.Valid, info.State);
        Assert.NotNull(info.Signer);
        Assert.Contains("Microsoft", info.Signer!.Organization ?? info.Signer.CommonName);
        Assert.True(info.Signer.IsSamePublisherAs(info.Signer));
    }

    [Fact]
    public void A_changed_byte_makes_the_signature_invalid()
    {
        if (Verifier.Verify(SignedLibrary, false).State != SignatureState.Valid) Assert.Skip("No valid embedded signature on this runtime to tamper with.");
        using var temp = new TempFolder();
        var copy = temp.Sub("changed.dll");
        var bytes = File.ReadAllBytes(SignedLibrary);
        // Flip a byte inside the first section's data (after the headers, covered by the Authenticode hash)
        bytes[0x400] ^= 0xFF;
        File.WriteAllBytes(copy, bytes);
        var info = Verifier.Verify(copy, false);
        Assert.Equal(SignatureState.Invalid, info.State);
        Assert.NotNull(info.ErrorCode);
    }

    /// <summary>
    /// The regression test for the "hash only" flag (0x200 instead of 0x80): a copy of a small program signed by a self-made certificate
    /// with the very subject of the real publisher. Its hash is fine, but the certificate chains to no trusted root - Windows must say
    /// "invalid" (0x800B0109), in BOTH modes. The certificate lives only in memory of a short PowerShell process (EphemeralKeySet, no
    /// certificate store is touched).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_file_signed_by_a_self_made_certificate_with_the_publishers_name_is_invalid_not_valid(bool revocation)
    {
        var path = SelfSigned.Path;
        if (path is null) Assert.Skip(SelfSigned.WhyNot ?? "could not sign a test file");
        var signedPath = path!;
        var info = Verifier.Verify(signedPath, revocation);
        Assert.NotEqual(SignatureState.Valid, info.State);
        Assert.Equal(SignatureState.Invalid, info.State);
        Assert.Equal(unchecked((int)0x800B0109), info.ErrorCode); // CERT_E_UNTRUSTEDROOT
        Assert.Null(info.Signer);
    }

    [Fact]
    public void The_policy_refuses_the_self_signed_impostor_even_with_the_real_publishers_subject()
    {
        var path = SelfSigned.Path;
        if (path is null) Assert.Skip(SelfSigned.WhyNot ?? "could not sign a test file");
        var signedPath = path!;
        var legit = SignatureInfo.Valid(PublisherIdentity.From(new X500DistinguishedName("CN=SignPath Foundation, O=SignPath Foundation, C=AT")));
        var decision = UpdateSignaturePolicy.Decide(legit, Verifier.Verify(signedPath, true), "PlatinVault");
        Assert.False(decision.MayRun);
        Assert.Equal(UpdateSignatureVerdict.InstallerInvalid, decision.Verdict);
    }

    [Fact]
    public void A_file_without_signature_is_unsigned()
    {
        using var temp = new TempFolder();
        var path = temp.Sub("plain.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x00]);
        Assert.Equal(SignatureState.Unsigned, Verifier.Verify(path, false).State);
        File.WriteAllText(temp.Sub("notes.txt"), "text");
        Assert.Equal(SignatureState.Unsigned, Verifier.Verify(temp.Sub("notes.txt"), true).State);
    }

    [Fact]
    public void The_product_name_is_read_from_the_version_resource()
    {
        // dotnet's own host carries "Microsoft® .NET" as product name; a file without a resource has none
        using var temp = new TempFolder();
        var plain = temp.Sub("plain.exe");
        File.WriteAllBytes(plain, [0x4D, 0x5A, 0x00, 0x00]);
        Assert.True(string.IsNullOrEmpty(Verifier.ProductName(plain)));
        Assert.True(string.IsNullOrEmpty(Verifier.ProductName(temp.Sub("missing.exe"))));
        Assert.Contains("PlatinVault", Verifier.ProductName(typeof(PlatinVault.Desktop.DesktopApp).Assembly.Location) ?? string.Empty);
    }

    [Fact]
    public void The_test_program_itself_is_unsigned_so_the_updater_keeps_the_checksum_only_rule()
    {
        // Documents today's situation: builds of this repository are not signed (yet)
        Assert.NotEqual(SignatureState.Valid, Verifier.Verify(Environment.ProcessPath!, false).State);
    }

    /// <summary>One signed copy of whoami.exe for the whole test class run (signing starts a PowerShell, a few seconds).</summary>
    private static class SelfSigned
    {
        private static readonly Lazy<(string? Path, string? WhyNot)> Made = new(Create);

        public static string? Path => Made.Value.Path;

        public static string? WhyNot => Made.Value.WhyNot;

        private static (string?, string?) Create()
        {
            var source = System.IO.Path.Combine(Environment.SystemDirectory, "whoami.exe");
            if (!File.Exists(source)) return (null, "whoami.exe not found");
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pv-wintrust-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(folder);
            var target = System.IO.Path.Combine(folder, "impostor.exe");
            var script = System.IO.Path.Combine(folder, "sign.ps1");
            File.Copy(source, target);
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp folder, cleaned by Windows later */ }
            };
            File.WriteAllText(script, """
                param([string]$Target)
                $ErrorActionPreference = 'Stop'
                $rsa = [System.Security.Cryptography.RSA]::Create(2048)
                $dn = New-Object System.Security.Cryptography.X509Certificates.X500DistinguishedName('CN=SignPath Foundation, O=SignPath Foundation, C=AT')
                $req = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest($dn, $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
                $oids = New-Object System.Security.Cryptography.OidCollection
                $null = $oids.Add((New-Object System.Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3')))
                $req.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($oids, $false)))
                $cert = $req.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1), [DateTimeOffset]::Now.AddDays(10))
                # In memory only: exported and re-imported with an ephemeral key, never added to a certificate store
                $pfx = $cert.Export('Pfx', 'x')
                $signer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, 'x', [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
                $null = Set-AuthenticodeSignature -LiteralPath $Target -Certificate $signer -HashAlgorithm SHA256
                """);
            try
            {
                var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, target }) info.ArgumentList.Add(argument);
                using var process = Process.Start(info);
                if (process is null) return (null, "powershell.exe did not start");
                var error = process.StandardError.ReadToEndAsync();
                process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(120_000)) { process.Kill(true); return (null, "signing timed out"); }
                if (process.ExitCode != 0) return (null, "signing failed: " + error.Result);
                return (target, null);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return (null, "PowerShell not available: " + ex.Message);
            }
        }
    }
}


public sealed class UpdateFolderCleanupTests
{
    [Fact]
    public void Old_installers_and_partial_downloads_go_the_installer_being_installed_and_foreign_files_stay()
    {
        using var temp = new TempFolder();
        var folder = temp.Path;
        var keep = Path.Combine(folder, "PlatinVault-Setup-v2026.11.15.exe");
        foreach (var name in new[] { "PlatinVault-Setup-v2026.11.14.exe", "PlatinVault-Setup-v2026.11.13.exe.part", "PlatinVault-Setup-v2026.11.15.exe", "notes.txt", "PlatinVault-Setup-v2026.11.14.exe.sha256" })
        {
            File.WriteAllText(Path.Combine(folder, name), "x");
        }

        Assert.Equal(2, UpdateChecker.CleanupOldInstallers(folder, keep));
        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "PlatinVault-Setup-v2026.11.14.exe.sha256")));
        Assert.False(File.Exists(Path.Combine(folder, "PlatinVault-Setup-v2026.11.14.exe")));
        Assert.False(File.Exists(Path.Combine(folder, "PlatinVault-Setup-v2026.11.13.exe.part")));
    }

    [Fact]
    public void A_locked_installer_stays_and_a_missing_folder_is_fine()
    {
        using var temp = new TempFolder();
        var locked = Path.Combine(temp.Path, "PlatinVault-Setup-v2026.11.14.exe");
        File.WriteAllText(locked, "x");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(0, UpdateChecker.CleanupOldInstallers(temp.Path));
            Assert.True(File.Exists(locked));
        }
        Assert.Equal(1, UpdateChecker.CleanupOldInstallers(temp.Path));
        Assert.Equal(0, UpdateChecker.CleanupOldInstallers(temp.Sub("does-not-exist")));
    }
}

public sealed class DesktopFolderHostTests
{
    private static LocalDesktopHost Host(bool withFolders = true) => new(recordOnly: true, null, null,
        withFolders
            ?
            [
                new(DesktopFolderKinds.Program, @"C:\Test\Programs\PlatinVault"),
                new(DesktopFolderKinds.Data, @"C:\Test\PlatinVault"),
                new(DesktopFolderKinds.Backups, @"C:\Test\PlatinVault\backups"),
                new(DesktopFolderKinds.Logs, @"C:\Test\PlatinVault\logs"),
            ]
            : null,
        recordFolderOpens: true);

    [Fact]
    public void Lists_the_four_folders_and_only_records_an_open_in_a_test_run()
    {
        var host = Host();
        Assert.Equal(DesktopFolderKinds.All, host.Folders.Select(f => f.Kind));
        Assert.True(host.OpenFolder(DesktopFolderKinds.Backups).Opened);
        Assert.Equal([DesktopFolderKinds.Backups], host.OpenedFolders);
    }

    [Theory]
    [InlineData("program", true)]
    [InlineData("logs", true)]
    [InlineData("Program", false)]
    [InlineData("..\\..\\Windows", false)]
    [InlineData("", false)]
    public void Only_the_four_kinds_are_known(string kind, bool known) => Assert.Equal(known, DesktopFolderKinds.IsKnown(kind));

    [Fact]
    public void A_kind_the_host_does_not_list_is_not_opened()
    {
        var host = Host(withFolders: false);
        Assert.Empty(host.Folders);
        Assert.False(host.OpenFolder(DesktopFolderKinds.Data).Opened);
        Assert.Empty(host.OpenedFolders);
    }
}

public sealed class UpdateInProgressCleanupTests
{
    private static string Make(string folder, string name, TimeSpan age)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    [Fact]
    public void The_installer_of_an_update_in_progress_and_its_part_file_are_never_deleted()
    {
        using var temp = new TempFolder();
        var installer = Make(temp.Path, "PlatinVault-Setup-v2026.11.15.exe", TimeSpan.FromHours(5));
        var part = Make(temp.Path, "PlatinVault-Setup-v2026.11.15.exe.part", TimeSpan.FromHours(5));
        var old = Make(temp.Path, "PlatinVault-Setup-v2026.11.14.exe", TimeSpan.FromHours(5));

        // even without an age rule and even for a file that is hours old
        Assert.Equal(1, UpdateChecker.CleanupOldInstallers(temp.Path, installer));
        Assert.True(File.Exists(installer));
        Assert.True(File.Exists(part));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void The_delayed_pass_leaves_young_installers_alone_even_when_nothing_is_tracked()
    {
        using var temp = new TempFolder();
        var young = Make(temp.Path, "PlatinVault-Setup-v2026.11.15.exe", TimeSpan.FromMinutes(1));
        var justUnderLimit = Make(temp.Path, "PlatinVault-Setup-v2026.11.13.exe", UpdateChecker.DelayedCleanupMinAge - TimeSpan.FromMinutes(1));
        var old = Make(temp.Path, "PlatinVault-Setup-v2026.11.14.exe", UpdateChecker.DelayedCleanupMinAge + TimeSpan.FromMinutes(1));

        Assert.Equal(1, UpdateChecker.CleanupOldInstallers(temp.Path, keep: null, UpdateChecker.DelayedCleanupMinAge));
        Assert.True(File.Exists(young));
        Assert.True(File.Exists(justUnderLimit));
        Assert.False(File.Exists(old));
        Assert.Equal(TimeSpan.FromMinutes(10), UpdateChecker.DelayedCleanupMinAge);
    }

    [Fact]
    public void Target_path_is_the_file_the_download_ends_up_as()
    {
        var offer = new UpdateOffer(PlatinVault.Versioning.ReleaseVersion.Parse("v2026.11.15")!.Value, "https://github.com/x", "https://github.com/y", "PlatinVault-Setup-v2026.11.15.exe");
        Assert.Equal(Path.Combine(@"C:\data\updates", "PlatinVault-Setup-v2026.11.15.exe"), UpdateChecker.TargetPath(offer, @"C:\data\updates"));
    }
}
