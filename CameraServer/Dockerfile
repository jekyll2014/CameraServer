# Multi-stage Dockerfile for CameraServer using OpenCvSharp official image
# Using pre-built OpenCvSharp image with OpenCV and .NET support

# Stage 1: Build application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS app-builder

WORKDIR /src

# Copy project files
COPY ["CameraServer/CameraServer.Server.csproj", "CameraServer/"]
COPY ["CameraLib/CameraLib.csproj", "CameraLib/"]
COPY ["BlazorClient/CameraServer.Client.csproj", "BlazorClient/"]
COPY ["Shared/CameraServer.Shared.csproj", "Shared/"]

# Copy NuGet configuration
COPY nuget.config ./

# Set environment variables
ENV NUGET_FALLBACK_PACKAGES=""

# Copy source code
COPY . .

# Clean NuGet caches
RUN rm -rf /root/.nuget /root/.dotnet /root/.config /tmp/NuGetScratch 2>/dev/null || true

# Build all projects
RUN dotnet build "Shared/CameraServer.Shared.csproj" -c Release \
    -p:DisableImplicitNuGetFallbackFolder=true \
    -p:RestoreUseStaticGraphEvaluation=false 2>&1 | tail -5

RUN dotnet build "CameraLib/CameraLib.csproj" -c Release \
    -p:DisableImplicitNuGetFallbackFolder=true \
    -p:RestoreUseStaticGraphEvaluation=false 2>&1 | tail -5

RUN dotnet build "BlazorClient/CameraServer.Client.csproj" -c Release \
    -p:DisableImplicitNuGetFallbackFolder=true \
    -p:RestoreUseStaticGraphEvaluation=false 2>&1 | tail -5

RUN dotnet build "CameraServer/CameraServer.Server.csproj" -c Release \
    -p:DisableImplicitNuGetFallbackFolder=true \
    -p:RestoreUseStaticGraphEvaluation=false 2>&1 | tail -5

# Publish application
RUN dotnet publish "CameraServer/CameraServer.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Final runtime using OpenCvSharp official image
# This image includes OpenCV 4.7.0 and .NET 6 runtime
# We need to add .NET 8 runtime for our app
FROM ghcr.io/shimat/opencvsharp/ubuntu22-dotnet6-opencv4.7.0:20230114 AS runtime

ENV DEBIAN_FRONTEND=noninteractive
WORKDIR /app

# Update system packages
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Install .NET 8 ASP.NET Core runtime alongside .NET 6
# Download the ASP.NET Core runtime tarball (includes base runtime)
RUN mkdir -p /tmp/dotnet8 && \
    cd /tmp/dotnet8 && \
    wget https://dotnetcli.blob.core.windows.net/dotnet/aspnetcore/Runtime/8.0.0/aspnetcore-runtime-8.0.0-linux-x64.tar.gz -q && \
    tar -xzf aspnetcore-runtime-8.0.0-linux-x64.tar.gz -C /usr/share/dotnet && \
    rm -rf /tmp/dotnet8

# Verify both .NET versions are installed
RUN /usr/share/dotnet/dotnet --info | head -20

# Verify OpenCV is available
RUN ldconfig -p | grep -i opencv | head -5 && echo "? OpenCV libraries available"

# Set environment variables
ENV LD_LIBRARY_PATH=/usr/local/lib:/usr/local/lib/x86_64-linux-gnu:$LD_LIBRARY_PATH
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

# Create necessary directories
RUN mkdir -p /app/config \
    && mkdir -p /app/Videos \
    && mkdir -p /app/MotionRecords \
    && mkdir -p /app/logs

# Copy application from builder
COPY --from=app-builder /app/publish/ .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/healthcheck || exit 1

# Expose ports
EXPOSE 80 443

# USER root

# Run application
ENTRYPOINT ["dotnet", "CameraServer.Server.dll"]
