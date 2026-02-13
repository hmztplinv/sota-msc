# Bölüm 1: Catalog.API — Proje İskeleti + BuildingBlocks

> **Tarih:** 2025-02-12  
> **Proje:** sota-eshop-microservices  
> **Platform:** .NET 9 + Docker Compose + PostgreSQL  
> **Ortam:** WSL2 / Docker / VSCode

---

## 1. Amaç & Kazanımlar

Bu bölümde mikroservis projesinin **temel iskeletini** oluşturduk. Sadece klasör yapısı değil — projenin geri kalanında kullanacağımız **generic building block'ları** (Result pattern, CQRS abstractions) yazdık ve ilk mikroservisimiz Catalog.API'yi ayağa kaldırdık.

### Bu Bölümde Ne Yaptık?

- ✅ `sota-eshop-microservices.sln` solution yapısını oluşturduk
- ✅ `BuildingBlocks.Results` — Generic Result\<T\> pattern implementasyonu
- ✅ `BuildingBlocks.CQRS` — Generic ICommand, IQuery, Handler abstractions
- ✅ `Catalog.API` — Minimal API projesi (Marten + Carter + Serilog + MediatR)
- ✅ `Product` domain modeli + Seed Data (IInitialData)
- ✅ Docker Compose ile PostgreSQL container
- ✅ Marten Document DB bağlantısı ve schema auto-creation
- ✅ Health check endpoint
- ✅ Serilog structured logging

---

## 2. Kavramlar & Tanımlar

### 2.1 Result Pattern (Sonuç Kalıbı)

**Ne?** Bir işlemin başarı veya hata durumunu **explicit** (açık) olarak ifade eden yapı.

**Neden?** C#'ta exception fırlatmak pahalıdır (stack trace oluşturma maliyeti) ve intent'i (niyeti) gizler. `Result<T>` ile:
- Başarılı sonuç → `Result<T>.Success(value)`
- Hatalı sonuç → `Result<T>.Failure(error)`
- Exception → Sadece **gerçek** beklenmedik hatalar için (programcı hatası, altyapı çökmesi)

```csharp
// ❌ Kötü — Exception ile flow control
public Product GetProduct(Guid id)
{
    var product = db.Find(id);
    if (product is null)
        throw new NotFoundException("Product not found"); // Pahalı + gizli
    return product;
}

// ✅ İyi — Result ile explicit flow
public Result<Product> GetProduct(Guid id)
{
    var product = db.Find(id);
    if (product is null)
        return Error.NotFound("Product.NotFound", $"Product {id} not found");
    return product; // Implicit conversion
}
```

### 2.2 CQRS (Command Query Responsibility Segregation — Komut Sorgu Sorumluluk Ayrımı)

**Ne?** Yazma (Command) ve okuma (Query) işlemlerini **farklı modeller** üzerinden yürütme.

**Neden?**
- **Okuma** ve **yazma** farklı ihtiyaçlara sahip (okuma: hız, caching; yazma: validation, consistency)
- Pipeline behavior'larda ayrıştırma yapabilme: "Sadece command'lara validation uygula"
- Ölçeklendirme (scaling) bağımsızlığı

```
┌─────────────┐     ┌──────────────────┐     ┌──────────────┐
│  Controller  │────▶│    MediatR        │────▶│   Handler    │
│  (Endpoint)  │     │  Pipeline         │     │              │
└─────────────┘     │  ┌─Validation──┐  │     │  ICommand ──▶│ Write DB
                    │  ├─Logging─────┤  │     │  IQuery ────▶│ Read DB
                    │  └─Caching─────┘  │     └──────────────┘
                    └──────────────────┘
```

### 2.3 Vertical Slice Architecture (Dikey Dilim Mimarisi)

**Ne?** Kodu **teknik katmanlara** (Controller → Service → Repository) değil, **feature'lara** (özellik) göre organize etme.

**Neden?**
- Bir feature'ı değiştirmek tek bir klasörde kalır
- Katmanlar arası gereksiz abstraction yok
- Her slice kendi modelini, handler'ını, validator'ını içerir

```
❌ Katmanlı:                    ✅ Vertical Slice:
Controllers/                    Features/
  ProductController.cs            Products/
Services/                           GetProducts/
  ProductService.cs                   GetProductsQuery.cs
Repositories/                         GetProductsHandler.cs
  ProductRepository.cs                GetProductsEndpoint.cs
                                    CreateProduct/
                                      CreateProductCommand.cs
                                      CreateProductHandler.cs
                                      CreateProductEndpoint.cs
```

### 2.4 Marten (Document DB on PostgreSQL)

**Ne?** PostgreSQL'i **Document Database** ve **Event Store** olarak kullanmamızı sağlayan .NET kütüphanesi.

**Neden?**
- PostgreSQL'in `jsonb` column tipi sayesinde JSON document'ları native olarak saklar
- Şema esnekliği — migration gerektirmeden alan ekleme/çıkarma
- Event Sourcing desteği (Bölüm 2+'da kullanacağız)
- Ayrı bir MongoDB/CosmosDB'ye ihtiyaç yok

**Nasıl Çalışır?**
```
Product C# nesnesi
    ↓ serialize (System.Text.Json)
mt_doc_product tablosu
    ├── id (uuid, PK)
    ├── data (jsonb) ← Product JSON burada
    ├── mt_last_modified (timestamp)
    ├── mt_version (uuid)
    └── mt_dotnet_type (varchar)
```

### 2.5 Carter (Minimal API Modüler Routing)

**Ne?** ASP.NET Minimal API'ları **modüler** olarak organize etmeyi sağlayan kütüphane.

**Neden?** Vanilla Minimal API'da tüm endpoint'ler `Program.cs`'te birikiyor. Carter ile her feature kendi `ICarterModule`'ünde tanımlanır.

```csharp
// Carter modülü — Bölüm 2'de yazacağız
public class ProductEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", GetProducts);
        app.MapPost("/api/products", CreateProduct);
    }
}
```

### 2.6 Serilog (Yapılandırılmış Loglama)

**Ne?** .NET için structured logging kütüphanesi.

**Neden?**
- Log mesajları **JSON formatında** yazılır — log aggregation tool'ları (Seq, Elasticsearch) ile aranabilir
- `Enrich` ile her log'a otomatik property ekleme (Application adı, TraceId, vb.)
- Bootstrap Logger pattern: uygulama ayağa kalkarken bile logları yakalar

```json
{
  "Timestamp": "22:18:51",
  "Level": "INF",
  "Message": "HTTP GET /health responded 200 in 38.73 ms",
  "Application": "Catalog.API"
}
```

### 2.7 MediatR (Mediator Pattern Implementasyonu)

**Ne?** In-process mesajlaşma kütüphanesi. CQRS pattern'inin altyapısı.

**Neden?**
- Command/Query → Handler eşleşmesini otomatik yapar
- **Pipeline Behaviors** ile cross-cutting concern'leri (validation, logging, caching) handler'lara dokunmadan ekler
- Loose coupling — endpoint, handler'ı doğrudan çağırmaz, MediatR üzerinden gönderir

---

## 3. Neden Böyle? Mimari Gerekçe

### 3.1 Neden BuildingBlocks Ayrı Proje?

| Karar | Gerekçe |
|-------|---------|
| **Ayrı class library** | Her mikroservis sadece ihtiyacı olan BuildingBlock'u referans alır (Interface Segregation) |
| **Results ayrı, CQRS ayrı** | Discount.Grpc gibi basit servisler sadece Results kullanabilir, CQRS'e ihtiyaç duymaz |
| **CQRS → Results referansı** | Command/Query handler'lar `Result<T>` döner — doğal bağımlılık |

### 3.2 Neden Result\<T\> Class, Record Değil?

| Seçenek | Trade-off |
|---------|-----------|
| `record Result<T>` | Immutable ama inheritance desteklemez (sealed olur) |
| `class Result<T>` | `Result<T> : Result` inheritance'ı mümkün — void ve değer dönen versiyonlar aynı base |
| **Kararımız:** class | Inheritance + implicit operator desteği için |

### 3.3 Neden Error Record?

`Error` value object — iki hata aynı Code + Message + Type'a sahipse eşittir. Record'un sağladığı:
- Immutability (değiştirilemez)
- Value equality (referans değil, değer karşılaştırma)
- Deconstruction desteği

### 3.4 Neden Marten (PostgreSQL) ve MongoDB Değil?

| Kriter | Marten | MongoDB |
|--------|--------|---------|
| Altyapı | Zaten PostgreSQL kullanıyoruz | Ayrı database engine |
| Event Sourcing | Built-in | Ek kütüphane gerekir |
| Transaction | PostgreSQL ACID transactions | Multi-document tx sınırlı |
| Tooling | psql, pgAdmin — bilinen araçlar | mongosh — ayrı öğrenme |
| **Karar** | ✅ Tek DB engine, dual kullanım | ❌ Ek complexity |

### 3.5 Docker Compose'da Neden CatalogDb Init Script?

Marten 8.x'te `CreateDatabasesForTenants` API değişmiş. Pragmatik çözüm:
- `init-databases.sql` → PostgreSQL'in `/docker-entrypoint-initdb.d/` mekanizması
- Container ilk oluşturulduğunda otomatik çalışır
- Tekrar başlatmalarda skip eder (idempotent)

---

## 4. Adım Adım Uygulama

### Adım 1.1 — Solution ve Klasör Yapısı

```bash
# Ana dizin
mkdir -p ~/sota-eshop-microservices && cd ~/sota-eshop-microservices

# Solution
dotnet new sln -n sota-eshop-microservices

# Klasörler
mkdir -p src/BuildingBlocks
mkdir -p src/Services/{Catalog,Basket,Discount,Ordering}
mkdir -p src/ApiGateways src/WebApps
mkdir -p monitoring/{prometheus,grafana/dashboards,grafana/provisioning,jaeger}
mkdir -p docker docs
```

### Adım 1.2 — Projeleri Oluştur ve Bağla

```bash
# BuildingBlocks
dotnet new classlib -n BuildingBlocks.CQRS -o src/BuildingBlocks/BuildingBlocks.CQRS -f net9.0
dotnet new classlib -n BuildingBlocks.Results -o src/BuildingBlocks/BuildingBlocks.Results -f net9.0

# Catalog.API
dotnet new web -n Catalog.API -o src/Services/Catalog/Catalog.API -f net9.0

# Solution'a ekle
dotnet sln add src/BuildingBlocks/BuildingBlocks.CQRS/BuildingBlocks.CQRS.csproj
dotnet sln add src/BuildingBlocks/BuildingBlocks.Results/BuildingBlocks.Results.csproj
dotnet sln add src/Services/Catalog/Catalog.API/Catalog.API.csproj

# Referanslar
dotnet add src/Services/Catalog/Catalog.API/ reference src/BuildingBlocks/BuildingBlocks.CQRS/
dotnet add src/Services/Catalog/Catalog.API/ reference src/BuildingBlocks/BuildingBlocks.Results/
dotnet add src/BuildingBlocks/BuildingBlocks.CQRS/ reference src/BuildingBlocks/BuildingBlocks.Results/
```

**Referans Grafiği:**
```
Catalog.API
  ├── BuildingBlocks.CQRS
  │     └── BuildingBlocks.Results
  └── BuildingBlocks.Results
```

### Adım 1.3 — BuildingBlocks.Results

**3 kavram:** `Error` (hata bilgisi), `Result` (void sonuç), `Result<T>` (değerli sonuç).

**Error.cs** — Immutable hata value object:
```csharp
namespace BuildingBlocks.Results;

public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static readonly Error None = new(string.Empty, string.Empty);
}

public enum ErrorType
{
    Failure = 0,      // → HTTP 500
    NotFound = 1,     // → HTTP 404
    Validation = 2,   // → HTTP 400
    Conflict = 3,     // → HTTP 409
    Unauthorized = 4  // → HTTP 401
}
```

**Result.cs** — Generic result container:
```csharp
namespace BuildingBlocks.Results;

// Değer döndürmeyen işlemler için (DeleteProduct → başarılı mı?)
public class Result
{
    protected Result(bool isSuccess, Error error) { /* invariant kontrol */ }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    // Error → Result implicit conversion
    public static implicit operator Result(Error error) => Failure(error);
}

// Değer döndüren işlemler için (GetProduct → Result<ProductResponse>)
public class Result<T> : Result
{
    public T Value { get; } // IsSuccess true ise erişilebilir

    // Implicit conversions — temiz kullanım
    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
```

**Kullanım Örneği (Bölüm 2'de göreceğiz):**
```csharp
// Handler'da
public async Task<Result<ProductResponse>> Handle(GetProductByIdQuery query, CancellationToken ct)
{
    var product = await session.LoadAsync<Product>(query.Id, ct);
    if (product is null)
        return Error.NotFound("Product.NotFound", $"Product {query.Id} not found");
    
    return product.Adapt<ProductResponse>(); // implicit Result<T>.Success
}
```

### Adım 1.4 — BuildingBlocks.CQRS

**Neden MediatR'ın IRequest'ini wrap ediyoruz?**
- `ICommand<T>` vs `IQuery<T>` semantik ayrım
- Pipeline behavior'larda `where TRequest : ICommand<TResponse>` filtresi
- Tüm handler'lar `Result<T>` dönmeye zorlanır — tutarlılık

**ICommand.cs:**
```csharp
using BuildingBlocks.Results;
using MediatR;

namespace BuildingBlocks.CQRS;

// Değer döndüren command (CreateProduct → Result<Guid>)
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

// Void command (DeleteProduct → Result)
public interface ICommand : IRequest<Result>;

// Handler'lar
public interface ICommandHandler<in TCommand, TResponse> 
    : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface ICommandHandler<in TCommand> 
    : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;
```

**IQuery.cs:**
```csharp
using BuildingBlocks.Results;
using MediatR;

namespace BuildingBlocks.CQRS;

// Query — her zaman değer döner
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface IQueryHandler<in TQuery, TResponse> 
    : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
```

**Generic Hiyerarşi:**
```
MediatR.IRequest<TResponse>
  ├── ICommand<TResponse> : IRequest<Result<TResponse>>
  └── IQuery<TResponse>   : IRequest<Result<TResponse>>

MediatR.IRequestHandler<TRequest, TResponse>
  ├── ICommandHandler<TCommand, TResponse>
  └── IQueryHandler<TQuery, TResponse>
```

### Adım 1.5 — Catalog.API NuGet Paketleri

```bash
dotnet add src/Services/Catalog/Catalog.API/ package Marten                                    # Document DB
dotnet add src/Services/Catalog/Catalog.API/ package Carter --version 8.2.1                    # Modüler endpoints
dotnet add src/Services/Catalog/Catalog.API/ package Mapster                                   # Object mapping
dotnet add src/Services/Catalog/Catalog.API/ package Serilog.AspNetCore                        # Structured logging
dotnet add src/Services/Catalog/Catalog.API/ package FluentValidation                          # Validation
dotnet add src/Services/Catalog/Catalog.API/ package FluentValidation.DependencyInjectionExtensions
```

> ⚠️ **Carter 10.0.0** sadece .NET 10 destekler. .NET 9 için **8.2.1** kullanılmalı!

### Adım 1.6 — Product Domain Modeli

```csharp
namespace Catalog.API.Models;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public List<string> Categories { get; set; } = [];
    public string ImageFile { get; set; } = default!;
    public decimal Price { get; set; }
}
```

> **Neden class (record değil)?** Marten deserialization için mutable property'ler istiyor.  
> **Neden `default!`?** Null-safety uyarısını bastırır — Marten set edecek.  
> **Neden `[] `(collection expression)?** C# 13 — `new List<string>()` yerine kısa syntax.

### Adım 1.7 — Program.cs (Wiring)

```csharp
using Catalog.API.Data;
using Carter;
using Marten;
using Serilog;

// Bootstrap Logger — uygulama ayağa kalkarken hataları yakala
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Catalog.API starting up...");
    var builder = WebApplication.CreateBuilder(args);

    // Serilog — appsettings'ten config oku
    builder.Host.UseSerilog((context, loggerConfig) =>
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.WithProperty("Application", "Catalog.API"));

    // Marten — PostgreSQL document DB
    builder.Services.AddMarten(options =>
    {
        var connectionString = builder.Configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string is required.");
        options.Connection(connectionString);

        if (builder.Environment.IsDevelopment())
            options.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

    }).InitializeWith<CatalogInitialData>()
      .UseLightweightSessions();

    // MediatR — CQRS handler'ları auto-register
    builder.Services.AddMediatR(config =>
        config.RegisterServicesFromAssembly(typeof(Program).Assembly));

    // Carter — endpoint modülleri auto-discover
    builder.Services.AddCarter();

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.MapCarter();
    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Catalog.API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
```

> ⚠️ **Marten 8.x not:** `AutoCreateSchemaObjects` enum'ı `JasperFx.AutoCreate.All` namespace'inde.  
> Eski versiyonlardaki `Weasel.Core.AutoCreate` artık geçerli değil.

### Adım 1.8 — Seed Data

```csharp
public sealed class CatalogInitialData : IInitialData
{
    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        await using var session = store.LightweightSession();

        if (await session.Query<Product>().AnyAsync(cancellation))
            return; // İdempotent — tekrar ekleme

        session.Store(GetPreconfiguredProducts().ToArray()); // ⚠️ Marten 8.x: .ToArray() zorunlu!
        await session.SaveChangesAsync(cancellation);
    }
}
```

> ⚠️ **Marten 8.x not:** `session.Store()` artık `IReadOnlyList<T>` kabul etmiyor.  
> `.ToArray()` ile array'e çevirmek gerekiyor.

### Adım 1.9 — Docker Compose + PostgreSQL

```yaml
# docker/docker-compose.yml
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

volumes:
  postgres-data:
```

```sql
-- docker/init-databases.sql
CREATE DATABASE "CatalogDb";
```

**Başlatma:**
```bash
docker compose -f docker/docker-compose.yml up -d
```

---

## 5. Kontrol Listesi

- [x] Solution oluşturuldu (`sota-eshop-microservices.sln`)
- [x] BuildingBlocks.Results build başarılı
- [x] BuildingBlocks.CQRS build başarılı
- [x] Catalog.API build başarılı
- [x] Docker Compose ile PostgreSQL container healthy
- [x] CatalogDb database oluşturuldu
- [x] Marten schema auto-created (`mt_doc_product` tablosu)
- [x] Seed data 5 ürün eklendi
- [x] Health check `/health` endpoint 200 OK
- [x] Serilog structured logging çalışıyor

**Doğrulama Komutları:**
```bash
# PostgreSQL healthy?
docker exec postgres pg_isready -U postgres

# Seed data geldi mi?
docker exec postgres psql -U postgres -d CatalogDb \
  -c "SELECT data->>'Name' as name, data->>'Price' as price FROM mt_doc_product;"

# Health check
curl -s http://localhost:5075/health
```

---

## 6. Sık Hatalar & Çözümleri

### Hata 1: Carter 10.0.0 .NET 9 ile uyumsuz
```
error: NU1202: Package Carter 10.0.0 is not compatible with net9.0
```
**Çözüm:** Versiyon belirt: `dotnet add package Carter --version 8.2.1`

### Hata 2: Weasel.Core.AutoCreate bulunamadı
```
error CS0234: 'AutoCreate' does not exist in namespace 'Weasel.Core'
```
**Çözüm:** Marten 8.x ile namespace değişti → `JasperFx.AutoCreate.All`

### Hata 3: CatalogDb database does not exist
```
3D000: database "CatalogDb" does not exist
```
**Çözüm:** Docker Compose'da `init-databases.sql` ile otomatik oluştur, veya manuel:
```bash
docker exec postgres psql -U postgres -c "CREATE DATABASE \"CatalogDb\";"
```

### Hata 4: Marten Store() IReadOnlyList kabul etmiyor
```
ArgumentOutOfRangeException: Do not use IEnumerable<T> here
```
**Çözüm:** `.ToArray()` ekle: `session.Store(products.ToArray())`

### Hata 5: CreateDatabasesForTenants API değişmiş
```
CS1061: 'MartenConfigurationExpression' does not contain 'CreateDatabasesForTenants'
```
**Çözüm:** Marten 8.x'te bu API kaldırıldı. Database oluşturmayı Docker init script'e taşı.

---

## 7. Best Practices — Bu Bölüme Özel

### 7.1 Generic-First Approach
BuildingBlocks'u **önce interface/abstraction**, sonra implementation olarak yazdık. Bu sayede:
- Her mikroservis aynı contract'ları kullanır
- Pipeline behavior'lar generic olduğu için tüm handler'lara uygulanır
- Yeni bir servis eklemek → sadece referans ekle, BuildingBlocks'u tekrar yazma

### 7.2 Implicit Operator Kullanımı
`Result<T>` üzerinde implicit conversion tanımladık:
```csharp
public static implicit operator Result<T>(T value) => Success(value);
public static implicit operator Result<T>(Error error) => Failure(error);
```
Bu sayede handler'larda `return product;` yazabiliyoruz — `Result<T>.Success(product)` yazmaya gerek yok.

### 7.3 Bootstrap Logger Pattern
Serilog'da iki aşamalı logger:
1. **Bootstrap** — uygulama configuration okunmadan önce bile çalışır
2. **Configuration-aware** — `builder.Host.UseSerilog()` ile gerçek config'den okur

Bu pattern, DI container ayağa kalkmadan önce oluşan hataları da yakalar.

### 7.4 Docker Init Script Mekanizması
PostgreSQL Docker image'ı, `/docker-entrypoint-initdb.d/` klasöründeki `.sql` dosyalarını **sadece ilk başlatmada** çalıştırır. Bu idempotent bir mekanizmadır — volume zaten varsa tekrar çalışmaz.

---

## 8. TODO / Tartışma Notları

- **TODO:** Marten log seviyesini `Warning`'e çekmek — şu an schema SQL'leri çok verbose loglanıyor
- **TODO:** Health check'i genişlet — PostgreSQL bağlantı kontrolü ekle (`AspNetCore.Diagnostics.HealthChecks`)
- **TODO:** `launchSettings.json`'daki port'u `5001` olarak standardize et (Master Plan ile uyumlu)
- **TODO:** `.gitignore` dosyası oluştur (bin, obj, .vs, .idea, docker volumes)
- **TARTIŞMA:** Marten 8.x API değişiklikleri → resmi dökümantasyon ile versiyon pinleme stratejisi

---

## 9. Kısa Özet (Summary)

Bu bölümde `sota-eshop-microservices` projesinin temel iskeletini kurduk. İki **generic BuildingBlocks** kütüphanesi yazdık: `Result<T>` pattern ile exception'sız hata yönetimi, `ICommand`/`IQuery` ile CQRS semantik ayrımı. Catalog.API'yi Marten (PostgreSQL document DB), Carter (modüler endpoints), Serilog (structured logging) ve MediatR (CQRS pipeline) ile ayağa kaldırdık. Docker Compose ile PostgreSQL container'ı çalıştırdık ve 5 örnek ürünü seed data olarak veritabanına yazdık.

---

## 10. Ne Öğrendim? (What I Learned) — 3 Madde

1. **Result\<T\> pattern, exception'ları flow control olarak kullanmaktan çok daha iyi.** Explicit başarı/hata durumu, implicit operator'ler ile temiz syntax, ve `ErrorType` ile HTTP status code mapping — production-grade error handling'in temeli.

2. **Generic CQRS abstractions (ICommand/IQuery) MediatR'ın üzerine ince bir katman ekliyor ama büyük fayda sağlıyor.** Command ve Query'yi tip seviyesinde ayırmak, pipeline behavior'larda seçici filtreleme ve tutarlı `Result<T>` dönüş tipi zorunluluğu getiriyor.

3. **Marten 8.x'in breaking change'leri öğretici oldu.** `Weasel.Core.AutoCreate` → `JasperFx.AutoCreate`, `Store()` artık array istiyor, `CreateDatabasesForTenants` kaldırıldı. Lesson: NuGet paketlerinde **versiyon pinleme** ve **migration notlarını okumak** kritik.

---

## 11. Öğrenme Pekiştirme (Reinforcement)

### Mini Quiz (10 Soru)

**S1 (Doğru/Yanlış):** Result pattern'de başarısız bir işlem exception fırlatır.

**S2 (Kısa Cevap):** `ICommand<TResponse>` MediatR'ın hangi interface'inden türer ve dönüş tipi nedir?

**S3 (Doğru/Yanlış):** Marten, MongoDB gibi ayrı bir database engine kullanır.

**S4 (Kısa Cevap):** Carter'ın Minimal API'ya eklediği temel fayda nedir?

**S5 (Senaryo):** Bir handler'da `return Error.NotFound("X", "Y");` yazarsak, bu implicit olarak ne döner?

**S6 (Kısa Cevap):** Serilog Bootstrap Logger pattern'inde neden iki aşamalı logger kullanılır?

**S7 (Doğru/Yanlış):** `BuildingBlocks.CQRS` projesi `BuildingBlocks.Results`'a bağımlıdır, tersi de doğrudur.

**S8 (Kısa Cevap):** Marten 8.x'te `options.AutoCreateSchemaObjects` için doğru namespace nedir?

**S9 (Senaryo):** Docker Compose'da PostgreSQL container'ı restart edildiğinde `init-databases.sql` tekrar çalışır mı?

**S10 (Kısa Cevap):** `session.Store(products)` yerine `session.Store(products.ToArray())` yazmamızın sebebi nedir?

---

### Görevler (3 Adet)

**Görev 1:** `ErrorType` enum'ına `Forbidden = 5` (403) ekle. `Error` record'una `Forbidden` factory metodu ekle. Build'in başarılı olduğunu doğrula.

**Görev 2:** Health check endpoint'ini genişlet — PostgreSQL bağlantı durumunu döndür. İpucu: `IDocumentStore`'u inject edip bir query çalıştır.

**Görev 3:** `appsettings.Development.json`'daki Serilog Marten override'ını `Warning`'e çevir. Uygulamayı yeniden çalıştır ve schema SQL loglarının artık görünmediğini doğrula.

---

### Cevap Anahtarı

**S1:** Yanlış. Result pattern'de hata `Result.Failure(error)` olarak döner, exception fırlatılmaz.

**S2:** `IRequest<Result<TResponse>>` — MediatR'ın `IRequest<T>` interface'inden türer, dönüş tipi `Result<TResponse>`.

**S3:** Yanlış. Marten, PostgreSQL'in `jsonb` column tipini kullanarak document DB işlevselliği sağlar — ayrı engine yok.

**S4:** Modülerlik — her feature kendi `ICarterModule`'ünde endpoint'lerini tanımlar, `Program.cs`'te birikmez.

**S5:** `Result<T>.Failure(Error.NotFound("X", "Y"))` — implicit operator sayesinde `Error` otomatik olarak `Result<T>` failure'a dönüşür.

**S6:** Configuration okunmadan (DI container ayağa kalkmadan) önce oluşabilecek hataları yakalamak için. İlk aşama Console'a yazar, ikinci aşama appsettings'ten config okur.

**S7:** Yanlış. Tek yönlü: CQRS → Results. Results, CQRS'e bağımlı değildir.

**S8:** `JasperFx.AutoCreate.All` — Marten 8.x ile Weasel 8.6'da bu namespace'e taşındı.

**S9:** Hayır. PostgreSQL init script'leri sadece volume **ilk kez oluşturulduğunda** çalışır. Restart'ta çalışmaz (idempotent).

**S10:** Marten 8.x'te `Store<T>()` metodu `IReadOnlyList<T>` veya `IEnumerable<T>` kabul etmiyor, `T[]` (array) istiyor. `ArgumentOutOfRangeException` fırlatır.

---

## 📁 Bölüm Sonu Dosya Yapısı

```
sota-eshop-microservices/
├── sota-eshop-microservices.sln
├── src/
│   ├── BuildingBlocks/
│   │   ├── BuildingBlocks.CQRS/
│   │   │   ├── BuildingBlocks.CQRS.csproj
│   │   │   ├── ICommand.cs
│   │   │   └── IQuery.cs
│   │   └── BuildingBlocks.Results/
│   │       ├── BuildingBlocks.Results.csproj
│   │       ├── Error.cs
│   │       └── Result.cs
│   └── Services/
│       └── Catalog/
│           └── Catalog.API/
│               ├── Catalog.API.csproj
│               ├── Program.cs
│               ├── appsettings.Development.json
│               ├── Models/
│               │   └── Product.cs
│               ├── Data/
│               │   └── CatalogInitialData.cs
│               └── Features/
│                   └── Products/        ← Bölüm 2'de dolacak
├── docker/
│   ├── docker-compose.yml
│   └── init-databases.sql
├── monitoring/
│   ├── prometheus/
│   ├── grafana/
│   └── jaeger/
└── docs/
    └── BOLUM01_CATALOG_SETUP.md  ← Bu dosya
```

---

*Bölüm 1 tamamlandı. Sonraki: **Bölüm 2 — Catalog.API: Vertical Slice + CQRS Handlers***  
*GetProducts, GetProductById, CreateProduct, UpdateProduct, DeleteProduct handler'ları + Carter endpoints*

---

> **Versiyon:** 1.0 | **Son Güncelleme:** 2025-02-12

## Bölüm 1 — TODO Tamamlama Notları

> Bu notları `BOLUM01_CATALOG_SETUP.md`'nin ilgili bölümlerine ekle.

---

### TODO 1: Marten Log Seviyesi ✅

`appsettings.Development.json`'da Serilog override'ları eklendi:

```json
"Override": {
    "Microsoft": "Warning",
    "Microsoft.AspNetCore": "Warning",
    "Marten": "Warning",
    "Npgsql": "Warning"
}
```

**Etki:** Marten'ın verbose schema SQL logları artık görünmüyor. Sadece `Warning` ve üstü loglanıyor.

---

### TODO 2: Seq ⏭️ (Ertelendi)

Seq container'ı (`datalust/seq`) WSL2 + Docker Desktop ortamında crash ediyor. 2024.3 ve 2023.4 versiyonları da aynı sorunu verdi (Autofac resolution hatası). 

**Karar:** Seq, Bölüm 8 (Observability Stack) ile birlikte Prometheus + Grafana kurulurken tekrar denenecek. Şu an **Serilog Console sink** yeterli.

**appsettings'ten Seq sink kaldırıldı**, `Serilog.Sinks.Seq` paketi projede kalabilir (ileride kullanılacak).

---

### TODO 3: Health Check — PostgreSQL Kontrolü ✅

**Eklenen Paket:**
```bash
dotnet add src/Services/Catalog/Catalog.API/ package AspNetCore.HealthChecks.NpgSql
```

**Program.cs Değişiklikleri:**

1. Using eklendi:
```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
```

2. DI registration (AddCarter'dan sonra):
```csharp
// --- Health Checks ---
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Database") 
        ?? throw new InvalidOperationException("Database connection string is required for health check."));
```

3. Eski basit endpoint kaldırıldı, ASP.NET Health Checks middleware eklendi:
```csharp
// Eski: app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));
// Yeni:
app.MapHealthChecks("/health");
```

**Fark:** Artık `/health` endpoint'i sadece "Healthy" demekle kalmıyor, PostgreSQL bağlantısını da kontrol ediyor. DB çökerse `Unhealthy` döner.

**Test:** `curl -s http://localhost:5001/health` → `Healthy`

---

### TODO 4: launchSettings.json Port Standardizasyonu ✅

Port `5001` olarak sabitlendi (Master Plan ile uyumlu):

```json
// src/Services/Catalog/Catalog.API/Properties/launchSettings.json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5001",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

**Port Mapping Tablosu (Master Plan):**
| Servis | Port |
|--------|------|
| Catalog.API | 5001 |
| Basket.API | 5002 |
| Discount.Grpc | 5003 |
| Ordering.API | 5004 |
| YARP Gateway | 5000 |

---

### TODO 5: .gitignore ✅

```
## .NET
bin/
obj/
*.user
*.suo
*.cache
*.dll
*.pdb

## IDE
.vs/
.vscode/
.idea/
*.swp

## Docker volumes
docker/*-data/

## OS
.DS_Store
Thumbs.db

## Logs
*.log

## NuGet
packages/
*.nupkg
```

---

### TODO 6: Git Init + İlk Commit + Push ✅

```bash
git init
git add .
git commit -m "Bölüm 1: Catalog.API iskelet + BuildingBlocks (Result pattern, CQRS abstractions, Marten, Carter, Serilog)"
git remote add origin https://github.com/hmztplinv/sota-msc.git
git push -u origin main
```

**Strateji:** Her bölüm sonunda commit + push yapılacak. Commit mesajı: `"Bölüm X: [kısa açıklama]"`

---

### TARTIŞMA: Marten 8.x Versiyon Pinleme ✅ (Açıklama)

Bölüm 1'de 3 breaking change yaşandı:

| Sorun | Eski API | Yeni API (Marten 8.x) |
|-------|----------|----------------------|
| AutoCreate namespace | `Weasel.Core.AutoCreate` | `JasperFx.AutoCreate` |
| Store() parametresi | `IReadOnlyList<T>` kabul ederdi | Sadece `T[]` (array) kabul ediyor |
| DB oluşturma | `CreateDatabasesForTenants()` | API kaldırıldı → Docker init script |

**Versiyon Pinleme Stratejisi:** NuGet'te `.csproj` dosyasında sabit versiyon belirtiyoruz (örn: `Marten 8.21.0`). `dotnet add package` her zaman en son uyumlu versiyonu çeker ve pinler. Bilinçli upgrade yapmadan versiyon değişmez. Bu strateji zaten uygulanıyor — ek aksiyon gerekmiyor.

---

> **Bölüm 1 tamamen tamamlandı. Sonraki session: Bölüm 2 — Vertical Slice + CQRS Handlers**