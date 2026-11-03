using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Companion.Api.Data;

/// <summary>Lets "dotnet ef migrations add" build the context without starting the web host (the SQLite file is never opened).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CompanionDbContext>
{
    public CompanionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlite("Data Source=design_time_only.db")
            .Options;
        return new CompanionDbContext(options);
    }
}
