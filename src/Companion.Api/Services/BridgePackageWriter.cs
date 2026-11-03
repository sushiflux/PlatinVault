using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

namespace Companion.Api.Services;

/// <summary>
/// Writes the "ready to run" bridge ZIP: the exe, the templates from package/ (autostart scripts, readme), the end-user handbook
/// PDFs (German and English, next to the exe so the bridge finds them), the licence documents (LICENSE - the GNU AGPL v3
/// PlatinVault is licensed under - and THIRD-PARTY-NOTICES.md - the self-contained exe carries the .NET runtime and other open-source parts, whose licences require
/// their notices in every copy) and a generated bridge.json with the server address, the token and the language ("de"/"en", the
/// one the bridge speaks from its very first start; once connected it follows the server's <c>ui.language</c> itself).
/// The archive is built in a temporary file and then copied to the response: ZipArchive flushes entries synchronously on
/// dispose, which Kestrel forbids on the response stream, and a seekable target gives plain, widely compatible ZIP entries.
/// The temp file is deleted when closed; the exe is never held in memory.
/// </summary>
internal static class BridgePackageWriter
{
    public const string SettingsFileName = "bridge.json";

    /// <summary>Name of the Linux download and of the folder inside it.</summary>
    public const string LinuxPackageFileName = "PlatinVault-Bridge-linux-x64.tar.gz";

    private const string LinuxPackageFolder = "PlatinVault-Bridge";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static async Task WriteAsync(Stream output, BridgeBundle bundle, HandbookFiles handbooks, LicenseFiles licenses, string serverOrigin, string token, string language, CancellationToken ct)
    {
        await using var temp = CreateTempFile("zip");

        var handbookList = handbooks.FindAll().ToList();
        var licenseList = licenses.FindAll().ToList();
        var handbookNames = handbookList.Select(h => h.ZipEntryName)
            .Concat(licenseList.Select(l => l.FileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using (var zip = await ZipArchive.CreateAsync(temp, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, ct))
        {
            var exeEntry = zip.CreateEntry(BridgeBundle.ExeName, CompressionLevel.Fastest);
            await using (var entryStream = await exeEntry.OpenAsync(ct))
            await using (var exe = bundle.OpenExe())
            {
                await exe.CopyToAsync(entryStream, ct);
            }

            foreach (var (entryName, path) in bundle.PackageFiles())
            {
                // The generated settings file and the exe from the bundle always win over same-named template files
                if (entryName.Equals(SettingsFileName, StringComparison.OrdinalIgnoreCase) ||
                    entryName.Equals(BridgeBundle.ExeName, StringComparison.OrdinalIgnoreCase) ||
                    handbookNames.Contains(entryName))
                {
                    continue;
                }

                var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                await using var entryStream = await entry.OpenAsync(ct);
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
                await file.CopyToAsync(entryStream, ct);
            }

            // The handbook is part of the product: whoever unpacks the ZIP can read it right away (and the bridge opens it from its tray menu)
            foreach (var handbook in handbookList)
            {
                var entry = zip.CreateEntry(handbook.ZipEntryName, CompressionLevel.Optimal);
                await using var entryStream = await entry.OpenAsync(ct);
                await using var file = new FileStream(handbook.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
                await file.CopyToAsync(entryStream, ct);
            }

            // The licence documents travel with every copy of the exe (comments for the owner are stripped, see LicenseFiles)
            foreach (var license in licenseList)
            {
                var entry = zip.CreateEntry(license.FileName, CompressionLevel.Optimal);
                await using var entryStream = await entry.OpenAsync(ct);
                await entryStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(LicenseFiles.ReadShippedText(license.Path)), ct);
            }

            var settingsEntry = zip.CreateEntry(SettingsFileName, CompressionLevel.Optimal);
            await using (var entryStream = await settingsEntry.OpenAsync(ct))
            {
                await JsonSerializer.SerializeAsync(entryStream, new { server = serverOrigin, token, language }, Indented, ct);
            }
        }

        temp.Position = 0;
        await temp.CopyToAsync(output, ct);
    }

    /// <summary>
    /// The "ready to run" bridge package for Linux: a <c>.tar.gz</c> with one top-level folder <c>PlatinVault-Bridge/</c> holding the binary, the
    /// templates from package-linux (install.sh, uninstall.sh, readme), the handbook PDFs, the licence documents and a generated
    /// <c>bridge.json</c> (server address, token, language; mode 0600 because it holds the token). The scripts and the binary are executable.
    /// Built in a temporary file like the ZIP (see above), the binary is streamed from disk.
    /// </summary>
    public static async Task WriteLinuxAsync(Stream output, BridgeBundle bundle, HandbookFiles handbooks, LicenseFiles licenses, string serverOrigin, string token, string language, CancellationToken ct)
    {
        await using var temp = CreateTempFile("tar.gz");

        var handbookList = handbooks.FindAll().ToList();
        var licenseList = licenses.FindAll().ToList();
        var reserved = handbookList.Select(h => h.ZipEntryName)
            .Concat(licenseList.Select(l => l.FileName))
            .Append(SettingsFileName)
            .Append(bundle.FileNameOf(BridgePlatform.Linux))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var modified = DateTimeOffset.UtcNow;
        const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        const UnixFileMode Plain = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        await using (var gzip = new GZipStream(temp, CompressionLevel.Optimal, leaveOpen: true))
        await using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            async Task AddAsync(string name, Stream data, UnixFileMode mode)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, $"{LinuxPackageFolder}/{name}")
                {
                    Mode = mode,
                    ModificationTime = modified,
                    Uid = 0,
                    Gid = 0,
                    DataStream = data,
                };
                await tar.WriteEntryAsync(entry, ct);
            }

            var folder = new PaxTarEntry(TarEntryType.Directory, LinuxPackageFolder + "/") { Mode = Executable, ModificationTime = modified };
            await tar.WriteEntryAsync(folder, ct);

            await using (var binary = bundle.OpenExe(BridgePlatform.Linux))
            {
                await AddAsync(bundle.FileNameOf(BridgePlatform.Linux), binary, Executable);
            }

            foreach (var (entryName, path) in bundle.PackageFiles(BridgePlatform.Linux))
            {
                // The generated settings file and the binary from the bundle always win over same-named template files
                if (reserved.Contains(entryName)) continue;

                // Shell scripts are executable, text files are not; the scripts are written with LF (a CRLF checkout must not break them)
                var script = entryName.EndsWith(".sh", StringComparison.Ordinal);
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
                if (script)
                {
                    using var reader = new StreamReader(file);
                    var text = (await reader.ReadToEndAsync(ct)).Replace("\r\n", "\n");
                    await using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
                    await AddAsync(entryName, bytes, Executable);
                }
                else
                {
                    await AddAsync(entryName, file, Plain);
                }
            }

            foreach (var handbook in handbookList)
            {
                await using var file = new FileStream(handbook.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
                await AddAsync(handbook.ZipEntryName, file, Plain);
            }

            foreach (var license in licenseList)
            {
                await using var text = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(LicenseFiles.ReadShippedText(license.Path)));
                await AddAsync(license.FileName, text, Plain);
            }

            if (bundle.VersionOf(BridgePlatform.Linux) is { } version)
            {
                await using var versionFile = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(version));
                await AddAsync(BridgeBundle.VersionFileName, versionFile, Plain);
            }

            await using (var settings = new MemoryStream())
            {
                await JsonSerializer.SerializeAsync(settings, new { server = serverOrigin, token, language }, Indented, ct);
                settings.Position = 0;
                await AddAsync(SettingsFileName, settings, Private);
            }
        }

        temp.Position = 0;
        await temp.CopyToAsync(output, ct);
    }

    /// <summary>
    /// The temporary file the package is built in. It holds the access token, and /tmp is readable by every user on a shared Linux host, so on
    /// Unix it is created readable by its owner only (the mode is set at creation, there is no moment with a wider one). Deleted when closed.
    /// </summary>
    private static FileStream CreateTempFile(string extension)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 81920,
            Options = FileOptions.DeleteOnClose | FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(Path.Combine(Path.GetTempPath(), $"platinvault-bridge-{Guid.NewGuid():n}.{extension}"), options);
    }
}
