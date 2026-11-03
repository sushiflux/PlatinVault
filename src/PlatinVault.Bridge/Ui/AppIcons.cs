using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PlatinVault.Bridge.Ui;

/// <summary>
/// The app's trophy icon from the embedded <c>branding/platinvault.ico</c>: bitmaps and icons in any size for windows and
/// the tray, plus a dimmed grey variant for "not connected". The .ico is parsed here (its entries are PNG compressed), so
/// the exact size for the current DPI can be picked and nothing depends on how the OS resolves multi-size icons.
/// </summary>
internal static class AppIcons
{
    private static readonly List<(int Size, byte[] Data)> Entries = LoadEntries();

    /// <summary>The trophy at exactly <paramref name="size"/> pixels (scaled from the nearest larger entry).</summary>
    public static Bitmap GetBitmap(int size)
    {
        var source = Entries.Where(e => e.Size >= size).OrderBy(e => e.Size).FirstOrDefault();
        if (source.Data is null) source = Entries.OrderByDescending(e => e.Size).First();

        using var stream = new MemoryStream(source.Data);
        using var decoded = new Bitmap(stream);
        if (decoded.Width == size) return new Bitmap(decoded);

        var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(decoded, new Rectangle(0, 0, size, size));
        return scaled;
    }

    /// <summary>An icon the caller owns and must dispose.</summary>
    public static Icon GetIcon(int size, bool dimmed = false)
    {
        using var bitmap = dimmed ? Dim(GetBitmap(size)) : GetBitmap(size);
        var handle = bitmap.GetHicon();
        try
        {
            using var wrapper = Icon.FromHandle(handle);
            return (Icon)wrapper.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>Grey and half transparent: the same trophy, visibly "off".</summary>
    private static Bitmap Dim(Bitmap source)
    {
        var matrix = new ColorMatrix(
        [
            [0.30f, 0.30f, 0.30f, 0f, 0f],
            [0.59f, 0.59f, 0.59f, 0f, 0f],
            [0.11f, 0.11f, 0.11f, 0f, 0f],
            [0f, 0f, 0f, 0.6f, 0f],
            [0f, 0f, 0f, 0f, 1f],
        ]);

        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(matrix);
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel, attributes);
        }
        source.Dispose();
        return result;
    }

    private static List<(int Size, byte[] Data)> LoadEntries()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("platinvault.ico")
                           ?? throw new InvalidOperationException("Das App-Symbol fehlt in der Exe.");
        using var reader = new BinaryReader(stream);

        reader.ReadUInt16(); // reserved
        reader.ReadUInt16(); // type (1 = icon)
        var count = reader.ReadUInt16();

        var directory = new List<(int Size, int Length, int Offset)>();
        for (var i = 0; i < count; i++)
        {
            int width = reader.ReadByte();
            reader.ReadByte(); // height (same as width for app icons)
            reader.ReadBytes(6); // colors, reserved, planes, bits per pixel
            var length = reader.ReadInt32();
            var offset = reader.ReadInt32();
            directory.Add((width == 0 ? 256 : width, length, offset));
        }

        var entries = new List<(int Size, byte[] Data)>();
        foreach (var (size, length, offset) in directory)
        {
            stream.Position = offset;
            entries.Add((size, reader.ReadBytes(length)));
        }
        return entries;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
