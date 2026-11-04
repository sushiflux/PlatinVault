using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Data;

/// <summary>The database of PlatinVault: one SQLite file per installation (see <see cref="SqliteDatabase"/>).</summary>
public sealed class CompanionDbContext(DbContextOptions<CompanionDbContext> options) : DbContext(options)
{
    public DbSet<GameEntity> Games => Set<GameEntity>();
    public DbSet<GuideTabEntity> GuideTabs => Set<GuideTabEntity>();
    public DbSet<ProgressCheckEntity> ProgressChecks => Set<ProgressCheckEntity>();
    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();
    public DbSet<PlayerCountSampleEntity> PlayerCountSamples => Set<PlayerCountSampleEntity>();
    public DbSet<PlayerCountDailyEntity> PlayerCountDaily => Set<PlayerCountDailyEntity>();
    public DbSet<PlaytimeSnapshotEntity> PlaytimeSnapshots => Set<PlaytimeSnapshotEntity>();
    public DbSet<GuideTabTranslationEntity> GuideTabTranslations => Set<GuideTabTranslationEntity>();
    public DbSet<GameTranslationEntity> GameTranslations => Set<GameTranslationEntity>();
    public DbSet<LeaseEntity> Leases => Set<LeaseEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot compare or order DateTimeOffset in SQL: every one is stored as sortable UTC text (see the converter). Only for
        // SQLite - the one-time import tool (tools/migrate-postgres-to-sqlite) reads the old PostgreSQL database through this same model.
        if (Database.IsSqlite())
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameEntity>(entity =>
        {
            entity.ToTable("games");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Slug).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Subtitle).HasMaxLength(500);
            entity.Property(x => x.CoverUrl).HasMaxLength(1000);
            entity.Property(x => x.HeroBannerUrl).HasMaxLength(1000);
            entity.Property(x => x.AccentColor).HasMaxLength(40);
            entity.Property(x => x.EstimatedHours).HasMaxLength(160);
            entity.Property(x => x.MissablesInfo).HasMaxLength(240);
            entity.Property(x => x.DifficultyInfo).HasMaxLength(240);
            entity.Property(x => x.GenreTags).HasMaxLength(240);
            entity.Property(x => x.ContentLanguage).HasMaxLength(8);
            entity.Property(x => x.SteamGenresLanguage).HasMaxLength(8);
            entity.Property(x => x.ReviewScoreDesc).HasMaxLength(60);
            entity.Property(x => x.ReleaseDate).HasMaxLength(60);
            entity.HasIndex(x => x.SteamAppId);
        });

        modelBuilder.Entity<GuideTabEntity>(entity =>
        {
            entity.ToTable("guide_tabs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.GameId, x.Slug }).IsUnique();
            entity.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Icon).HasMaxLength(120);
            entity.Property(x => x.GroupKey).HasMaxLength(16);
            entity.Property(x => x.TopicId).HasMaxLength(80);
            entity.HasOne(x => x.Game)
                .WithMany(g => g.Tabs)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuideTabTranslationEntity>(entity =>
        {
            entity.ToTable("guide_tab_translations");
            entity.HasKey(x => new { x.TabId, x.Language });
            entity.Property(x => x.Language).HasMaxLength(8).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(240).IsRequired();
            entity.Property(x => x.SourceHash).HasMaxLength(64).IsRequired();
            entity.HasOne(x => x.Tab)
                .WithMany()
                .HasForeignKey(x => x.TabId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameTranslationEntity>(entity =>
        {
            entity.ToTable("game_translations");
            entity.HasKey(x => new { x.GameId, x.Language });
            entity.Property(x => x.Language).HasMaxLength(8).IsRequired();
            entity.Property(x => x.Subtitle).HasMaxLength(500);
            entity.Property(x => x.EstimatedHours).HasMaxLength(160);
            entity.Property(x => x.MissablesInfo).HasMaxLength(240);
            entity.Property(x => x.DifficultyInfo).HasMaxLength(240);
            entity.Property(x => x.GenreTags).HasMaxLength(240);
            entity.Property(x => x.SourceHash).HasMaxLength(64).IsRequired();
            entity.HasOne(x => x.Game)
                .WithMany()
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProgressCheckEntity>(entity =>
        {
            entity.ToTable("progress_checks");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.GameId, x.ItemKey }).IsUnique();
            entity.Property(x => x.ItemKey).HasMaxLength(200).IsRequired();
            entity.HasOne(x => x.Game)
                .WithMany(g => g.ProgressChecks)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // History recorded by HistoryRecorderWorker: the composite keys double as the (app, time) indexes the charts read
        modelBuilder.Entity<PlayerCountSampleEntity>(entity =>
        {
            entity.ToTable("player_count_samples");
            entity.HasKey(x => new { x.AppId, x.SampledAt });
            entity.HasIndex(x => x.SampledAt);
        });

        modelBuilder.Entity<PlayerCountDailyEntity>(entity =>
        {
            entity.ToTable("player_count_daily");
            entity.HasKey(x => new { x.AppId, x.Date });
        });

        modelBuilder.Entity<PlaytimeSnapshotEntity>(entity =>
        {
            entity.ToTable("playtime_snapshots");
            entity.HasKey(x => new { x.AppId, x.Date });
        });

        modelBuilder.Entity<LeaseEntity>(entity =>
        {
            entity.ToTable("app_leases");
            entity.HasKey(x => x.Name);
            entity.Property(x => x.Name).HasMaxLength(80);
            entity.Property(x => x.Owner).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<AppSettingEntity>(entity =>
        {
            entity.ToTable("app_settings");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(120);
        });
    }
}

