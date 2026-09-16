using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Infrastructure.Auth;
using NovaWallet.Infrastructure.Outbox;
using NovaWallet.Infrastructure.Persistence;
using NovaWallet.Infrastructure.Time;

namespace NovaWallet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Database Configuration (PostgreSQL / SQLite flexible provider)
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
                               ?? "Data Source=novawallet.db";

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
                connectionString.Contains("Data Source=(localdb)", StringComparison.OrdinalIgnoreCase) ||
                connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase) ||
                connectionString.Contains("Trusted_Connection=", StringComparison.OrdinalIgnoreCase))
            {
                // Microsoft SQL Server (LocalDB, SQL Express, or Enterprise SQL Server)
                options.UseSqlServer(connectionString, sqlServerOptions =>
                {
                    sqlServerOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    sqlServerOptions.EnableRetryOnFailure(maxRetryCount: 3);
                });
            }
            else if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
                     connectionString.Contains("Port=5432", StringComparison.OrdinalIgnoreCase) ||
                     connectionString.Contains("Username=", StringComparison.OrdinalIgnoreCase))
            {
                // PostgreSQL
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                });
            }
            else
            {
                // SQLite (Default frictionless local database file)
                options.UseSqlite(connectionString, sqliteOptions =>
                {
                    sqliteOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                });
            }
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // 2. Services
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // 3. Background Outbox Processor (Stretch Goal)
        services.AddHostedService<OutboxProcessorHostedService>();

        // 4. JWT Authentication
        var secretKey = configuration["Jwt:SecretKey"] ?? "NovaWalletSecretKeyMustBeAtLeast32BytesLong!";
        var issuer = configuration["Jwt:Issuer"] ?? "NovaWallet.LedgerService";
        var audience = configuration["Jwt:Audience"] ?? "NovaWallet.Api";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddAuthorization();

        return services;
    }
}
