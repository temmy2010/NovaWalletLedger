using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovaWallet.Api.Controllers;
using NovaWallet.Application.DTOs;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Open in-memory SQLite connection for lifetime of factory
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Close();
        _connection?.Dispose();
    }
}

public class ApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public ApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> GetJwtTokenAsync(string customerId = "CUST-TEST-001")
    {
        var response = await _client.PostAsJsonAsync("/api/auth/token", new TokenRequest(customerId, "Customer"));
        response.EnsureSuccessStatusCode();
        var tokenObj = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return tokenObj!.AccessToken;
    }

    [Fact]
    public async Task HealthEndpoints_ShouldReturnHealthy()
    {
        var liveResponse = await _client.GetAsync("/health/live");
        var readyResponse = await _client.GetAsync("/health/ready");

        liveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoints_WithoutAuth_ShouldReturn401Unauthorized()
    {
        var unauthenticatedClient = _factory.CreateClient();
        var response = await unauthenticatedClient.GetAsync($"/api/wallets/{Guid.NewGuid()}/balance");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FullLedgerFlow_EndToEnd_ShouldSucceedWithInvariantsMaintained()
    {
        // 1. Get Auth Token
        var token = await GetJwtTokenAsync("CUST-E2E-001");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 2. Create Wallet A
        var createW1 = await _client.PostAsJsonAsync("/api/wallets", new CreateWalletRequest("CUST-E2E-001", Bvn: "12345678901"));
        createW1.StatusCode.Should().Be(HttpStatusCode.Created);
        var w1 = await createW1.Content.ReadFromJsonAsync<WalletDto>();
        w1.Should().NotBeNull();
        w1!.BalanceKobo.Should().Be(0L);

        // 3. Create Wallet B
        var createW2 = await _client.PostAsJsonAsync("/api/wallets", new CreateWalletRequest("CUST-E2E-002", Bvn: "98765432109"));
        createW2.StatusCode.Should().Be(HttpStatusCode.Created);
        var w2 = await createW2.Content.ReadFromJsonAsync<WalletDto>();
        w2.Should().NotBeNull();

        // 4. Inbound Credit to Wallet A (₦20,000 / 2,000,000 kobo)
        var creditResponse = await _client.PostAsJsonAsync($"/api/wallets/{w1.Id}/credit", new CreditWalletRequest(2_000_000L, "NIP-DEP-001", "011", "SESSION-123", "Salary Deposit"));
        creditResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Check Balance of Wallet A
        var balResponse = await _client.GetFromJsonAsync<BalanceResponse>($"/api/wallets/{w1.Id}/balance");
        balResponse!.BalanceKobo.Should().Be(2_000_000L);
        balResponse.FormattedNaira.Should().Be(20_000.00m);

        // 6. Transfer ₦5,000 (500,000 kobo) from Wallet A to Wallet B with Idempotency-Key
        var transferMsg = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(new TransferRequest(w1.Id, w2!.Id, 500_000L, "P2P-TRF-001", "Lunch payment"))
        };
        transferMsg.Headers.Add("Idempotency-Key", "idempotency-e2e-1001");
        transferMsg.Headers.Add("X-Correlation-Id", "trace-corr-1001");

        var transferResponse = await _client.SendAsync(transferMsg);
        transferResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var transferResult = await transferResponse.Content.ReadFromJsonAsync<TransferResponse>();
        transferResult!.SourceBalanceAfterKobo.Should().Be(1_500_000L);

        // 7. Verify Replay of Same Idempotency-Key
        var replayMsg = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(new TransferRequest(w1.Id, w2.Id, 500_000L, "P2P-TRF-001", "Lunch payment"))
        };
        replayMsg.Headers.Add("Idempotency-Key", "idempotency-e2e-1001");

        var replayResponse = await _client.SendAsync(replayMsg);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayResult = await replayResponse.Content.ReadFromJsonAsync<TransferResponse>();
        replayResult!.TransactionId.Should().Be(transferResult.TransactionId);

        // Balance should still be 1,500,000 kobo
        var checkBalAfterReplay = await _client.GetFromJsonAsync<BalanceResponse>($"/api/wallets/{w1.Id}/balance");
        checkBalAfterReplay!.BalanceKobo.Should().Be(1_500_000L);

        // 8. Query Paginated Statement (Newest First)
        var statementResponse = await _client.GetFromJsonAsync<StatementResponse>($"/api/wallets/{w1.Id}/statement?pageNumber=1&pageSize=10");
        statementResponse!.TotalCount.Should().Be(2); // 1 Credit, 1 TransferOut
        statementResponse.Items[0].Type.Should().Be(Domain.Enums.TransactionType.TransferOut, "Newest transaction must be first");
        statementResponse.Items[1].Type.Should().Be(Domain.Enums.TransactionType.Credit);

        // 9. Query Audit Trail
        var auditResponse = await _client.GetFromJsonAsync<List<AuditLogDto>>($"/api/wallets/{w1.Id}/audit-logs");
        auditResponse!.Should().HaveCount(2);
        auditResponse[0].Operation.Should().Be("TRANSFER_OUT");
        auditResponse[1].Operation.Should().Be("CREDIT");
    }
}
