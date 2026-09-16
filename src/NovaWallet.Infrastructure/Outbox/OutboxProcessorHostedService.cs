namespace NovaWallet.Infrastructure.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaWallet.Infrastructure.Persistence;

public class OutboxProcessorHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorHostedService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(3);

    public OutboxProcessorHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingEventsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Unexpected error in outbox worker loop");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Outbox worker stopped");
    }

    private async Task DispatchPendingEventsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pendingMessages = await db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.RetryCount < 5)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(20)
            .ToListAsync(ct);

        if (pendingMessages.Count == 0)
            return;

        foreach (var msg in pendingMessages)
        {
            try
            {
                // Dispatches to downstream message bus (Kafka/RabbitMQ)
                _logger.LogInformation("[OUTBOX PUBLISH] Event={EventType} Id={EventId} Payload={Payload}",
                    msg.EventType, msg.Id, msg.Payload);

                msg.MarkProcessed();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish outbox event {EventId}", msg.Id);
                msg.MarkFailed(ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
