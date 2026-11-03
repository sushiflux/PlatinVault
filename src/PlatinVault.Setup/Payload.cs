using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PlatinVault.Setup;

/// <summary>
/// The program the installer carries: a ZIP appended to the end of the setup exe (scripts/build-windows.ps1), found through a trailer -
/// "PVPAYLD1", offset and length of the ZIP (little-endian Int64) and its SHA-256. The trailer is searched in the last 64 KiB, so a
/// later Authenticode signature (which appends its certificate table) does not break it. The checksum is verified before anything is
/// extracted: a damaged download never becomes a half installation. For tests the ZIP can come from a file (<c>--payload</c>).
/// </summary>
internal sealed class Payload : IDisposable
{
    public static readonly byte[] Magic = "PVPAYLD1"u8.ToArray();
    public const int TrailerSize = 8 + 8 + 8 + 32;

    private readonly string _file;
    private readonly long _offset;
    private readonly long _length;
    private readonly byte[] _sha256;
    private ZipArchive? _zip;

    private Payload(string file, long offset, long length, byte[] sha256)
    {
        _file = file;
        _offset = offset;
        _length = length;
        _sha256 = sha256;
    }

    public long Length => _length;

    /// <summary>The payload appended to <paramref name="exe"/>, or null when there is none (a plain build of the setup).</summary>
    public static Payload? FromExe(string exe)
    {
        using var stream = File.OpenRead(exe);
        var window = (int)Math.Min(stream.Length, 64 * 1024);
        var tail = new byte[window];
        stream.Seek(-window, SeekOrigin.End);
        stream.ReadExactly(tail);
        var at = tail.AsSpan().LastIndexOf(Magic);
        if (at < 0 || at + TrailerSize > tail.Length) return null;
        var offset = BitConverter.ToInt64(tail, at + 8);
        var length = BitConverter.ToInt64(tail, at + 16);
        var hash = tail.AsSpan(at + 24, 32).ToArray();
        var trailerPosition = stream.Length - window + at;
        if (offset <= 0 || length <= 0 || offset + length > trailerPosition) return null;
        return new Payload(exe, offset, length, hash);
    }

    /// <summary>A plain ZIP file as payload (tests); its checksum is computed, not checked.</summary>
    public static Payload FromZip(string zip)
    {
        using var stream = File.OpenRead(zip);
        return new Payload(zip, 0, stream.Length, SHA256.HashData(stream));
    }

    /// <summary>Appends <paramref name="zip"/> plus trailer to <paramref name="exe"/> (used by the build script through "--append-payload").</summary>
    public static void Append(string exe, string zip)
    {
        using var output = new FileStream(exe, FileMode.Append, FileAccess.Write);
        var offset = output.Position;
        byte[] hash;
        using (var input = File.OpenRead(zip))
        {
            hash = SHA256.HashData(input);
            input.Position = 0;
            input.CopyTo(output);
        }
        var length = output.Position - offset;
        output.Write(Magic);
        output.Write(BitConverter.GetBytes(offset));
        output.Write(BitConverter.GetBytes(length));
        output.Write(hash);
    }

    public bool Verify()
    {
        using var stream = Open();
        return SHA256.HashData(stream).AsSpan().SequenceEqual(_sha256);
    }

    private Stream Open()
    {
        var file = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return new SubStream(file, _offset, _length);
    }

    private ZipArchive Zip => _zip ??= new ZipArchive(Open(), ZipArchiveMode.Read, leaveOpen: false);

    public string? ReadText(string entry)
    {
        // Some ZIP tools write backslashes into entry names (Windows PowerShell 5.1 Compress-Archive): compare normalised
        var wanted = entry.Replace('\\', '/');
        var e = Zip.Entries.FirstOrDefault(x => string.Equals(x.FullName.Replace('\\', '/'), wanted, StringComparison.OrdinalIgnoreCase));
        if (e is null) return null;
        using var reader = new StreamReader(e.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public long UncompressedSize => Zip.Entries.Sum(e => e.Length);

    /// <summary>Extracts everything below <paramref name="target"/>, refusing entries that would leave it (zip slip).</summary>
    public void ExtractTo(string target, IProgress<float>? progress, CancellationToken ct)
    {
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        var total = Math.Max(1, UncompressedSize);
        long done = 0;
        foreach (var entry in Zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Unsafe path in payload: {entry.FullName}");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
            done += entry.Length;
            progress?.Report(done / (float)total);
        }
    }

    public void Dispose() => _zip?.Dispose();

    /// <summary>A read-only window into another stream.</summary>
    private sealed class SubStream(Stream inner, long offset, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, length);
        }

        public override int Read(byte[] buffer, int count, int size) => Read(buffer.AsSpan(count, size));

        public override int Read(Span<byte> buffer)
        {
            var remaining = length - _position;
            if (remaining <= 0) return 0;
            if (buffer.Length > remaining) buffer = buffer[..(int)remaining];
            inner.Position = offset + _position;
            var read = inner.Read(buffer);
            _position += read;
            return read;
        }

        public override long Seek(long position, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => position,
            SeekOrigin.Current => _position + position,
            _ => length + position,
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset2, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
