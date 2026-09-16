using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Api.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var correlationId = context.Items.TryGetValue("X-Correlation-Id", out var cid) ? cid?.ToString() : traceId;

        int statusCode;
        string title;
        string detail;
        string typeUrl;

        switch (exception)
        {
            case WalletNotFoundException wnfe:
                statusCode = (int)HttpStatusCode.NotFound;
                title = "Wallet Not Found";
                detail = wnfe.Message;
                typeUrl = "https://developer.firstbank.ng/errors/wallet-not-found";
                _logger.LogWarning(wnfe, "Wallet not found: {Message}", wnfe.Message);
                break;

            case InsufficientFundsException ife:
                statusCode = (int)HttpStatusCode.UnprocessableEntity;
                title = "Insufficient Funds";
                detail = ife.Message;
                typeUrl = "https://developer.firstbank.ng/errors/insufficient-funds";
                _logger.LogWarning(ife, "Insufficient funds: {Message}", ife.Message);
                break;

            case DailyLimitExceededException dlee:
                statusCode = (int)HttpStatusCode.UnprocessableEntity;
                title = "Daily Transfer Limit Exceeded";
                detail = dlee.Message;
                typeUrl = "https://developer.firstbank.ng/errors/daily-limit-exceeded";
                _logger.LogWarning(dlee, "Daily limit exceeded: {Message}", dlee.Message);
                break;

            case IdempotencyConflictException ice:
                statusCode = (int)HttpStatusCode.UnprocessableEntity;
                title = "Idempotency Conflict";
                detail = ice.Message;
                typeUrl = "https://developer.firstbank.ng/errors/idempotency-conflict";
                _logger.LogWarning(ice, "Idempotency conflict: {Message}", ice.Message);
                break;

            case InvalidAmountException iae:
                statusCode = (int)HttpStatusCode.BadRequest;
                title = "Invalid Amount";
                detail = iae.Message;
                typeUrl = "https://developer.firstbank.ng/errors/invalid-amount";
                _logger.LogWarning(iae, "Invalid amount: {Message}", iae.Message);
                break;

            case SameWalletTransferException swte:
                statusCode = (int)HttpStatusCode.BadRequest;
                title = "Invalid Transfer Destination";
                detail = swte.Message;
                typeUrl = "https://developer.firstbank.ng/errors/same-wallet-transfer";
                _logger.LogWarning(swte, "Same wallet transfer: {Message}", swte.Message);
                break;

            case WalletInactiveException wie:
                statusCode = (int)HttpStatusCode.Forbidden;
                title = "Wallet Inactive";
                detail = wie.Message;
                typeUrl = "https://developer.firstbank.ng/errors/wallet-inactive";
                _logger.LogWarning(wie, "Inactive wallet: {Message}", wie.Message);
                break;

            case ValidationException ve:
                statusCode = (int)HttpStatusCode.BadRequest;
                title = "Validation Failed";
                var errors = ve.Errors.Select(e => e.ErrorMessage).ToList();
                detail = string.Join("; ", errors);
                typeUrl = "https://developer.firstbank.ng/errors/validation-failed";
                _logger.LogWarning(ve, "Validation error: {Detail}", detail);
                break;

            case ArgumentException ae:
                statusCode = (int)HttpStatusCode.BadRequest;
                title = "Invalid Request Argument";
                detail = ae.Message;
                typeUrl = "https://developer.firstbank.ng/errors/bad-request";
                _logger.LogWarning(ae, "Argument error: {Message}", ae.Message);
                break;

            default:
                statusCode = (int)HttpStatusCode.InternalServerError;
                title = "An Unexpected Error Occurred";
                detail = "An internal server error occurred while processing your financial transaction. Please contact support.";
                typeUrl = "https://developer.firstbank.ng/errors/internal-error";
                _logger.LogError(exception, "Unhandled exception occurred during request execution.");
                break;
        }

        var problemDetails = new ProblemDetails
        {
            Type = typeUrl,
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["traceId"] = traceId;
        if (!string.IsNullOrEmpty(correlationId))
        {
            problemDetails.Extensions["correlationId"] = correlationId;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        await context.Response.WriteAsync(json);
    }
}
