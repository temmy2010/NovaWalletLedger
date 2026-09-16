namespace NovaWallet.Tests.Integration;

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
using NovaWallet.Domain.Enums;
using NovaWallet.Infrastructure.Persistence;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ServiceDescriptor? descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            ServiceProvider sp = services.BuildServiceProvider();
            using IServiceScope scope = sp.CreateScope();
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
        var request = new TokenRequest { CustomerId = customerId, Role = "Customer" };
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/token", request);
        response.EnsureSuccessStatusCode();

        TokenResponse? tokenObj = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return tokenObj!.AccessToken;
    }

    [Fact]
    public async Task HealthEndpoints_ShouldReturnHealthy()
    {
        HttpResponseMessage liveResponse = await _client.GetAsync("/health/live");
        HttpResponseMessage readyResponse = await _client.GetAsync("/health/ready");

        liveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoints_WithoutAuth_ShouldReturn401Unauthorized()
    {
        HttpClient unauthenticatedClient = _factory.CreateClient();
        HttpResponseMessage response = await unauthenticatedClient.GetAsync($"/api/wallets/{Guid.NewGuid()}/balance");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FullLedgerFlow_EndToEnd_ShouldSucceedWithInvariantsMaintained()
    {
        // 1. Get Auth Token
        string token = await GetJwtTokenAsync("CUST-E2E-001");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 2. Create Wallet A
        var createRequest1 = new CreateWalletRequest { CustomerId = "CUST-E2E-001", Bvn = "12345678901" };
        HttpResponseMessage createW1 = await _client.PostAsJsonAsync("/api/wallets", createRequest1);
        createW1.StatusCode.Should().Be(HttpStatusCode.Created);
        WalletDto? w1 = await createW1.Content.ReadFromJsonAsync<WalletDto>();
        w1.Should().NotBeNull();
        w1!.BalanceKobo.Should().Be(0L);

        // 3. Create Wallet B
        var createRequest2 = new CreateWalletRequest { CustomerId = "CUST-E2E-002", Bvn = "98765432109" };
        HttpResponseMessage createW2 = await _client.PostAsJsonAsync("/api/wallets", createRequest2);
        createW2.StatusCode.Should().Be(HttpStatusCode.Created);
        WalletDto? w2 = await createW2.Content.ReadFromJsonAsync<WalletDto>();
        w2.Should().NotBeNull();

        // 4. Inbound Credit to Wallet A (₦20,000 / 2,000,000 kobo)
        var creditRequest = new CreditWalletRequest
        {
            AmountKobo = 2_000_000L,
            Reference = "NIP-DEP-001",
            CounterpartyBankCode = "011",
            SessionId = "SESSION-123",
            Description = "Salary Deposit"
        };
        HttpResponseMessage creditResponse = await _client.PostAsJsonAsync($"/api/wallets/{w1.Id}/credit", creditRequest);
        creditResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Check Balance of Wallet A
        BalanceResponse? balResponse = await _client.GetFromJsonAsync<BalanceResponse>($"/api/wallets/{w1.Id}/balance");
        balResponse!.BalanceKobo.Should().Be(2_000_000L);
        balResponse.FormattedNaira.Should().Be(20_000.00m);

        // 6. Transfer ₦5,000 (500,000 kobo) from Wallet A to Wallet B with Idempotency-Key
        var transferPayload = new TransferRequest
        {
            SourceWalletId = w1.Id,
            DestinationWalletId = w2!.Id,
            AmountKobo = 500_000L,
            Reference = "P2P-TRF-001",
            Description = "Lunch payment"
        };

        var transferMsg = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(transferPayload)
        };
        transferMsg.Headers.Add("Idempotency-Key", "idempotency-e2e-1001");
        transferMsg.Headers.Add("X-Correlation-Id", "trace-corr-1001");

        HttpResponseMessage transferResponse = await _client.SendAsync(transferMsg);
        transferResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        TransferResponse? transferResult = await transferResponse.Content.ReadFromJsonAsync<TransferResponse>();
        transferResult!.SourceBalanceAfterKobo.Should().Be(1_500_000L);

        // 7. Verify Replay of Same Idempotency-Key
        var replayMsg = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(transferPayload)
        };
        replayMsg.Headers.Add("Idempotency-Key", "idempotency-e2e-1001");

        HttpResponseMessage replayResponse = await _client.SendAsync(replayMsg);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        TransferResponse? replayResult = await replayResponse.Content.ReadFromJsonAsync<TransferResponse>();
        replayResult!.TransactionId.Should().Be(transferResult.TransactionId);

        // Balance must remain exactly 1,500,000 kobo
        BalanceResponse? checkBalAfterReplay = await _client.GetFromJsonAsync<BalanceResponse>($"/api/wallets/{w1.Id}/balance");
        checkBalAfterReplay!.BalanceKobo.Should().Be(1_500_000L);

        // 8. Query Paginated Statement (Newest First)
        StatementResponse? statementResponse = await _client.GetFromJsonAsync<StatementResponse>($"/api/wallets/{w1.Id}/statement?pageNumber=1&pageSize=10");
        statementResponse!.TotalCount.Should().Be(2);
        statementResponse.Items[0].Type.Should().Be(TransactionType.TransferOut, "Newest transaction must be first");
        statementResponse.Items[1].Type.Should().Be(TransactionType.Credit);

        // 9. Query Audit Trail
        List<AuditLogDto>? auditResponse = await _client.GetFromJsonAsync<List<AuditLogDto>>($"/api/wallets/{w1.Id}/audit-logs");
        auditResponse!.Should().HaveCount(2);
        auditResponse[0].Operation.Should().Be("TRANSFER_OUT");
        auditResponse[1].Operation.Should().Be("CREDIT");
    }
}
