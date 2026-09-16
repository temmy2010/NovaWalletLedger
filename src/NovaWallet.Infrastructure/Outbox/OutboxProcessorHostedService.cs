using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.Infrastructure.Outbox;

public class OutboxProcessorHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorHostedService> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(3);

    public OutboxProcessorHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Processor background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred while processing outbox messages.");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("Outbox Processor background service stopped.");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pendingMessages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.RetryCount < 5)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
            return;

        _logger.LogInformation("Processing {Count} pending outbox messages...", pendingMessages.Count);

        foreach (var message in pendingMessages)
        {
            try
            {
                // In production, publish to Kafka / RabbitMQ / Azure Service Bus
                // For this service, we simulate external domain event publication with structured logging
                _logger.LogInformation("[OUTBOX PUBLISHED] Event: {EventType}, Id: {EventId}, Payload: {Payload}",
                    message.EventType, message.Id, message.Payload);

                message.MarkProcessed();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish outbox message {EventId}", message.Id);
                message.MarkFailed(ex.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
