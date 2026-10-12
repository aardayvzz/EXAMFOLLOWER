using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using LgsTakip.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace LgsTakip.Api.IntegrationTests;

public sealed class SqlServerWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string DatabaseNamePrefix = "LgsTakip_IntegrationTests_";
    public const string ExpectedIssuer = "LgsTakip.Api.IntegrationTests";
    public const string ExpectedAudience = "LgsTakip.Api.IntegrationTests.Client";

    private const string ExpectedDataSource = @"(localdb)\MSSQLLocalDB";

    private readonly string _signingKey = Convert.ToBase64String(
        RandomNumberGenerator.GetBytes(48));
    private readonly string _collectorApiKey = Convert.ToBase64String(
        RandomNumberGenerator.GetBytes(48));
    private readonly string _connectionString;
    private readonly TimeProvider? _timeProvider;
    private readonly ConcurrentQueue<string>? _logMessages;
    private bool _databaseMayExist;

    public SqlServerWebApplicationFactory(
        TimeProvider? timeProvider = null,
        ConcurrentQueue<string>? logMessages = null)
    {
        _timeProvider = timeProvider;
        _logMessages = logMessages;
        DatabaseName = $"{DatabaseNamePrefix}{Guid.NewGuid():N}";
        _connectionString = new SqlConnectionStringBuilder
        {
            DataSource = ExpectedDataSource,
            InitialCatalog = DatabaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true
        }.ConnectionString;
    }

    public string DatabaseName { get; }
    public string CollectorApiKey => _collectorApiKey;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = ExpectedIssuer,
                ["Jwt:Audience"] = ExpectedAudience,
                ["Jwt:SigningKey"] = _signingKey,
                ["Jwt:AccessTokenExpirationMinutes"] = "60",
                ["CollectorAuthentication:ApiKeyHeaderName"] =
                    "X-Collector-Api-Key",
                ["CollectorAuthentication:ApiKey"] = _collectorApiKey,
                ["Logging:LogLevel:Default"] = "Warning"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LgsTakipDbContext>();
            services.RemoveAll<DbContextOptions<LgsTakipDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<LgsTakipDbContext>>();

            services.AddDbContext<LgsTakipDbContext>(options =>
                options.UseSqlServer(_connectionString));

            if (_timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_timeProvider);
            }
        });

        if (_logMessages is not null)
        {
            builder.ConfigureLogging(logging =>
                logging.AddProvider(new CollectingLoggerProvider(_logMessages)));
        }
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LgsTakipDbContext>();

        ValidateTestDatabaseTarget(dbContext.Database.GetConnectionString());
        _databaseMayExist = true;

        await dbContext.Database.MigrateAsync();
    }

    public TokenValidationParameters CreateTokenValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = ExpectedIssuer,
            ValidateAudience = true,
            ValidAudience = ExpectedAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_signingKey)),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(5),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (_databaseMayExist)
            {
                await using var scope = Services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<LgsTakipDbContext>();

                ValidateTestDatabaseTarget(
                    dbContext.Database.GetConnectionString());

                await dbContext.Database.EnsureDeletedAsync();
            }
        }
        finally
        {
            await base.DisposeAsync();
        }
    }

    private static void ValidateTestDatabaseTarget(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Test database connection string is missing.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;

        if (!string.Equals(
                builder.DataSource,
                ExpectedDataSource,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Test database cleanup refused: data source is not the approved LocalDB instance.");
        }

        if (string.IsNullOrWhiteSpace(databaseName)
            || !databaseName.StartsWith(
                DatabaseNamePrefix,
                StringComparison.Ordinal)
            || databaseName.Length == DatabaseNamePrefix.Length)
        {
            throw new InvalidOperationException(
                "Test database cleanup refused: database name does not have the required unique prefix.");
        }
    }
}
