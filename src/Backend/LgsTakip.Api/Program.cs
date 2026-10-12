using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Text;
using System.Threading.RateLimiting;
using LgsTakip.Api.Application.Authentication.Interfaces;
using LgsTakip.Api.Application.Authentication.Services;
using LgsTakip.Api.Application.ExamResults.Interfaces;
using LgsTakip.Api.Application.ExamResults.Services;
using LgsTakip.Api.Application.ParentAnalytics.Interfaces;
using LgsTakip.Api.Application.ParentAnalytics.Services;
using LgsTakip.Api.Application.ParentLinking.Interfaces;
using LgsTakip.Api.Application.ParentLinking.Services;
using LgsTakip.Api.Application.RecurringLessons.Interfaces;
using LgsTakip.Api.Application.RecurringLessons.Services;
using LgsTakip.Api.Application.StudySessions.Interfaces;
using LgsTakip.Api.Application.StudySessions.Services;
using LgsTakip.Api.Application.ZoomLessons.Interfaces;
using LgsTakip.Api.Application.ZoomLessons.Services;
using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Domain.Enums;
using LgsTakip.Api.Infrastructure.Authentication;
using LgsTakip.Api.Infrastructure.CollectorAuthentication;
using LgsTakip.Api.Infrastructure.ParentLinking;
using LgsTakip.Api.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<LgsTakipDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddProblemDetails();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(allowIntegerValues: false)));

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var collectorSection = builder.Configuration.GetSection(
    CollectorAuthenticationOptions.SectionName);

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(jwtSection)
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Issuer),
        "JWT issuer must be configured at 'Jwt:Issuer'.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Audience),
        "JWT audience must be configured at 'Jwt:Audience'.")
    .Validate(
        options => options.AccessTokenExpirationMinutes > 0,
        "JWT access token expiration must be greater than zero minutes.")
    .Validate(
        options => !string.IsNullOrEmpty(options.SigningKey)
            && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
        "JWT signing key must be configured at 'Jwt:SigningKey' and contain at least 32 bytes.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer()
    .AddScheme<
        CollectorAuthenticationOptions,
        CollectorApiKeyAuthenticationHandler>(
            CollectorAuthenticationDefaults.Scheme,
            _ => { });

builder.Services
    .AddOptions<CollectorAuthenticationOptions>(
        CollectorAuthenticationDefaults.Scheme)
    .Bind(collectorSection)
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApiKeyHeaderName),
        "Collector API key header name must be configured at 'CollectorAuthentication:ApiKeyHeaderName'.")
    .Validate(
        options => !string.IsNullOrEmpty(options.ApiKey)
            && Encoding.UTF8.GetByteCount(options.ApiKey) >= 32,
        "Collector API key must be configured at 'CollectorAuthentication:ApiKey' and contain at least 32 bytes.")
    .ValidateOnStart();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptionsAccessor) =>
    {
        var jwtOptions = jwtOptionsAccessor.Value;

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtRegisteredClaimNames.Name,
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("StudentOnly", policy =>
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireRole(UserRole.Student.ToString()));
    options.AddPolicy("ParentOnly", policy =>
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireRole(UserRole.Parent.ToString()));
    options.AddPolicy(
        CollectorAuthenticationDefaults.Policy,
        policy => policy
            .AddAuthenticationSchemes(
                CollectorAuthenticationDefaults.Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(
                CollectorAuthenticationDefaults.ClaimType,
                CollectorAuthenticationDefaults.ClaimValue));
});

builder.Services
    .AddOptions<ParentLinkingOptions>()
    .Bind(builder.Configuration.GetSection(ParentLinkingOptions.SectionName))
    .Validate(
        options => options.InvitationLifetimeMinutes is >= 5 and <= 60,
        "Parent linking invitation lifetime must be between 5 and 60 minutes.")
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("Authentication", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown-client",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));

    options.AddPolicy("ParentLinkInvitationCreate", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetAuthenticatedUserPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));

    options.AddPolicy("ParentLinkRedeem", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetAuthenticatedUserPartition(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));

    options.AddPolicy("CollectorIngestion", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown-collector-client",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
});

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<IStudySessionService, StudySessionService>();
builder.Services.AddScoped<IParentAnalyticsService, ParentAnalyticsService>();
builder.Services.AddScoped<IParentLinkService, ParentLinkService>();
builder.Services.AddScoped<IExamResultService, ExamResultService>();
builder.Services.AddScoped<IZoomLessonService, ZoomLessonService>();
builder.Services.AddScoped<IRecurringLessonService, RecurringLessonService>();

static string GetAuthenticatedUserPartition(HttpContext httpContext)
{
    return httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "unauthenticated-user";
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
