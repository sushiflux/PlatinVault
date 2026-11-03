using Companion.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Companion.Api.Data;

/// <summary>
/// When the server backs up its database (<see cref="DatabaseBackupService"/>): right after the start (the migrations have run by then),
/// every <see cref="BackupOptions.IntervalHours"/> hours, and once more when the host stops cleanly (Docker <c>stop</c>, Ctrl+C, the
/// Windows program quitting - also before every update). Hosts with <c>Companion:Backups:Enabled = false</c> make none.
/// </summary>
public sealed class DatabaseBackupWorker(DatabaseBackupService backups, IOptions<CompanionOptions> options, ILogger<DatabaseBackupWorker> logger)
    : BackgroundService
{
    private bool Enabled => options.Value.Backups.Enabled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            logger.LogInformation("Automatische Sicherungen sind für diesen Server ausgeschaltet.");
            return;
        }
        try
        {
            // Let the start finish first (the window, the first requests)
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await backups.RunAsync("Start", stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(0.1, options.Value.Backups.IntervalHours)));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await backups.RunAsync("alle 6 Stunden", stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping: the last backup follows in StopAsync
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (!Enabled) return;
        // The last changes before a shutdown are never missed. A backup of a few MB takes well under a second; the host's
        // shutdown timeout is the upper limit.
        await backups.RunAsync("Beenden", cancellationToken);
    }
}
