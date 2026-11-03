using Companion.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PlatinVault.Windows.Tests;

/// <summary>A real, migrated PlatinVault database file in a test folder with one marker row ("test.note" in app_settings).</summary>
internal static class SqliteTestDatabase
{
    private const string Key = "test.note";

    public static CompanionDbContext Context(string file) =>
        new(new DbContextOptionsBuilder<CompanionDbContext>().UseSqlite(SqliteDatabase.ConnectionString(file, pooling: false)).Options);

    public static async Task CreateAsync(string file, string note)
    {
        await using var db = Context(file);
        await db.Database.MigrateAsync();
        db.AppSettings.Add(new AppSettingEntity { Key = Key, Value = note });
        await db.SaveChangesAsync();
    }

    public static async Task SetNoteAsync(string file, string note)
    {
        await using var db = Context(file);
        (await db.AppSettings.SingleAsync(s => s.Key == Key)).Value = note;
        await db.SaveChangesAsync();
    }

    public static async Task<string> NoteAsync(string file)
    {
        await using var db = Context(file);
        var value = await db.AppSettings.AsNoTracking().Where(s => s.Key == Key).Select(s => s.Value).SingleAsync();
        SqliteConnection.ClearAllPools();
        return value;
    }
}
