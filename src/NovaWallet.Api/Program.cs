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

// Structured console logging with Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Core layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Rate limiting on transfer endpoints
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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Container health probes
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("Database");

// Swagger documentation
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NovaWallet Ledger Service API",
        Version = "v1",
        Description = "FirstBank NovaPay Digital Factory - Concurrency-Safe Financial Wallet Ledger Backend Service.",
        Contact = new OpenApiContact
        {
            Name = "FirstBank Digital Factory - NovaPay Engineering",
            Url = new Uri("https://www.firstbanknigeria.com")
        }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using Bearer scheme. Enter: 'Bearer {token}'.\n" +
                      "Generate a test token via POST /api/auth/token.",
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

// HTTP Middleware pipeline
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// Enable Swagger in all environments (Development & Production)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "NovaWallet Ledger Service v1");
    c.RoutePrefix = "swagger";
});

// Redirect root URL "/" directly to "/swagger"
app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health checks
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// Database initialization and demo seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await db.Database.EnsureCreatedAsync();

        if (!await db.Wallets.AnyAsync())
        {
            logger.LogInformation("Seeding initial demo wallets...");

            var wallet1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var wallet2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

            var wallet1 = new Wallet(wallet1Id, "CUST-FIRSTBANK-001", KycTier.Tier3, "22233344455", "11122233344");
            wallet1.Credit(10_000_000L); // ₦100,000.00

            var wallet2 = new Wallet(wallet2Id, "CUST-FIRSTBANK-002", KycTier.Tier2, "33344455566", "22233344455");
            wallet2.Credit(5_000_000L);  // ₦50,000.00

            var tx1 = new Transaction(Guid.NewGuid(), wallet1.Id, TransactionType.Credit, 10_000_000L, 10_000_000L, "SEED-NIP-001", null, "Initial Seed Deposit", "NIP");
            var tx2 = new Transaction(Guid.NewGuid(), wallet2.Id, TransactionType.Credit, 5_000_000L, 5_000_000L, "SEED-NIP-002", null, "Initial Seed Deposit", "NIP");

            var audit1 = new AuditLog(Guid.NewGuid(), wallet1.Id, "SEED_CREDIT", 10_000_000L, 0, 10_000_000L, "SEED-NIP-001", "SYSTEM-INIT", "SEEDER");
            var audit2 = new AuditLog(Guid.NewGuid(), wallet2.Id, "SEED_CREDIT", 5_000_000L, 0, 5_000_000L, "SEED-NIP-002", "SYSTEM-INIT", "SEEDER");

            db.Wallets.AddRange(wallet1, wallet2);
            db.Transactions.AddRange(tx1, tx2);
            db.AuditLogs.AddRange(audit1, audit2);

            await db.SaveChangesAsync();
            logger.LogInformation("Demo accounts created: Wallet1={W1} (₦100,000), Wallet2={W2} (₦50,000)", wallet1Id, wallet2Id);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize database");
    }
}

app.Run();

public partial class Program { }
