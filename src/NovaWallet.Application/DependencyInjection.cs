using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Application.Interfaces;
using NovaWallet.Application.Services;
using NovaWallet.Application.Validators;

namespace NovaWallet.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateWalletRequestValidator>();

        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<ITransferService, TransferService>();
        services.AddScoped<IStatementService, StatementService>();
        services.AddScoped<IAuditService, AuditService>();

        return services;
    }
}
