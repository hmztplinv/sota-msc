# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1) Solution seviyesinde .csproj'ları kopyala — layer caching için
COPY src/BuildingBlocks/BuildingBlocks.Results/BuildingBlocks.Results.csproj BuildingBlocks/BuildingBlocks.Results/
COPY src/BuildingBlocks/BuildingBlocks.CQRS/BuildingBlocks.CQRS.csproj BuildingBlocks/BuildingBlocks.CQRS/
COPY src/Services/Basket/Basket.API/Basket.API.csproj Services/Basket/Basket.API/

# 2) Restore — NuGet paketleri cache'lenir
RUN dotnet restore Services/Basket/Basket.API/Basket.API.csproj

# 3) Tüm kaynak kodu kopyala
COPY src/BuildingBlocks/ BuildingBlocks/
COPY src/Services/Basket/ Services/Basket/

# 4) Publish — Release mode
RUN dotnet publish Services/Basket/Basket.API/Basket.API.csproj \
    -c Release \
    -o /app/publish

# ============================================
# Stage 2: Runtime
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl — health check için gerekli
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Non-root user — güvenlik best practice
USER $APP_UID

COPY --from=build /app/publish .

# Container internal port
EXPOSE 8080

ENTRYPOINT ["dotnet", "Basket.API.dll"]