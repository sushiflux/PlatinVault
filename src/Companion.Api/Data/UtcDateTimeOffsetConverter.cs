using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Companion.Api.Data;

/// <summary>
/// Stores every <see cref="DateTimeOffset"/> as fixed-width UTC text, e.g. <c>2026-11-02 18:04:05.1234567+00:00</c>.
/// <para>
/// Why: SQLite has no date type, and EF Core's SQLite provider cannot compare or order <see cref="DateTimeOffset"/> values in SQL
/// (the stored offset would make text order wrong). PlatinVault only ever stores UTC (PostgreSQL's <c>timestamptz</c> did the same), so
/// one fixed format with a fixed offset sorts exactly like the instants it stands for: <c>WHERE x &lt; @cutoff</c>, <c>ORDER BY</c> and
/// <c>MIN/MAX</c> work in the database and the file stays readable. Values read back carry offset zero, as before.
/// </para>
/// </summary>
public sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, string>(v => ToStore(v), v => FromStore(v))
{
    public const string Format = "yyyy-MM-dd HH:mm:ss.fffffff'+00:00'";

    public static string ToStore(DateTimeOffset value) => value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    public static DateTimeOffset FromStore(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToUniversalTime();
}
