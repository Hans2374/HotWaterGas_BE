# =============================================================================
# HotWaterGas Backend - Production Dockerfile for Render Deployment
# =============================================================================
# Project Analysis:
# - .NET SDK: 8.0
# - ASP.NET Core: 8.0.11
# - Project: HotWaterGas_BE.csproj (Web API)
# - Output DLL: HotWaterGas_BE.dll
# - Dependencies: EF Core 8.0.5, SQL Server, JWT, Swagger, PayOS, Cloudinary
# =============================================================================

# ── Stage 1: Build ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Install dependencies first for better layer caching
COPY ["HotWaterGas_BE/HotWaterGas_BE.csproj", "HotWaterGas_BE/"]
COPY ["Repos/Repos.csproj", "Repos/"]
COPY ["Services/Services.csproj", "Services/"]

# Restore dependencies (cached layer)
RUN dotnet restore "HotWaterGas_BE/HotWaterGas_BE.csproj"

# Copy source code
COPY . .

# Build and publish Release
WORKDIR "/src/HotWaterGas_BE"
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r appgroup && useradd -r -g appgroup appuser

# Install curl for health checks (optional)
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Copy published output
COPY --from=build /app/publish .

# Set ownership
RUN chown -R appuser:appgroup /app

# Switch to non-root user
USER appuser

# ── Expose port (Render uses PORT env var) ───────────────────────────────────
EXPOSE 8080

# ── Health check ──────────────────────────────────────────────────────────────
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# ── Environment defaults ───────────────────────────────────────────────────────
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# ── Entry point ───────────────────────────────────────────────────────────────
ENTRYPOINT ["dotnet", "HotWaterGas_BE.dll"]
