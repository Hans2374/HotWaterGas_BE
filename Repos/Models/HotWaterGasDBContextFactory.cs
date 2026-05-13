using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Repos.Models;

/// <summary>
/// Design-time factory for HotWaterGasDBContext.
/// Enables EF Core CLI tools (migrations, database update, etc.) to work
/// without requiring the full application to be running.
///
/// Supports configuration from:
/// - appsettings.json
/// - appsettings.{Environment}.json
/// - Environment variables (ConnectionStrings__DefaultConnection)
/// - --connection command-line argument
/// </summary>
public class HotWaterGasDBContextFactory : IDesignTimeDbContextFactory<HotWaterGasDBContext>
{
    public HotWaterGasDBContext CreateDbContext(string[] args)
    {
        return CreateDbContext(args, FindConnectionString());
    }

    private HotWaterGasDBContext CreateDbContext(string[] args, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "No connection string found. Set the ConnectionStrings__DefaultConnection environment variable, " +
                "add a DefaultConnection to appsettings.json, or use the --connection argument.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<HotWaterGasDBContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new HotWaterGasDBContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Finds the connection string using configuration precedence:
    /// 1. --connection command-line argument
    /// 2. ConnectionStrings__DefaultConnection environment variable
    /// 3. appsettings.{Environment}.json
    /// 4. appsettings.json
    /// </summary>
    private static string? FindConnectionString()
    {
        // 1. Check for --connection argument
        var connectionArg = GetCommandLineArg("connection");
        if (!string.IsNullOrWhiteSpace(connectionArg))
        {
            Console.WriteLine("[DesignTimeFactory] Using connection string from --connection argument.");
            return connectionArg;
        }

        // 2. Check for environment variable
        var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(envConnectionString))
        {
            Console.WriteLine("[DesignTimeFactory] Using connection string from ConnectionStrings__DefaultConnection environment variable.");
            return envConnectionString;
        }

        // 3. Try to load from appsettings files
        var startupProjectPath = FindStartupProjectPath();
        if (string.IsNullOrWhiteSpace(startupProjectPath))
        {
            Console.WriteLine("[DesignTimeFactory] Warning: Could not locate startup project path. Falling back to appsettings.json in current directory.");
            startupProjectPath = Directory.GetCurrentDirectory();
        }

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(startupProjectPath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var connectionStringFromConfig = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionStringFromConfig))
        {
            Console.WriteLine($"[DesignTimeFactory] Using connection string from appsettings (Environment: {environment}).");
            return connectionStringFromConfig;
        }

        return null;
    }

    /// <summary>
    /// Locates the startup project path by searching upward from the current directory.
    /// </summary>
    private static string? FindStartupProjectPath()
    {
        // The migrations project (Repos) is typically two levels up from the startup project (HotWaterGas_BE)
        // Current: Repos/Models/ (where this factory lives)
        // Target:  HotWaterGas_BE/

        var currentDir = Directory.GetCurrentDirectory();
        var repoProjectPath = Path.Combine(currentDir, "Repos.csproj");

        // If we're running from Repos project directory, go up two levels
        if (File.Exists(repoProjectPath))
        {
            var parentDir = Directory.GetParent(currentDir)?.FullName;
            if (parentDir != null)
            {
                var grandParentDir = Directory.GetParent(parentDir)?.FullName;
                if (grandParentDir != null)
                {
                    var startupProjectPath = Path.Combine(grandParentDir, "HotWaterGas_BE");
                    if (Directory.Exists(startupProjectPath))
                    {
                        return startupProjectPath;
                    }
                }
            }
        }

        // If we're running from HotWaterGas_BE directory
        var apiProjectPath = Path.Combine(currentDir, "HotWaterGas_BE.csproj");
        if (File.Exists(apiProjectPath))
        {
            return currentDir;
        }

        // Search upward for HotWaterGas_BE directory
        var dir = currentDir;
        for (int i = 0; i < 5; i++)
        {
            var hotWaterGasDir = Path.Combine(dir, "HotWaterGas_BE");
            if (Directory.Exists(hotWaterGasDir))
            {
                return hotWaterGasDir;
            }
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        // Fallback: check if appsettings.json exists in current directory
        if (File.Exists(Path.Combine(currentDir, "appsettings.json")))
        {
            return currentDir;
        }

        return null;
    }

    /// <summary>
    /// Extracts a command-line argument value by name.
    /// </summary>
    private static string? GetCommandLineArg(string name)
    {
        for (int i = 0; i < Environment.GetCommandLineArgs().Length - 1; i++)
        {
            var arg = Environment.GetCommandLineArgs()[i];
            if (arg.StartsWith($"--{name}=", StringComparison.OrdinalIgnoreCase))
            {
                return arg.Substring(arg.IndexOf('=') + 1);
            }
        }
        return null;
    }
}
