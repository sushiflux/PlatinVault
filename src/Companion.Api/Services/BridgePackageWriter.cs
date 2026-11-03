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

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static async Task WriteAsync(Stream output, BridgeBundle bundle, HandbookFiles handbooks, LicenseFiles licenses, string serverOrigin, string token, string language, CancellationToken ct)
    {
        await using var temp = new FileStream(
            Path.Combine(Path.GetTempPath(), $"platinvault-bridge-{Guid.NewGuid():n}.zip"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

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
}
