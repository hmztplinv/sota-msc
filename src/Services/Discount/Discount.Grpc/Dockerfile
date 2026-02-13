FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# csproj kopyala ve restore
COPY src/Services/Discount/Discount.Grpc/Discount.Grpc.csproj Services/Discount/Discount.Grpc/
RUN dotnet restore Services/Discount/Discount.Grpc/Discount.Grpc.csproj

# Source kopyala ve publish
COPY src/Services/Discount/ Services/Discount/
RUN dotnet publish Services/Discount/Discount.Grpc/Discount.Grpc.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Discount.Grpc.dll"]
