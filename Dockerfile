# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy solution and csproj files for layer caching
COPY NovaWalletLedger.sln .
COPY src/NovaWallet.Domain/NovaWallet.Domain.csproj src/NovaWallet.Domain/
COPY src/NovaWallet.Application/NovaWallet.Application.csproj src/NovaWallet.Application/
COPY src/NovaWallet.Infrastructure/NovaWallet.Infrastructure.csproj src/NovaWallet.Infrastructure/
COPY src/NovaWallet.Api/NovaWallet.Api.csproj src/NovaWallet.Api/
COPY tests/NovaWallet.Tests/NovaWallet.Tests.csproj tests/NovaWallet.Tests/

RUN dotnet restore

# Copy all source files and publish
COPY . .
WORKDIR /app/src/NovaWallet.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Copy published application
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "NovaWallet.Api.dll"]
