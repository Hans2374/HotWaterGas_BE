using HotWaterGas_BE.Config;
using HotWaterGas_BE.Middleware;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Repos;
using Repos.Models;
using Services;
using Services.DTOs;
using Services.Implementations;
using Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// ── Kestrel Configuration ──────────────────────────────────────────────────────
// Production (Render/Docker): Listen on HTTP only with Render's PORT env var.
// HTTPS is terminated at the platform/load balancer layer before reaching the container.
// Development: Let launchSettings.json or default behavior handle HTTPS.
if (!builder.Environment.IsDevelopment())
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(int.Parse(port));
    });
}

// ── Forwarded Headers Configuration ─────────────────────────────────────────────
// MUST be first to handle scheme/host normalization before any middleware runs
// Critical for OAuth callback when behind proxies or different ports
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Only trust localhost proxy (Kestrel/VS)
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Configuration Loading ───────────────────────────────────────────────────────
// ASP.NET Core Configuration reads environment variables natively.
// Environment variables use __ prefix to override nested JSON config:
//   ConnectionStrings__DefaultConnection
//   Jwt__Key
//   etc.
//
// For Render/Docker: Set environment variables directly in dashboard
// For local dev: Copy .env.example to .env and use a launch profile

// Add environment variables to configuration (ASP.NET Core native support)
builder.Configuration.AddEnvironmentVariables();

// Add environment-specific settings override (e.g., appsettings.Development.json)
var envSpecificSettings = $"appsettings.{builder.Environment.EnvironmentName}.json";
if (File.Exists(Path.Combine(builder.Environment.ContentRootPath, envSpecificSettings)))
{
    builder.Configuration.AddJsonFile(envSpecificSettings, optional: true, reloadOnChange: true);
}

// ── Database Configuration ───────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. " +
        "Set the ConnectionStrings__DefaultConnection environment variable " +
        "or add a DefaultConnection to appsettings.json.");
}

builder.Services.AddDbContext<HotWaterGasDBContext>(options =>
    options.UseNpgsql(connectionString));

// ── CORS Configuration ──────────────────────────────────────────────────────────
// Get allowed origins from configuration (supports environment variables)
var allowedOrigins = builder.Configuration.GetSection("CORS:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "https://localhost:7140", "https://hot-water-gas-fe.vercel.app" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

// ── DataProtection Configuration ──────────────────────────────────────────────────
// Required for OAuth correlation cookies to persist across app restarts
// Without this, correlation cookies are invalidated on each restart, causing
// "oauth state was missing or invalid" errors during OAuth flow
// Keys are stored in the Postgres database so they survive redeploys and instance restarts
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<HotWaterGasDBContext>()
    .SetApplicationName("HotWaterGas");

builder.Services.AddRepos();
builder.Services.AddServs();
builder.Services.AddAuthOptions(builder.Configuration);
builder.Services.AddHttpClient<IEmailService, MailerSendEmailService>(client =>
{
    client.BaseAddress = new Uri("https://api.mailersend.com/v1/");
});
builder.Services.AddJwtAuthentication(builder.Configuration);

// Register Cloudinary configuration — must be before app.Build()
builder.Services.Configure<Services.DTOs.CloudinaryOptions>(
    builder.Configuration.GetSection("Cloudinary"));

// ── Swagger Configuration ────────────────────────────────────────────────────────
// Only enable Swagger in Development environment
var enableSwagger = builder.Configuration.GetValue<bool>("ASPNETCORE_ENABLE_SWAGGER",
    builder.Environment.IsDevelopment());

if (enableSwagger)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new() { Title = "HotWaterGas API", Version = "v1" });

        // JWT Bearer authentication security definition
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

        // Apply security requirement globally to all operations
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

        // Tell Swagger that [FromForm] IFormFile parameters are file uploads
        options.OperationFilter<FormFileOperationFilter>();
    });
}
else
{
    // Still need AddEndpointsApiExplorer for minimalapi fallback, but skip SwaggerGen
    builder.Services.AddEndpointsApiExplorer();
}

// ── Startup validation: MailerSend ─────────────────────────────────────────────
var mailerSendApiToken = builder.Configuration["MailerSend:ApiToken"] ?? string.Empty;
var mailerSendFromEmail = builder.Configuration["MailerSend:FromEmail"] ?? string.Empty;
var mailerSendFromName = builder.Configuration["MailerSend:FromName"] ?? string.Empty;

var missingMailerSendKeys = new List<string>();
if (string.IsNullOrWhiteSpace(mailerSendApiToken)) missingMailerSendKeys.Add("ApiToken");
if (string.IsNullOrWhiteSpace(mailerSendFromEmail)) missingMailerSendKeys.Add("FromEmail");
if (string.IsNullOrWhiteSpace(mailerSendFromName)) missingMailerSendKeys.Add("FromName");

if (missingMailerSendKeys.Count > 0)
{
    throw new InvalidOperationException($"MailerSend configuration missing: {string.Join(", ", missingMailerSendKeys)}");
}

// ── Startup validation: Cloudinary ─────────────────────────────────────────────
var cloudinaryCloudName = builder.Configuration["Cloudinary:CloudName"] ?? string.Empty;
var cloudinaryApiKey = builder.Configuration["Cloudinary:ApiKey"] ?? string.Empty;
var cloudinaryApiSecret = builder.Configuration["Cloudinary:ApiSecret"] ?? string.Empty;

var missingCloudinaryKeys = new List<string>();
if (string.IsNullOrWhiteSpace(cloudinaryCloudName)) missingCloudinaryKeys.Add("CloudName");
if (string.IsNullOrWhiteSpace(cloudinaryApiKey)) missingCloudinaryKeys.Add("ApiKey");
if (string.IsNullOrWhiteSpace(cloudinaryApiSecret)) missingCloudinaryKeys.Add("ApiSecret");

// Warn but do not block startup — image upload endpoint will return 502 when invoked without credentials
if (builder.Environment.IsDevelopment())
{
    if (missingCloudinaryKeys.Count > 0)
    {
        Console.WriteLine($"[Cloudinary.Config] WARNING: Missing keys: {string.Join(", ", missingCloudinaryKeys)}. Image upload will fail until credentials are set.");
    }
    else
    {
        var maskedKey = cloudinaryApiKey.Length > 4
            ? $"{cloudinaryApiKey[..4]}***"
            : "****";
        Console.WriteLine($"[Cloudinary.Config] CloudName={cloudinaryCloudName} ApiKeyPrefix={maskedKey}");
    }
}

// ── Build app (freezes service collection) ─────────────────────────────────────
var app = builder.Build();

// ── Hosting Diagnostics ────────────────────────────────────────────────────────
var isDev = builder.Environment.IsDevelopment();
var listeningPort = isDev ? "7140 (VS HTTPS)" : (Environment.GetEnvironmentVariable("PORT") ?? "8080");
var httpsHandler = isDev ? "Visual Studio dev certificate" : "Platform/Load Balancer (Render)";

app.Logger.LogInformation("[Hosting] Environment: {Environment}", builder.Environment.EnvironmentName);
app.Logger.LogInformation("[Hosting] Listening on: http://0.0.0.0:{Port}", listeningPort);
app.Logger.LogInformation("[Hosting] HTTPS handled by: {HttpsHandler}", httpsHandler);

// ── Forwarded Headers (MUST be first) ─────────────────────────────────────────
app.UseForwardedHeaders();

// ── HTTPS Redirection ──────────────────────────────────────────────────────────
var forceHttps = builder.Configuration.GetValue<bool>("ASPNETCORE_FORCE_HTTPS_REDIRECTION",
    !builder.Environment.IsDevelopment());

if (forceHttps)
{
    app.UseHttpsRedirection();
}

// ── CORS ───────────────────────────────────────────────────────────────────────
app.UseCors("AllowFrontend");

// ── Authentication & Authorization ──────────────────────────────────────────────
app.UseAuthentication();
app.UseAuthorization();

// ── OAuth Correlation Diagnostics (temporary - for debugging only) ───────────────
app.UseOAuthCorrelationDiagnostics();

// ── Exception Handling (MUST be after auth pipeline) ───────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();

// ── Swagger UI (after all other middleware - route-based) ───────────────────────
if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "HotWaterGas API v1");
        options.RoutePrefix = "swagger";
    });
}

// ── Health Check Endpoint (for Docker/Render) ──────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0"
}));

app.MapControllers();

app.Run();
