using HotWaterGas_BE.Config;
using HotWaterGas_BE.Middleware;
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

// Add services to the container.

builder.Services.AddDbContext<HotWaterGasDBContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
    }

    options.UseSqlServer(connectionString);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173",
                          "http://localhost:3000",
                          "http://localhost:5140",
                          "https://localhost:7268")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
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

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
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

// ── Startup validation: Cloudinary ───────────────────────────────────────────
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

// Post-build logging — service provider is now available via app.Services
if (!missingCloudinaryKeys.Any())
{
    var maskedKey = cloudinaryApiKey.Length > 4
        ? $"{cloudinaryApiKey[..4]}***"
        : "****";
    app.Logger.LogInformation(
        "[Cloudinary.Config] CloudName={CloudName} ApiKeyPrefix={ApiKeyPrefix}",
        cloudinaryCloudName, maskedKey);
}
else
{
    app.Logger.LogWarning(
        "[Cloudinary.Config] Missing keys: {MissingKeys}. Image upload will fail until credentials are set.",
        string.Join(", ", missingCloudinaryKeys));
}

var maskedToken = mailerSendApiToken.Length <= 8
    ? new string('*', mailerSendApiToken.Length)
    : $"{mailerSendApiToken[..5]}***{mailerSendApiToken[^3..]}";
app.Logger.LogInformation(
    "[MailerSend.Config] ApiTokenExists=true ApiTokenPreview={TokenPreview} FromEmail={FromEmail} FromName={FromName}",
    maskedToken, mailerSendFromEmail, mailerSendFromName);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

// CORS must be before UseHttpsRedirection for preflight requests to work
app.UseCors("AllowFrontend");

// Don't force HTTPS redirect in development to avoid mixed-protocol issues
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
