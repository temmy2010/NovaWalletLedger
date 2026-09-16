using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.Tests.Helpers;

public static class TestDbContextFactory
{
    public static ApplicationDbContext CreateInMemoryDbContext()
    {
        // Using SQLite In-Memory with an open connection to properly enforce relational invariants & constraints
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging()
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }
}
