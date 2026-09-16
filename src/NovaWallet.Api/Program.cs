using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using NovaWallet.Api.Middleware;
using NovaWallet.Application;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Infrastructure;
using NovaWallet.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Structured Logging with Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 3. Rate Limiting Middleware (Stretch Goal)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("transfer_policy", opt =>
    {
        opt.PermitLimit = 50;
        opt.Window = TimeSpan.FromSeconds(10);
        opt.QueueLimit = 10;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// 4. Controllers & Routing
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 5. Health Checks (Stretch Goal)
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("Database");

// 6. OpenAPI / Swagger Documentation
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NovaWallet Ledger Service API",
        Version = "v1",
        Description = "FirstBank NovaPay Digital Factory - Concurrency-Safe Financial Wallet Ledger Backend Service.\n\n" +
                      "- All monetary amounts in Kobo (integer math).\n" +
                      "- Concurrency safe, deadlock-free wallet transfers.\n" +
                      "- Idempotency-Key support with SHA-256 payload verification.\n" +
                      "- Server-side daily limit (₦500,000/day reset at midnight WAT).\n" +
                      "- Append-only immutable audit trail and Transactional Outbox pattern.",
        Contact = new OpenApiContact
        {
            Name = "FirstBank Digital Factory - NovaPay Engineering",
            Url = new Uri("https://www.firstbanknigeria.com")
        }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer {token}'.\n" +
                      "Use the /api/Auth/token endpoint to generate a test token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// 7. Request Pipeline Configuration
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "NovaWallet Ledger Service v1");
    c.RoutePrefix = "swagger";
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health Check Endpoints
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// 8. Auto Database Initialization & Demo Data Seeding
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Ensuring database schema exists...");
        await dbContext.Database.EnsureCreatedAsync();

        if (!await dbContext.Wallets.AnyAsync())
        {
            logger.LogInformation("Seeding demo wallets for testing and evaluation...");

            var wallet1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var wallet2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

            var wallet1 = new Wallet(wallet1Id, "CUST-FIRSTBANK-001", KycTier.Tier3, "22233344455", "11122233344");
            wallet1.Credit(10_000_000L); // ₦100,000.00 in kobo

            var wallet2 = new Wallet(wallet2Id, "CUST-FIRSTBANK-002", KycTier.Tier2, "33344455566", "22233344455");
            wallet2.Credit(5_000_000L); // ₦50,000.00 in kobo

            var seedTx1 = new Transaction(Guid.NewGuid(), wallet1.Id, TransactionType.Credit, 10_000_000L, 10_000_000L, "SEED-NIP-001", null, "Initial Seed Balance", "NIP");
            var seedTx2 = new Transaction(Guid.NewGuid(), wallet2.Id, TransactionType.Credit, 5_000_000L, 5_000_000L, "SEED-NIP-002", null, "Initial Seed Balance", "NIP");

            var seedAudit1 = new AuditLog(Guid.NewGuid(), wallet1.Id, "SEED_CREDIT", 10_000_000L, 0, 10_000_000L, "SEED-NIP-001", "SYSTEM-INIT", "SEEDER");
            var seedAudit2 = new AuditLog(Guid.NewGuid(), wallet2.Id, "SEED_CREDIT", 5_000_000L, 0, 5_000_000L, "SEED-NIP-002", "SYSTEM-INIT", "SEEDER");

            dbContext.Wallets.AddRange(wallet1, wallet2);
            dbContext.Transactions.AddRange(seedTx1, seedTx2);
            dbContext.AuditLogs.AddRange(seedAudit1, seedAudit2);

            await dbContext.SaveChangesAsync();
            logger.LogInformation("Demo wallets seeded:\n - Wallet 1: {W1} (Balance: ₦100,000)\n - Wallet 2: {W2} (Balance: ₦50,000)", wallet1Id, wallet2Id);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database initialization/seeding.");
    }
}

app.Run();

// Make implicit Program class public for WebApplicationFactory in integration tests
public partial class Program { }
