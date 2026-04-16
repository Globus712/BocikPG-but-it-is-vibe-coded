# ─── Build stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (better layer caching)
COPY BocikPG.csproj ./
RUN dotnet restore

# Copy the rest of the source and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ─── Runtime stage ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# LibGit2Sharp needs the native libgit2 library at runtime
RUN apt-get update && apt-get install -y --no-install-recommends \
        git \
        libgit2-1.7 \   
        libsodium-dev \ 
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# The Resources directory is bind-mounted at runtime (see docker-compose.yml),
# so we only create an empty placeholder here.
RUN mkdir -p Resources

# Copy and set up entrypoint script
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
