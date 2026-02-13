# Bölüm 3: Catalog.API — Carter Endpoints + Advanced Validation + Scalar (OpenAPI)

> **Tarih:** 2025-02-13  
> **Proje:** sota-eshop-microservices  
> **Platform:** .NET 9, Docker Compose, PostgreSQL + Marten  
> **Önceki Bölüm:** Bölüm 2 — Vertical Slice + CQRS Handlers  
> **Sonraki Bölüm:** Bölüm 4 — Basket.API — Proje İskeleti + Redis

---

## 1. Amaç & Kazanımlar

Bu bölümde Catalog.API'yi **production-grade** bir seviyeye taşıdık. Bölüm 2'de CRUD handler'ları ve temel pipeline behavior'ları yazmıştık. Bu bölümde eksik kalan **savunma katmanlarını**, **API dokümantasyonunu** ve **container'ize edilmiş çalışma ortamını** tamamladık.

### Bu Bölümde Ne Yaptık?

1. ✅ **ExceptionHandlingBehavior** — MediatR pipeline'ında beklenmeyen exception'ları `Result.Failure`'a çevirme
2. ✅ **CustomExceptionHandler** — .NET 9 `IExceptionHandler` ile global exception yakalama + Problem Details (RFC 9457)
3. ✅ **GetProductsByCategory** — Yeni bir feature slice (Vertical Slice örneği)
4. ✅ **Mapster Mapping** — Request DTO → Command otomasyonu
5. ✅ **Scalar** — .NET 9 SOTA OpenAPI documentation (Swashbuckle yerine)
6. ✅ **Dockerfile** — Multi-stage build ile container image oluşturma
7. ✅ **Docker Compose** — Catalog.API + PostgreSQL birlikte çalışması

### Öğrenme Hedefleri

Bu bölümü tamamlayan kişi:

- MediatR pipeline behavior'larını **defense-in-depth** mantığıyla tasarlayabilir
- .NET 9'daki `IExceptionHandler` interface'ini kullanarak Problem Details (RFC 9457) uyumlu hata response'ları döndürebilir
- Yeni bir Vertical Slice feature'ı (query + endpoint) ekleyebilir
- Mapster ile zero-config mapping yapabilir ve `with` expression ile immutable record'larda kısmi güncelleme uygulayabilir
- Scalar ile OpenAPI dokümantasyonu kurabilir
- Multi-stage Dockerfile yazabilir ve Docker Compose ile servis orkestasyonu yapabilir

---

## 2. Kavramlar & Tanımlar

### 2.1 ExceptionHandlingBehavior (İstisna Yakalama Davranışı)

MediatR pipeline'ına eklenen bir `IPipelineBehavior<TRequest, TResponse>`. Handler'dan fırlayan **beklenmeyen** exception'ları yakalar ve `Result.Failure(Error.Unexpected(...))` olarak döndürür. Bu sayede exception'lar flow control için kullanılmaz — tüm hatalar `Result<T>` üzerinden akar.

**Beklenen hata vs Beklenmeyen hata:**

| Tür | Örnek | Yakalayan | Çıktı |
|-----|-------|-----------|-------|
| Beklenen (Validation) | "Name boş olamaz" | `ValidationBehavior` | `Result.Failure(Error.Validation(...))` |
| Beklenen (Business) | "Ürün bulunamadı" | Handler kendi içinde | `Result.Failure(Error.NotFound(...))` |
| Beklenmeyen (Exception) | DB bağlantı kopması | `ExceptionHandlingBehavior` | `Result.Failure(Error.Unexpected(...))` |

### 2.2 IExceptionHandler (.NET 9 Global İstisna Yakalayıcı)

.NET 8+ ile gelen `Microsoft.AspNetCore.Diagnostics.IExceptionHandler` interface'i. Tüm HTTP pipeline'ını kapsar — MediatR dışından gelen exception'ları da yakalar. `ExceptionHandlingBehavior` ile birlikte **defense-in-depth** (derinlemesine savunma) sağlar.

```
HTTP Request
  └─ IExceptionHandler (2. katman — TÜM exception'ları yakalar)
       └─ Carter Endpoint
            └─ MediatR Pipeline
                 └─ LoggingBehavior
                      └─ ValidationBehavior
                           └─ ExceptionHandlingBehavior (1. katman — handler exception'ları)
                                └─ Handler
```

### 2.3 Problem Details (RFC 9457)

API'lerde hata response'larının **standart formatı**. Her client aynı yapıyı bekler:

```json
{
  "type": "https://httpstatuses.com/404",
  "title": "Not Found",
  "status": 404,
  "detail": "Product with id 'abc' was not found.",
  "instance": "/api/products/abc"
}
```

**Alanlar:**

| Alan | Açıklama |
|------|----------|
| `type` | Hata türünü tanımlayan URI |
| `title` | Kısa, okunabilir hata başlığı |
| `status` | HTTP status kodu |
| `detail` | Hatanın detaylı açıklaması |
| `instance` | Hatanın oluştuğu request path'i |

### 2.4 ErrorType.Unexpected (Beklenmeyen Hata Türü)

`ErrorType` enum'una eklenen yeni değer. `Failure` (iş kuralı ihlali) ile `Unexpected` (exception kaynaklı) ayrımını sağlar:

- **Failure** → "Stok yetersiz" — normal iş akışı, beklenen durum
- **Unexpected** → "DB bağlantı hatası" — beklenmeyen durum, alarm gerektirir

Bu ayrım **monitoring** için kritik: Grafana dashboard'da `Unexpected` spike'ı alarm tetikler, `Failure` normal iş akışıdır.

### 2.5 Mapster (Object-Object Mapper)

Nesneler arası mapping (eşleme) kütüphanesi. AutoMapper'a alternatif, **compile-time code generation** ile daha performanslı. Property isimleri eşleştiğinde **zero-config** çalışır:

```csharp
// 5 satır manuel mapping yerine:
var command = request.Adapt<CreateProductCommand>();
```

### 2.6 with Expression (C# Record Kopyalama İfadesi)

Record'ların immutable kopyasını oluştururken belirli property'leri değiştirme özelliği:

```csharp
var command = request.Adapt<UpdateProductCommand>() with { Id = id };
// request'ten tüm alanlar kopyalanır, sadece Id route param ile doldurulur
```

### 2.7 Scalar (OpenAPI Arayüzü)

.NET 9'da Swashbuckle (Swagger) artık varsayılan paket değil. Microsoft desteği bıraktı. **Scalar**, `Microsoft.AspNetCore.OpenApi` paketinin ürettiği OpenAPI spec'ini modern, interaktif bir UI ile gösterir.

### 2.8 Multi-Stage Docker Build (Çok Aşamalı Docker Derlemesi)

Dockerfile'da birden fazla `FROM` kullanarak:

1. **Build stage** — SDK image (~900MB) ile compile
2. **Runtime stage** — Sadece runtime image (~220MB) ile çalıştır

Production'da SDK gereksiz. Bu yaklaşım image boyutunu ~%75 küçültür.

### 2.9 Docker Layer Caching (Docker Katman Önbellekleme)

Dockerfile'da `.csproj` dosyaları kaynak koddan önce kopyalanır. Böylece NuGet restore sadece `.csproj` değiştiğinde tekrar çalışır. Kaynak kod değişikliklerinde cache'den gelir — CI/CD'de büyük zaman tasarrufu.

---

## 3. Neden Böyle? Mimari Gerekçe

### 3.1 Neden İki Katmanlı Exception Handling?

**Alternatif 1: Sadece Pipeline Behavior**
- ✅ Result pattern ile uyumlu
- ❌ Sadece MediatR request'lerini yakalar
- ❌ Carter endpoint kodu, model binding, serialization hataları kaçar

**Alternatif 2: Sadece IExceptionHandler Middleware**
- ✅ Tüm HTTP pipeline'ı kapsar
- ❌ Result pattern dışında kalır — her şey exception-based olur
- ❌ Handler içindeki business logic hataları ile teknik hataları ayırt edemez

**Kararımız: İkisi Birlikte (Defense-in-Depth)**
- ✅ Pipeline behavior: Handler-level exception → `Result.Failure`
- ✅ IExceptionHandler: Pipeline dışı exception → Problem Details
- ✅ Hiçbir exception client'a stack trace olarak sızmaz
- ⚠️ Trade-off: Biraz daha fazla kod, ama güvenlik ve tutarlılık kazanımı buna değer

### 3.2 Neden Pipeline Sırası Önemli?

```
Request → Logging → Validation → ExceptionHandling → Handler
```

| Sıra | Behavior | Neden Bu Sırada? |
|------|----------|-------------------|
| 1 | Logging | En dışta — her şeyi loglar (başarılı/başarısız) |
| 2 | Validation | Geçersiz request'leri handler'a ulaşmadan keser |
| 3 | ExceptionHandling | Handler'dan fırlayan exception'ları yakalar |

Eğer ExceptionHandling, Validation'dan önce olsaydı → Validation exception'ları da "Unexpected" olarak loglanırdı. Yanlış alarm.

### 3.3 Neden Mapster, AutoMapper Değil?

| Kriter | AutoMapper | Mapster |
|--------|------------|---------|
| Performans | Runtime reflection | Compile-time code gen |
| Konfigürasyon | `Profile` class'ları zorunlu | Zero-config (convention) |
| Boyut | Daha büyük dependency | Daha küçük |
| Esneklik | Çok esnek ama karmaşık | Yeterli + basit |

**Karar:** SOTA projemizde basitlik ve performans öncelikli → Mapster.

### 3.4 Neden Scalar, Swagger Değil?

- **Swashbuckle** .NET 9'da varsayılan olmaktan çıktı — Microsoft desteği bıraktı
- **.NET 9** built-in `Microsoft.AspNetCore.OpenApi` ile OpenAPI spec üretir
- **Scalar** bu spec'i modern UI ile gösterir, aktif geliştirme altında
- Swagger UI hâlâ kullanılabilir ama .NET 9 ekosisteminde **Scalar artık SOTA**

### 3.5 Neden Multi-Stage Dockerfile?

| Yaklaşım | Image Boyutu | Güvenlik |
|-----------|-------------|----------|
| Tek stage (SDK) | ~900MB | SDK araçları production'da gereksiz risk |
| Multi-stage | ~220MB | Sadece runtime, minimal attack surface |

**Neden .csproj önce kopyalanıyor?**

```dockerfile
# 1) Önce .csproj'lar — NuGet restore cache'lenir
COPY *.csproj .
RUN dotnet restore

# 2) Sonra kaynak kod — sadece kod değiştiğinde rebuild
COPY . .
RUN dotnet publish
```

Docker her `COPY`/`RUN` komutunu bir **layer** olarak cache'ler. `.csproj` değişmediyse `dotnet restore` cache'den gelir. Bu CI/CD pipeline'larda dakikalar kazandırır.

### 3.6 Marten LINQ — Contains vs Any + ToLower

GetProductsByCategory handler'ında Marten'ın LINQ çevirisi sorunuyla karşılaştık:

```csharp
// ❌ Marten bu LINQ'i PostgreSQL'e düzgün çeviremiyor
.Where(p => p.Categories.Any(c => c.ToLower() == query.Category.ToLower()))

// ✅ Marten-uyumlu — JSONB array @> operatörüne çevrilir
.Where(p => p.Categories.Contains(query.Category))
```

**Neden?** Marten, PostgreSQL JSONB üzerine kurulu. `Contains()` direkt PostgreSQL `@>` operatörüne çevrilir. `Any()` + `ToLower()` gibi karmaşık lambda'lar her zaman doğru SQL'e çevrilemez.

**Trade-off:** Case-sensitive oldu. İleride PostgreSQL `citext` veya normalize edilmiş kategori alanı ile çözülebilir.

---

## 4. Adım Adım Uygulama

### Adım 1: ErrorType.Unexpected Ekleme

**Dosya:** `src/BuildingBlocks/BuildingBlocks.Results/Error.cs`

#### 1a) Enum'a Unexpected eklendi:

```csharp
public enum ErrorType
{
    Failure = 0,      // 500
    NotFound = 1,     // 404
    Validation = 2,   // 400
    Conflict = 3,     // 409
    Unauthorized = 4, // 401
    Unexpected = 5    // 500 (beklenmeyen exception kaynaklı)
}
```

#### 1b) Error record'una factory method eklendi:

```csharp
public static Error Unexpected(string code, string description) =>
    new(code, description, ErrorType.Unexpected);
```

> **Neden Failure ve Unexpected ayrı?**  
> İkisi de 500 dönebilir ama semantik farklı. Monitoring'de `Unexpected` spike'ı alarm verir, `Failure` normal iş akışıdır.

---

### Adım 2: ExceptionHandlingBehavior Oluşturma

**Dosya:** `src/BuildingBlocks/BuildingBlocks.CQRS/Behaviors/ExceptionHandlingBehavior.cs`

```csharp
namespace BuildingBlocks.CQRS.Behaviors;

public sealed class ExceptionHandlingBehavior<TRequest, TResponse>(
    ILogger<ExceptionHandlingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            var requestName = typeof(TRequest).Name;

            logger.LogError(ex,
                "[UNHANDLED EXCEPTION] Request {RequestName} — {ExceptionMessage}",
                requestName, ex.Message);

            if (TryCreateFailureResult(ex, out var result))
            {
                return result;
            }

            throw;
        }
    }

    /// <summary>
    /// TResponse bir IResultBase implementasyonu ise (Result veya Result{T}),
    /// exception'ı Result.Failure'a çevirir.
    /// Reflection kullanıyoruz çünkü generic TResponse'un runtime tipini bilmiyoruz.
    /// </summary>
    private static bool TryCreateFailureResult(Exception ex, out TResponse result)
    {
        result = default!;

        var responseType = typeof(TResponse);

        if (!typeof(IResultBase).IsAssignableFrom(responseType))
            return false;

        var error = Error.Unexpected(
            code: "Unhandled",
            description: ex.Message);

        // Case 1: Result (non-generic)
        if (responseType == typeof(Result))
        {
            result = (TResponse)(object)Result.Failure(error);
            return true;
        }

        // Case 2: Result<T> (generic)
        if (responseType.IsGenericType &&
            responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var innerType = responseType.GetGenericArguments()[0];
            var failureMethod = typeof(Result<>)
                .MakeGenericType(innerType)
                .GetMethod(nameof(Result.Failure), [typeof(Error)])!;

            result = (TResponse)failureMethod.Invoke(null, [error])!;
            return true;
        }

        return false;
    }
}
```

**Nasıl çalışır?**

1. `try { return await next(); }` — bir sonraki behavior veya handler'ı çağırır
2. Exception fırlarsa:
   - Loglar (`[UNHANDLED EXCEPTION]` prefix ile)
   - `TResponse` bir `Result` veya `Result<T>` ise → `Result.Failure(Error.Unexpected(...))` döner
   - Değilse → exception'ı tekrar fırlatır (IExceptionHandler yakalayacak)

**TryCreateFailureResult neden reflection kullanıyor?**

Generic `TResponse` compile-time'da bilinmiyor. `TResponse` şunlardan biri olabilir:
- `Result` → `Result.Failure(error)` çağır
- `Result<ProductResponse>` → `Result<ProductResponse>.Failure(error)` çağır
- `string` veya başka tip → hiçbir şey yapma, exception'ı fırlat

Runtime'da tipi kontrol edip doğru `Failure` methodunu çağırmak için reflection gerekli.

---

### Adım 3: Pipeline'a Kayıt (Program.cs)

**Dosya:** `src/Services/Catalog/Catalog.API/Program.cs`

```csharp
builder.Services.AddMediatR(config =>
{
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);

    // Pipeline Behaviors — her request'te sırayla çalışır:
    // 1) Logging → request başlangıç/bitiş + süre ölçümü
    // 2) Validation → FluentValidation rules, handler'a ulaşmadan önce kontrol
    // 3) ExceptionHandling → beklenmeyen exception → Result.Failure(Unexpected)
    config.AddOpenBehavior(typeof(BuildingBlocks.CQRS.Behaviors.LoggingBehavior<,>));
    config.AddOpenBehavior(typeof(BuildingBlocks.CQRS.Behaviors.ValidationBehavior<,>));
    config.AddOpenBehavior(typeof(BuildingBlocks.CQRS.Behaviors.ExceptionHandlingBehavior<,>));
});
```

> **Sıralama kritik!** Logging en dışta (her şeyi loglar), Validation ortada (geçersiz request keser), ExceptionHandling en içte (handler exception yakalar).

---

### Adım 4: CustomExceptionHandler (IExceptionHandler)

**Dosya:** `src/Services/Catalog/Catalog.API/Exceptions/CustomExceptionHandler.cs`

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.API.Exceptions;

public sealed class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception,
            "[GLOBAL EXCEPTION] {ExceptionType} — {Message}",
            exception.GetType().Name, exception.Message);

        var (statusCode, title) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            FluentValidation.ValidationException =>
                (StatusCodes.Status400BadRequest, "Validation Error"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.com/{statusCode}"
        };

        if (exception is FluentValidation.ValidationException validationEx)
        {
            problemDetails.Extensions["errors"] = validationEx.Errors
                .Select(e => new { e.PropertyName, e.ErrorMessage })
                .ToArray();
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
```

**Program.cs kayıtları:**

```csharp
// Service registration
builder.Services.AddExceptionHandler<Catalog.API.Exceptions.CustomExceptionHandler>();
builder.Services.AddProblemDetails();

// Middleware (UseSerilogRequestLogging sonrasına)
app.UseExceptionHandler();
```

**Exception → HTTP Status Mapping:**

| Exception Tipi | HTTP Status | Kullanım |
|----------------|-------------|----------|
| `ArgumentException` | 400 Bad Request | Geçersiz parametre |
| `KeyNotFoundException` | 404 Not Found | Kaynak bulunamadı |
| `UnauthorizedAccessException` | 401 Unauthorized | Yetkisiz erişim |
| `FluentValidation.ValidationException` | 400 Bad Request | Doğrulama hatası |
| Diğer tüm exception'lar | 500 Internal Server Error | Beklenmeyen hata |

---

### Adım 5: GetProductsByCategory — Yeni Feature Slice

#### 5a) Handler

**Dosya:** `src/Services/Catalog/Catalog.API/Features/GetProductsByCategory/GetProductsByCategoryHandler.cs`

```csharp
namespace Catalog.API.Features.GetProductsByCategory;

public sealed record GetProductsByCategoryQuery(string Category)
    : IQuery<GetProductsByCategoryResult>;

public sealed record GetProductsByCategoryResult(IReadOnlyList<Product> Products);

internal sealed class GetProductsByCategoryHandler(IDocumentSession session)
    : IQueryHandler<GetProductsByCategoryQuery, GetProductsByCategoryResult>
{
    public async Task<Result<GetProductsByCategoryResult>> Handle(
        GetProductsByCategoryQuery query,
        CancellationToken cancellationToken)
    {
        // Marten LINQ — Categories JSONB array'inde Contains → PostgreSQL @> operatörü
        // TODO: Case-insensitive arama için kategori normalizasyonu eklenebilir
        var products = await session.Query<Product>()
            .Where(p => p.Categories.Contains(query.Category))
            .ToListAsync(cancellationToken);

        return new GetProductsByCategoryResult(products);
    }
}
```

> **Dikkat:** `IQuery<T>` zaten `Result<T>` sarmalıyor. `IQuery<Result<GetProductsByCategoryResult>>` yazmak **çift sarmalama** hatasına yol açar. Doğrusu: `IQuery<GetProductsByCategoryResult>`.

#### 5b) Endpoint

**Dosya:** `src/Services/Catalog/Catalog.API/Features/GetProductsByCategory/GetProductsByCategoryEndpoint.cs`

```csharp
namespace Catalog.API.Features.GetProductsByCategory;

public sealed class GetProductsByCategoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/category/{category}",
            async (string category, ISender sender) =>
            {
                var result = await sender.Send(new GetProductsByCategoryQuery(category));

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : Results.NotFound(result.Error);
            })
            .WithName("GetProductsByCategory")
            .WithTags("Products")
            .Produces<GetProductsByCategoryResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithDescription("Get products filtered by category name");
    }
}
```

**Vertical Slice yapısı (klasör):**

```
Features/
  GetProductsByCategory/
    GetProductsByCategoryHandler.cs    ← Query + Result + Handler
    GetProductsByCategoryEndpoint.cs   ← Carter endpoint
```

---

### Adım 6: Mapster Mapping

#### 6a) CreateProductEndpoint — Basit Mapping

```csharp
using Mapster;

// ❌ Eski — manuel mapping (her property elle)
var command = new CreateProductCommand(
    request.Name,
    request.Categories,
    request.Description,
    request.ImageFile,
    request.Price);

// ✅ Yeni — Mapster (zero-config, property isimleri eşleşiyor)
var command = request.Adapt<CreateProductCommand>();
```

#### 6b) UpdateProductEndpoint — Mapping + Route Param

UpdateProduct'ta `Id` route'dan gelir, request body'de yoktur:

```csharp
using Mapster;

// ❌ Eski
var command = new UpdateProductCommand(
    id,
    request.Name,
    request.Categories,
    request.Description,
    request.ImageFile,
    request.Price);

// ✅ Yeni — Mapster + with expression
var command = request.Adapt<UpdateProductCommand>() with { Id = id };
```

**`with { Id = id }` nasıl çalışır?**

1. `Adapt<UpdateProductCommand>()` → Request'teki tüm alanları eşler, `Id` = `Guid.Empty` olur
2. `with { Id = id }` → Immutable kopya oluşturur, sadece `Id`'yi route param ile doldurur
3. Orijinal obje değişmez — **thread-safe**

> **Hatırlama kuralı:** `with` expression sadece `record` type'larda çalışır. Class'larda çalışmaz.

---

### Adım 7: Scalar — OpenAPI Documentation

#### 7a) NuGet Paketleri

```bash
dotnet add package Microsoft.AspNetCore.OpenApi --version 9.0.2
dotnet add package Scalar.AspNetCore
```

> **Dikkat:** `Microsoft.AspNetCore.OpenApi` paketinin versiyonunu `.NET 9` ile uyumlu tutmak gerekir. `--version 9.0.2` belirtilmezse 10.x inebilir ve build hata verir.

#### 7b) Program.cs Değişiklikleri

**Service Registration:**

```csharp
builder.Services.AddOpenApi();
```

**Middleware (sadece Development'ta):**

```csharp
using Scalar.AspNetCore;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                    // /openapi/v1.json endpoint'i
    app.MapScalarApiReference(options =>
    {
        options.Title = "Catalog API";
        options.Theme = ScalarTheme.Mars;
    });
}
```

**Erişim URL'leri:**

| Sayfa | URL |
|-------|-----|
| Scalar UI | `http://localhost:5001/scalar/v1` |
| OpenAPI JSON | `http://localhost:5001/openapi/v1.json` |

---

### Adım 8: Dockerfile — Multi-Stage Build

**Dosya:** `src/Services/Catalog/Catalog.API/Dockerfile`

```dockerfile
# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1) .csproj'ları kopyala — NuGet restore layer cache
COPY src/BuildingBlocks/BuildingBlocks.Results/BuildingBlocks.Results.csproj BuildingBlocks/BuildingBlocks.Results/
COPY src/BuildingBlocks/BuildingBlocks.CQRS/BuildingBlocks.CQRS.csproj BuildingBlocks/BuildingBlocks.CQRS/
COPY src/Services/Catalog/Catalog.API/Catalog.API.csproj Services/Catalog/Catalog.API/

# 2) Restore — NuGet paketleri cache'lenir
RUN dotnet restore Services/Catalog/Catalog.API/Catalog.API.csproj

# 3) Tüm kaynak kodu kopyala
COPY src/BuildingBlocks/ BuildingBlocks/
COPY src/Services/Catalog/ Services/Catalog/

# 4) Publish — Release mode
RUN dotnet publish Services/Catalog/Catalog.API/Catalog.API.csproj \
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

EXPOSE 8080

ENTRYPOINT ["dotnet", "Catalog.API.dll"]
```

**Stage açıklamaları:**

| Stage | Base Image | Boyut | İçerik |
|-------|-----------|-------|--------|
| build | `sdk:9.0` | ~900MB | Compiler, NuGet, tüm kaynak kod |
| runtime | `aspnet:9.0` | ~220MB | Sadece runtime DLL'ler |

**Layer caching stratejisi:**

```
Layer 1: Base image          → Nadiren değişir
Layer 2: .csproj COPY        → Paket değişikliğinde rebuild
Layer 3: dotnet restore      → .csproj değişmediyse cache
Layer 4: Source COPY          → Her kod değişikliğinde rebuild
Layer 5: dotnet publish       → Source değiştiğinde rebuild
```

> **Not:** İlk sürümde `--no-restore` flag'i kullanıldı ama Docker layer'lar arası NuGet cache path uyumsuzluğu nedeniyle kaldırıldı. Küçük süre farkı var ama güvenilirlik daha önemli.

---

### Adım 9: Docker Compose'a Catalog.API Ekleme

**Dosya:** `docker/docker-compose.yml`

```yaml
networks:
  eshop-network:
    driver: bridge

services:
  postgres:
    image: postgres:16
    container_name: postgres
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - ./init-databases.sql:/docker-entrypoint-initdb.d/init-databases.sql
    networks:
      - eshop-network
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

  catalog-api:
    image: catalog-api:latest
    container_name: catalog-api
    build:
      context: ..
      dockerfile: src/Services/Catalog/Catalog.API/Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__Database=Host=postgres;Port=5432;Database=CatalogDb;Username=postgres;Password=postgres
    ports:
      - "5001:8080"
    depends_on:
      postgres:
        condition: service_healthy
    networks:
      - eshop-network
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 15s
    restart: unless-stopped

volumes:
  postgres-data:
```

**Önemli konfigürasyon noktaları:**

| Ayar | Değer | Neden? |
|------|-------|--------|
| `context: ..` | Root dizine çıkar | Dockerfile'daki `COPY src/...` path'leri çalışsın |
| `depends_on: condition: service_healthy` | PostgreSQL healthy olana kadar bekle | DB hazır olmadan uygulama başlamasın |
| `start_period: 15s` | İlk 15sn health check toleransı | Cold start süresini tolere eder |
| `ConnectionStrings__Database` | Docker DNS hostname (`postgres`) | `appsettings.json`'daki `localhost`'u override eder |
| `5001:8080` | External:Internal port mapping | Container internal 8080, dışarıdan 5001 |

---

## 5. Kontrol Listesi

- [x] Build başarılı (`dotnet build` — 0 error)
- [x] ExceptionHandlingBehavior registered (3. behavior sırasında)
- [x] CustomExceptionHandler registered + `app.UseExceptionHandler()` eklendi
- [x] GetProductsByCategory endpoint çalışıyor (`/api/products/category/Smartphone` → 3 ürün)
- [x] Mapster mapping çalışıyor (CreateProduct + UpdateProduct)
- [x] Scalar UI erişilebilir (`/scalar/v1`)
- [x] OpenAPI JSON üretiliyor (`/openapi/v1.json`)
- [x] Docker image build başarılı (`catalog-api:latest`)
- [x] Docker Compose ile PostgreSQL + Catalog.API birlikte ayağa kalkıyor
- [x] Container health check geçiyor (`healthy` status)
- [x] Tüm CRUD endpoint'leri container üzerinden çalışıyor
- [x] Serilog loglama çalışıyor

---

## 6. Sık Hatalar & Çözümleri

### Hata 1: Çift Result Sarmalama

```
error CS0738: 'Handler' does not implement interface member
'IRequestHandler<Query, Result<Result<T>>>.Handle(...)'
```

**Sebep:** `IQuery<T>` zaten `Result<T>` sarmalıyor. `IQuery<Result<T>>` yazmak çift sarmalama yapar.

**Çözüm:**

```csharp
// ❌ Yanlış
public sealed record MyQuery() : IQuery<Result<MyResult>>;

// ✅ Doğru
public sealed record MyQuery() : IQuery<MyResult>;
```

### Hata 2: Marten LINQ — Any + ToLower Çalışmıyor

**Sebep:** Marten, `Any(c => c.ToLower() == ...)` lambda'sını PostgreSQL'e düzgün çeviremiyor.

**Çözüm:** `Contains()` kullan — JSONB `@>` operatörüne çevrilir:

```csharp
// ❌ Çalışmıyor
.Where(p => p.Categories.Any(c => c.ToLower() == category.ToLower()))

// ✅ Çalışıyor
.Where(p => p.Categories.Contains(category))
```

**Trade-off:** Case-sensitive olur. Çözüm: Kategori normalizasyonu veya PostgreSQL `citext`.

### Hata 3: Microsoft.AspNetCore.OpenApi Versiyon Uyumsuzluğu

```
error CS1061: 'IServiceCollection' does not contain a definition for 'AddOpenApi'
```

**Sebep:** `dotnet add package Microsoft.AspNetCore.OpenApi` komutu .NET 10.x versiyonu indirdi.

**Çözüm:** Versiyon belirterek yükle:

```bash
dotnet add package Microsoft.AspNetCore.OpenApi --version 9.0.2
```

### Hata 4: Docker Build — NuGet Package Not Found

```
error NETSDK1064: Package Carter, version 8.2.1 was not found.
```

**Sebep:** `dotnet publish --no-restore` kullanıldığında, Docker layer'lar arası NuGet cache path uyumsuzluğu.

**Çözüm:** `--no-restore` flag'ini kaldır:

```dockerfile
# ❌
RUN dotnet publish ... --no-restore

# ✅
RUN dotnet publish ... -c Release -o /app/publish
```

### Hata 5: Curl Çok Satırlı Komut — Body Gitmemesi

```json
{
  "detail": "Implicit body inferred for parameter \"request\" but no body was provided."
}
```

**Sebep:** Terminalden çok satırlı curl komutu doğru parse edilemiyor.

**Çözüm:** Curl komutunu tek satırda yaz:

```bash
curl -s -X PUT http://localhost:5001/api/products/{id} -H "Content-Type: application/json" -d '{"name":"Test"}' | jq
```

---

## 7. Best Practices

### 7.1 Exception Handling

- **İki katmanlı savunma:** Pipeline Behavior + IExceptionHandler — hiçbir exception kaçmaz
- **Result pattern tutarlılığı:** Exception → `Result.Failure` dönüşümü sayesinde tüm hatalar aynı kanaldan akar
- **Log seviyeleri:** `[UNHANDLED EXCEPTION]` ve `[GLOBAL EXCEPTION]` prefix'leri ile log arama kolaylığı
- **Problem Details (RFC 9457):** Standart hata formatı — tüm client'lar aynı yapıyı bekler

### 7.2 Vertical Slice

- **Feature klasörü:** Her feature kendi klasöründe → Handler + Endpoint + Validator birlikte
- **Yeni feature ekleme:** Mevcut koda dokunmadan sadece yeni klasör + dosyalar
- **Bağımsızlık:** GetProductsByCategory, diğer feature'lardan habersiz

### 7.3 Mapping

- **Convention-based:** Property isimleri eşleşiyorsa konfigürasyon gereksiz
- **`with` expression:** Route param gibi ek alanları immutable şekilde enjekte etmenin SOTA yolu
- **Mapster > AutoMapper:** Compile-time code gen, daha az boilerplate

### 7.4 API Documentation

- **Development-only:** Scalar sadece Development'ta açık (`if (app.Environment.IsDevelopment())`)
- **Paket versiyonu:** `Microsoft.AspNetCore.OpenApi` mutlaka .NET runtime ile uyumlu versiyon
- **Endpoint metadata:** `.WithName()`, `.WithTags()`, `.Produces<T>()`, `.WithDescription()` → Scalar UI'da zengin görünüm

### 7.5 Docker

- **Multi-stage build:** SDK image'ı production'a taşıma
- **Layer caching:** `.csproj` önce, source code sonra
- **Health check:** Her container'da `healthcheck` tanımla
- **`depends_on: condition`:** Dependency'ler healthy olmadan başlama
- **Non-root user:** `USER $APP_UID` — minimal privilege principle
- **`start_period`:** Cold start toleransı, gereksiz restart önleme

---

## 8. TODO / Tartışma Notları

| # | Konu | Açıklama | Öncelik |
|---|------|----------|---------|
| 1 | Case-insensitive kategori araması | Şu an `Contains()` case-sensitive. PostgreSQL `citext` veya normalize edilmiş alan ile çözülebilir | Düşük |
| 2 | Exception mesajlarını production'da gizleme | `ex.Message` yerine generic mesaj (ör: "An unexpected error occurred"). Şu an Development'ta detaylı mesaj gösteriyoruz | Orta |
| 3 | Mapster TypeAdapterConfig | Karmaşık mapping senaryoları için global konfigürasyon. Şu an zero-config yeterli | Düşük |
| 4 | Rate Limiting | Scalar ve API endpoint'lere rate limit. Bölüm 15'te (YARP Gateway) ele alınacak | İleri |
| 5 | OpenAPI schema enrichment | Response örnekleri, enum değerleri, açıklamalar. Endpoint metadata zenginleştirme | Düşük |
| 6 | Docker Compose environment files | `.env` dosyası ile secret'ları `docker-compose.yml` dışına taşıma | Orta |
| 7 | Prometheus metrics entegrasyonu | OpenTelemetry instrumentation + Prometheus scraping. Observability stack'i aktif etme | İleri |

---

## 9. Kısa Özet (Summary)

Bölüm 3'te Catalog.API'yi production-grade seviyeye taşıdık. İki katmanlı exception handling (MediatR pipeline + IExceptionHandler) ile hiçbir exception'ın client'a stack trace olarak sızmamasını garantiledik. Problem Details (RFC 9457) ile standart hata formatı oluşturduk. Yeni bir GetProductsByCategory feature slice ekleyerek Vertical Slice pattern'inin genişletilebilirliğini gördük. Mapster ile mapping boilerplate'ini ortadan kaldırdık. Scalar ile modern OpenAPI dokümantasyonu kurduk. Son olarak, Catalog.API'yi multi-stage Dockerfile ile container'ize edip Docker Compose'a entegre ettik — PostgreSQL ile birlikte tek komutla ayağa kalkıyor.

---

## 10. Ne Öğrendim? (What I Learned) — 3 Madde

1. **Defense-in-Depth Exception Handling:** İki ayrı katman (MediatR Pipeline Behavior + IExceptionHandler) birlikte kullanıldığında hiçbir exception kaçmaz. Pipeline behavior Result pattern'e uyumlu çalışırken, IExceptionHandler pipeline dışı her şeyi Problem Details'e dönüştürür.

2. **Marten LINQ Sınırlamaları:** Marten, PostgreSQL JSONB üzerine kurulu bir ORM ve her LINQ ifadesini SQL'e çeviremez. `Contains()` gibi Marten-uyumlu methodları kullanmak gerekir. Bu, herhangi bir ORM ile çalışırken "generated SQL'i kontrol et" alışkanlığının önemini gösterir.

3. **Docker Multi-Stage + Layer Caching:** `.csproj` dosyalarını kaynak koddan önce kopyalayarak NuGet restore'un cache'lenmesini sağladık. Bu basit sıralama değişikliği CI/CD pipeline'larda dakikalar kazandırır. Multi-stage build ile production image boyutu ~%75 küçülür.

---

## 11. Öğrenme Pekiştirme (Reinforcement)

### Mini Quiz (10 Soru)

**S1 (Doğru/Yanlış):** `ExceptionHandlingBehavior`, `ValidationBehavior`'dan ÖNCE pipeline'a eklenmelidir.

**S2 (Kısa Cevap):** `IExceptionHandler` interface'i hangi .NET versiyonuyla geldi?

**S3 (Senaryo):** Bir handler içinde `NullReferenceException` fırladı. Bu exception'ı ilk kim yakalar: `ExceptionHandlingBehavior` mı yoksa `CustomExceptionHandler` mı?

**S4 (Doğru/Yanlış):** Problem Details (RFC 9457) formatında `instance` alanı zorunludur.

**S5 (Kısa Cevap):** `Error.Failure` ile `Error.Unexpected` arasındaki fark nedir?

**S6 (Senaryo):** `IQuery<Result<GetProductsByCategoryResult>>` yazdığınızda ne olur?

**S7 (Kısa Cevap):** Marten'da `Categories.Contains("X")` ifadesi PostgreSQL'de hangi operatöre çevrilir?

**S8 (Doğru/Yanlış):** Mapster'da property isimleri eşleşmezse otomatik olarak `null` atar.

**S9 (Senaryo):** Dockerfile'da `COPY *.csproj .` satırını `COPY . .` satırından SONRA koysanız ne olurdu?

**S10 (Kısa Cevap):** Docker Compose'da `depends_on: condition: service_healthy` ne işe yarar?

---

### Görevler

#### Görev 1: GetProductsByPriceRange Feature Slice

Yeni bir feature slice oluşturun: Fiyat aralığına göre ürün filtreleme.

**Gereksinimler:**
- Route: `GET /api/products/price?min=100&max=500`
- Query: `GetProductsByPriceRangeQuery(decimal MinPrice, decimal MaxPrice)`
- Marten LINQ ile `p.Price >= min && p.Price <= max` filtreleme
- Boş sonuç için boş liste dönmeli (404 değil)

**Beklenen çıktı:** 3 dosya (Handler, Endpoint) + çalışan curl testi.

#### Görev 2: ExceptionHandling Test

Handler içinde kasıtlı bir `throw new InvalidOperationException("Test exception")` ekleyin ve:
1. Console loglarında `[UNHANDLED EXCEPTION]` prefix'ini görün
2. Response'da Problem Details formatını doğrulayın
3. Test bitince `throw` satırını kaldırın

**Beklenen çıktı:** Log ekran görüntüsü + curl response.

#### Görev 3: Docker Image Boyut Karşılaştırma

```bash
docker images | grep catalog-api
```

Komutuyla image boyutunu not edin. Sonra Dockerfile'ı tek stage (sadece SDK) ile değiştirin:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0
# ... build + run aynı image'da
```

Image boyut farkını karşılaştırın.

**Beklenen çıktı:** İki image boyutu ve fark yüzdesi.

---

### Cevap Anahtarı

**S1:** Yanlış. `ExceptionHandlingBehavior`, `ValidationBehavior`'dan SONRA eklenmelidir. Sıra: Logging → Validation → ExceptionHandling. Aksi halde validation exception'ları "Unexpected" olarak loglanır.

**S2:** .NET 8 (ve sonrasında .NET 9'da da kullanılabilir). `Microsoft.AspNetCore.Diagnostics.IExceptionHandler` interface'i .NET 8 ile tanıtıldı.

**S3:** `ExceptionHandlingBehavior`. Çünkü handler'ı doğrudan saran en içteki behavior'dır. Exception'ı `Result.Failure(Error.Unexpected(...))` olarak döndürür. `CustomExceptionHandler`'a ulaşmaz.

**S4:** Yanlış. RFC 9457'de `instance` alanı opsiyoneldir. Ama best practice olarak request path'i eklenir — debugging için çok değerli.

**S5:** `Error.Failure` → İş kuralı ihlali (beklenen hata, ör: "Stok yetersiz"). `Error.Unexpected` → Exception kaynaklı (beklenmeyen hata, ör: "DB bağlantı koptu"). Monitoring'de `Unexpected` spike'ı alarm verir.

**S6:** Compile-time hata: `Result<Result<T>>` çift sarmalama oluşur. `IQuery<T>` zaten `Result<T>` döner. Doğrusu: `IQuery<GetProductsByCategoryResult>`.

**S7:** PostgreSQL `@>` (contains) operatörüne çevrilir. JSONB array'de eleman arama için optimize edilmiş operatör.

**S8:** Doğru (kısmen). Mapster eşleşmeyen property'leri default değerle bırakır — `string` → `null`, `int` → `0`, `Guid` → `Guid.Empty`. Bu yüzden `UpdateProductCommand`'da `with { Id = id }` ile route param'ı enjekte ettik.

**S9:** Layer caching avantajı kaybolurdu. Her kaynak kod değişikliğinde NuGet restore da tekrar çalışırdı. CI/CD'de gereksiz dakikalar harcanırdı.

**S10:** Bağımlı servisin (ör: PostgreSQL) health check'i geçmeden bu servisin (ör: Catalog.API) başlamasını engeller. Sadece `depends_on: postgres` yazmak yetmez — container'ın başlaması DB'nin hazır olması anlamına gelmez.

---

## Dosya Haritası — Bu Bölümde Oluşturulan/Değiştirilen Dosyalar

```
sota-eshop-microservices/
├── src/
│   ├── BuildingBlocks/
│   │   ├── BuildingBlocks.Results/
│   │   │   └── Error.cs                    ← ErrorType.Unexpected + Error.Unexpected() eklendi
│   │   └── BuildingBlocks.CQRS/
│   │       └── Behaviors/
│   │           └── ExceptionHandlingBehavior.cs  ← YENİ
│   │
│   └── Services/Catalog/Catalog.API/
│       ├── Exceptions/
│       │   └── CustomExceptionHandler.cs    ← YENİ — IExceptionHandler + Problem Details
│       ├── Features/
│       │   └── GetProductsByCategory/
│       │       ├── GetProductsByCategoryHandler.cs   ← YENİ
│       │       └── GetProductsByCategoryEndpoint.cs  ← YENİ
│       ├── Program.cs                       ← Behavior + ExceptionHandler + Scalar eklendi
│       └── Dockerfile                       ← YENİ — Multi-stage build
│
├── docker/
│   └── docker-compose.yml                   ← catalog-api servisi eklendi
│
└── docs/
    └── BOLUM03_ENDPOINTS_VALIDATION.md      ← Bu dosya
```

---

*Bu döküman Bölüm 3'ün tamamlanmış halidir. Bölüm 4: Basket.API — Proje İskeleti + Redis ile devam edilecektir.*
