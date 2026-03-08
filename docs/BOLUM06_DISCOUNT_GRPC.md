# Bölüm 6: Discount.Grpc — gRPC Service

## 1. Amaç & Kazanımlar

Bu bölümde Discount mikroservisini **gRPC** protokolü ile oluşturduk. REST API yerine binary protocol kullanan, servisler arası iletişim (inter-service communication) için optimize edilmiş bir servis geliştirdik.

**Kazanımlar:**
- gRPC ve Protocol Buffers (Protobuf) kavramlarını öğrenmek
- `.proto` dosyası ile contract-first API tasarımı yapmak
- EF Core + PostgreSQL ile basit bir veritabanı katmanı kurmak
- Layered Architecture'ı ne zaman tercih edeceğini anlamak
- Docker Compose'a yeni bir servis eklemeyi pekiştirmek
- gRPC Reflection ile test yapabilmek

---

## 2. Kavramlar & Tanımlar

### gRPC (gRPC Remote Procedure Call)
Google tarafından geliştirilen, **HTTP/2** üzerinde çalışan yüksek performanslı bir RPC (Uzaktan Yordam Çağrısı) framework'üdür. REST'ten farklı olarak:
- **Binary protocol** kullanır (JSON yerine Protocol Buffers)
- **HTTP/2** — multiplexing, header compression, bidirectional streaming
- **Strongly-typed contract** — `.proto` dosyası ile tanımlı
- **Code generation** — proto'dan otomatik client/server kodu üretilir

### Protocol Buffers (Protobuf)
Google'ın geliştirdiği **dil-bağımsız, platform-bağımsız** serileştirme mekanizmasıdır. JSON'a göre ~3-10x daha küçük payload, ~5-100x daha hızlı serileştirme sağlar.

### Contract-First Design (Sözleşme Öncelikli Tasarım)
Önce API sözleşmesi (`.proto` dosyası) yazılır, sonra bu sözleşmeden kod üretilir. REST'te genellikle code-first yaklaşım kullanılırken, gRPC'de contract-first zorunludur.

### gRPC Reflection (gRPC Yansıma)
Servisin kendini tanımlamasını sağlayan bir mekanizmadır. Test araçları (grpcurl, Postman) proto dosyası olmadan servisi keşfedebilir. **Sadece Development ortamında** aktif edilmelidir.

### Layered Architecture (Katmanlı Mimari)
Klasik Service → Repository → Database katmanlaması. Vertical Slice'a göre daha basit ama büyük projelerde sınırları belirsizleşebilir. **Basit CRUD servisleri** için ideal.

### Kestrel HTTP/2
.NET'in dahili web server'ı olan Kestrel, gRPC için **HTTP/2 protokolünü** destekler. TLS olmadan (plaintext) HTTP/2 kullanmak için `appsettings.json`'da açıkça yapılandırılmalıdır.

---

## 3. Neden Böyle? Mimari Gerekçe

### gRPC vs REST — Neden gRPC?

| Özellik | REST (JSON/HTTP) | gRPC (Protobuf/HTTP2) |
|---------|-------------------|------------------------|
| Payload boyutu | Büyük (text-based) | Küçük (~3-10x) |
| Serileştirme hızı | Yavaş | Hızlı (~5-100x) |
| Tip güvenliği | Yok (runtime) | Var (compile-time) |
| Streaming | Sınırlı | Bidirectional |
| Browser desteği | ✅ Doğal | ❌ gRPC-Web gerekli |
| İnsan tarafından okunabilir | ✅ JSON | ❌ Binary |
| **Kullanım alanı** | **Client → Server** | **Server → Server** |

**Karar:** Discount servisi sadece Basket.API tarafından çağrılacak (inter-service). Browser erişimi gerekmediği için gRPC'nin performans avantajı tercih edildi.

### Layered vs Vertical Slice — Neden Layered?

| Kriter | Vertical Slice | Layered |
|--------|---------------|---------|
| Karmaşıklık | Feature bazlı, her feature bağımsız | Basit, düz yapı |
| Uygunluk | Çok feature'lı, karmaşık domain | Az feature'lı, CRUD-ağırlıklı |
| Öğrenme eğrisi | Orta-yüksek | Düşük |

**Karar:** Discount servisi sadece 4 operasyonlu basit bir CRUD. Vertical Slice + CQRS overkill olurdu. Layered Architecture (Service → DbContext → PostgreSQL) yeterli ve anlaşılır.

### EF Core vs Marten — Neden EF Core?

Catalog'da document-based esneklik için Marten kullandık. Discount'ta ise:
- **Sabit şema** — Coupon tablosu değişmeyecek
- **İlişkisel veri** — Basit tablo yapısı
- **Migration desteği** — EF Core migrations ile şema versiyonlama
- **Öğrenme hedefi** — Projede hem Marten hem EF Core deneyimi kazanmak

### Seed Data Stratejisi — HasData vs SQL Script

| Yöntem | Avantaj | Dezavantaj |
|--------|---------|------------|
| `HasData` (EF Core) | Migration ile birlikte, versiyonlanabilir | Karmaşık veriler için uygun değil |
| SQL Script | Esnek, büyük veri setleri | Migration'dan bağımsız, sync sorunu |

**Karar:** `HasData` — seed data az ve sabit, migration ile birlikte takip edilebilir.

---

## 4. Adım Adım Uygulama

### Adım 1: Proje İskeleti

```bash
# gRPC proje şablonu
dotnet new grpc -n Discount.Grpc -o src/Services/Discount/Discount.Grpc --framework net9.0

# Solution'a ekle
dotnet sln add src/Services/Discount/Discount.Grpc/Discount.Grpc.csproj

# Varsayılan dosyaları temizle
rm src/Services/Discount/Discount.Grpc/Protos/greet.proto
rm src/Services/Discount/Discount.Grpc/Services/GreeterService.cs
```

> `dotnet new grpc` → Grpc.AspNetCore paketi, Protos/ klasörü ve MSBuild ayarları otomatik gelir.

### Adım 2: NuGet Paketleri

```bash
cd src/Services/Discount/Discount.Grpc

# ⚠️ .NET 9 için versiyon belirtmek ZORUNLU — yoksa .NET 10 paketi gelir
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.*
dotnet add package Mapster
dotnet add package Grpc.AspNetCore.Server.Reflection --version 2.*
```

> **Öğrenilen Ders:** NuGet varsayılan olarak en son stable sürümü çeker. `.NET 9` projesi için `--version 9.*` wildcard ile doğru major versiyon hedeflenmeli.

### Adım 3: Proto Dosyası (Contract)

**Dosya:** `Protos/discount.proto`

```protobuf
syntax = "proto3";

option csharp_namespace = "Discount.Grpc";
package discount;

service DiscountProtoService {
  rpc GetDiscount (GetDiscountRequest) returns (CouponModel);
  rpc CreateDiscount (CreateDiscountRequest) returns (CouponModel);
  rpc UpdateDiscount (UpdateDiscountRequest) returns (CouponModel);
  rpc DeleteDiscount (DeleteDiscountRequest) returns (DeleteDiscountResponse);
}

message GetDiscountRequest {
  string product_name = 1;
}

message CreateDiscountRequest {
  string product_name = 1;
  string description = 2;
  int32 amount = 3;
}

message UpdateDiscountRequest {
  int32 id = 1;
  string product_name = 2;
  string description = 3;
  int32 amount = 4;
}

message DeleteDiscountRequest {
  string product_name = 1;
}

message CouponModel {
  int32 id = 1;
  string product_name = 2;
  string description = 3;
  int32 amount = 4;
}

message DeleteDiscountResponse {
  bool success = 1;
}
```

**Proto Dosyası Detayları:**
- `syntax = "proto3"` → Proto3 sürümü (en güncel, sade)
- `option csharp_namespace` → Generate edilen C# kodunun namespace'i
- `package discount` → gRPC servis adreslemesinde kullanılır: `discount.DiscountProtoService`
- Field numaraları (`= 1, = 2`) → Binary encoding'de sıra belirler, **asla değiştirilmemeli**
- `int32 amount` → Para tutarını integer (kuruş/cent) olarak tutuyoruz — floating point hassasiyet sorunlarından kaçınmak için

**csproj ayarı:**
```xml
<Protobuf Include="Protos\discount.proto" GrpcServices="Server" />
```
- `GrpcServices="Server"` → Sadece server-side kod üretir
- Client kodu Basket.API'de `GrpcServices="Client"` ile üretilecek (Bölüm 7)

### Adım 4: Entity Model

**Dosya:** `Models/Coupon.cs`

```csharp
namespace Discount.Grpc.Models;

public sealed class Coupon
{
    public int Id { get; set; }
    public string ProductName { get; set; } = default!;
    public string Description { get; set; } = default!;
    public int Amount { get; set; }
}
```

- `sealed` → SOTA ilkesi: inheritance gerekmediğinde sealed kullan → JIT optimization + accidental inheritance önleme

### Adım 5: DbContext + Seed Data

**Dosya:** `Data/DiscountDbContext.cs`

```csharp
using Discount.Grpc.Models;
using Microsoft.EntityFrameworkCore;

namespace Discount.Grpc.Data;

public sealed class DiscountDbContext(DbContextOptions<DiscountDbContext> options) 
    : DbContext(options)
{
    public DbSet<Coupon> Coupons => Set<Coupon>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.ProductName).IsRequired().HasMaxLength(200);
            entity.Property(c => c.Description).HasMaxLength(500);

            // Seed Data
            entity.HasData(
                new Coupon { Id = 1, ProductName = "IPhone X", Description = "IPhone X Discount", Amount = 150 },
                new Coupon { Id = 2, ProductName = "Samsung 10", Description = "Samsung 10 Discount", Amount = 100 }
            );
        });
    }
}
```

- **Primary Constructor** (`DbContextOptions<DiscountDbContext> options`) → C# 13 feature
- `Set<Coupon>()` → Expression-bodied property, lazy evaluation

### Adım 6: Auto Migration Extension

**Dosya:** `Data/Extensions.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Discount.Grpc.Data;

public static class Extensions
{
    public static async Task<IApplicationBuilder> UseMigrationAsync(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DiscountDbContext>();
        await dbContext.Database.MigrateAsync();
        return app;
    }
}
```

- Development'ta Docker container ayağa kalktığında DB otomatik güncellenir
- **Production'da kapatılır** — migration CI/CD pipeline'da ayrı çalıştırılır

### Adım 7: gRPC Service Implementation

**Dosya:** `Services/DiscountService.cs`

```csharp
using Discount.Grpc.Data;
using Discount.Grpc.Models;
using Grpc.Core;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace Discount.Grpc.Services;

public sealed class DiscountService(DiscountDbContext dbContext, ILogger<DiscountService> logger)
    : DiscountProtoService.DiscountProtoServiceBase
{
    public override async Task<CouponModel> GetDiscount(GetDiscountRequest request, ServerCallContext context)
    {
        var coupon = await dbContext.Coupons
            .FirstOrDefaultAsync(c => c.ProductName == request.ProductName);

        if (coupon is null)
        {
            logger.LogInformation("Discount not found for product: {ProductName}. Returning no discount.", 
                request.ProductName);
            return new CouponModel { ProductName = request.ProductName, Description = "No discount", Amount = 0 };
        }

        logger.LogInformation("Discount retrieved for product: {ProductName}, Amount: {Amount}", 
            coupon.ProductName, coupon.Amount);
        return coupon.Adapt<CouponModel>();
    }

    public override async Task<CouponModel> CreateDiscount(CreateDiscountRequest request, ServerCallContext context)
    {
        var coupon = request.Adapt<Coupon>();

        if (string.IsNullOrEmpty(coupon.ProductName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ProductName is required."));

        dbContext.Coupons.Add(coupon);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Discount created for product: {ProductName}, Amount: {Amount}", 
            coupon.ProductName, coupon.Amount);
        return coupon.Adapt<CouponModel>();
    }

    public override async Task<CouponModel> UpdateDiscount(UpdateDiscountRequest request, ServerCallContext context)
    {
        var coupon = await dbContext.Coupons.FindAsync(request.Id)
            ?? throw new RpcException(new Status(StatusCode.NotFound, 
                $"Discount with Id={request.Id} not found."));

        request.Adapt(coupon);
        dbContext.Coupons.Update(coupon);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Discount updated for product: {ProductName}, Amount: {Amount}", 
            coupon.ProductName, coupon.Amount);
        return coupon.Adapt<CouponModel>();
    }

    public override async Task<DeleteDiscountResponse> DeleteDiscount(
        DeleteDiscountRequest request, ServerCallContext context)
    {
        var coupon = await dbContext.Coupons
            .FirstOrDefaultAsync(c => c.ProductName == request.ProductName)
            ?? throw new RpcException(new Status(StatusCode.NotFound, 
                $"Discount for product '{request.ProductName}' not found."));

        dbContext.Coupons.Remove(coupon);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Discount deleted for product: {ProductName}", request.ProductName);
        return new DeleteDiscountResponse { Success = true };
    }
}
```

**Dikkat Edilecek Noktalar:**
- `DiscountProtoServiceBase` → proto'dan auto-generate edilen base class
- `RpcException` → gRPC dünyasının exception'ı (REST'teki HTTP status code karşılığı)
- `GetDiscount` kupon bulamazsa **exception atmıyor** → amount=0 döndürüyor. Neden? Basket servisi her ürün için discount soracak — kupon yoksa "indirim yok" demek yeterli, hata değil.
- `Mapster Adapt` → Entity ↔ gRPC message mapping tek satırda
- `ServerCallContext` → gRPC metadata, deadline, cancellation gibi bilgileri taşır

### Adım 8: Program.cs (Tüm DI + Middleware)

```csharp
using Discount.Grpc.Data;
using Discount.Grpc.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// DbContext — PostgreSQL
builder.Services.AddDbContext<DiscountDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

// gRPC
builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();  // Test kolaylığı için

var app = builder.Build();

// Auto Migration + Seed
await app.UseMigrationAsync();

// gRPC Service endpoint
app.MapGrpcService<DiscountService>();

// gRPC Reflection — sadece Development'ta
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGet("/", () => "Discount gRPC Service is running.");

app.Run();
```

**Kritik:** `AddGrpcReflection()` DI kaydı olmadan `MapGrpcReflectionService()` çağrılırsa `InvalidOperationException` fırlatılır.

### Adım 9: appsettings.json — Kestrel HTTP/2

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "Database": "Host=localhost;Port=5432;Database=DiscountDb;Username=postgres;Password=postgres"
  },
  "Kestrel": {
    "EndpointDefaults": {
      "Protocols": "Http2"
    }
  }
}
```

**⚠️ Kritik:** `Kestrel.EndpointDefaults.Protocols = "Http2"` olmadan gRPC çalışmaz! TLS olmadan (Docker içinde plaintext) HTTP/2 kullanmak için bu ayar zorunludur.

### Adım 10: Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Services/Discount/Discount.Grpc/Discount.Grpc.csproj Services/Discount/Discount.Grpc/
RUN dotnet restore Services/Discount/Discount.Grpc/Discount.Grpc.csproj

COPY src/Services/Discount/ Services/Discount/
RUN dotnet publish Services/Discount/Discount.Grpc/Discount.Grpc.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Discount.Grpc.dll"]
```

**Öğrenilen Ders:** `--no-restore` flag'i `Microsoft.EntityFrameworkCore.Design` paketinin Roslyn analyzer'ları ile uyumsuzluk yaratabilir. Bu paketi içeren projelerde `--no-restore` kullanmamak daha güvenli.

### Adım 11: Docker Compose'a Ekleme

```yaml
discount-grpc:
  image: discount-grpc:latest
  container_name: discount-grpc
  build:
    context: ..
    dockerfile: src/Services/Discount/Discount.Grpc/Dockerfile
  environment:
    - ASPNETCORE_ENVIRONMENT=Development
    - ConnectionStrings__Database=Host=postgres;Port=5432;Database=DiscountDb;Username=postgres;Password=postgres
  ports:
    - "5003:8080"
  depends_on:
    postgres:
      condition: service_healthy
  networks:
    - eshop-network
  restart: unless-stopped
```

**init-databases.sql güncelleme:**
```sql
CREATE DATABASE "CatalogDb";
CREATE DATABASE "DiscountDb";
```

> **⚠️** `init-databases.sql` sadece PostgreSQL volume **ilk kez** oluşturulduğunda çalışır. Yeni DB eklendiğinde `docker compose down -v` ile volume sıfırlanmalı.

### Adım 12: gRPC Test (grpcurl)

```bash
# grpcurl kurulum
curl -L -o /tmp/grpcurl.tar.gz \
  https://github.com/fullstorydev/grpcurl/releases/download/v1.9.3/grpcurl_1.9.3_linux_x86_64.tar.gz
tar xzf /tmp/grpcurl.tar.gz -C /tmp
sudo mv /tmp/grpcurl /usr/local/bin/

# Servis listesi (Reflection test)
grpcurl -plaintext localhost:5003 list

# GetDiscount — Seed data
grpcurl -plaintext -d '{"product_name": "IPhone X"}' \
  localhost:5003 discount.DiscountProtoService/GetDiscount

# CreateDiscount
grpcurl -plaintext -d '{"product_name": "Huawei P40", "description": "Huawei Discount", "amount": 80}' \
  localhost:5003 discount.DiscountProtoService/CreateDiscount

# Olmayan ürün — amount=0 dönmeli
grpcurl -plaintext -d '{"product_name": "NonExistent"}' \
  localhost:5003 discount.DiscountProtoService/GetDiscount
```

---

## 5. Kontrol Listesi

- [x] `dotnet build` başarılı
- [x] Docker Compose'da `discount-grpc` container healthy
- [x] PostgreSQL'de `DiscountDb` veritabanı oluştu
- [x] EF Core migration otomatik uygulandı
- [x] Seed data (IPhone X, Samsung 10) veritabanında mevcut
- [x] gRPC Reflection çalışıyor (`grpcurl list`)
- [x] GetDiscount — var olan kupon → amount döner
- [x] GetDiscount — olmayan kupon → amount=0 döner
- [x] CreateDiscount — yeni kupon oluşturulur
- [x] UpdateDiscount — mevcut kupon güncellenir
- [x] DeleteDiscount — kupon silinir

---

## 6. Sık Hatalar & Çözümleri

### Hata 1: NuGet Paket Versiyon Uyumsuzluğu
```
error: NU1202: Package Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 
is not compatible with net9.0
```
**Sebep:** NuGet varsayılan olarak en son stable sürümü çeker. `10.x` → `.NET 10` hedefliyor.
**Çözüm:** `dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.*`

### Hata 2: --no-restore ile NETSDK1064
```
error NETSDK1064: Package Microsoft.CodeAnalysis.Analyzers, version 3.3.4 was not found.
```
**Sebep:** `Microsoft.EntityFrameworkCore.Design` paketi Roslyn analyzer'ları çeker. `--no-restore` ile publish'te bu analyzer'lar bulunamaz.
**Çözüm:** Dockerfile'da `--no-restore` flag'ini kaldır.

### Hata 3: gRPC Reflection — AddGrpcReflection() Eksik
```
System.InvalidOperationException: Unable to find the required services. 
Please add all the required services by calling 'IServiceCollection.AddGrpcReflection()'
```
**Sebep:** `MapGrpcReflectionService()` çağrıldı ama DI kaydı yapılmadı.
**Çözüm:** `builder.Services.AddGrpcReflection();` ekle.

### Hata 4: gRPC Bağlantı Timeout
```
Failed to dial target host "localhost:5003": context deadline exceeded
```
**Sebep:** Kestrel varsayılan olarak HTTP/1.1 kullanır. gRPC HTTP/2 gerektirir.
**Çözüm:** `appsettings.json`'a Kestrel HTTP/2 yapılandırması ekle:
```json
"Kestrel": {
  "EndpointDefaults": {
    "Protocols": "Http2"
  }
}
```

### Hata 5: init-databases.sql Çalışmıyor
**Sebep:** PostgreSQL `docker-entrypoint-initdb.d/` scriptleri sadece volume **ilk oluşturulduğunda** çalıştırır.
**Çözüm:** `docker compose down -v` ile volume'ları sil, yeniden başlat.

---

## 7. Best Practices

### gRPC Özel
- **Proto field numaralarını asla değiştirme** — backward compatibility için kritik
- **GetDiscount'ta exception atma, amount=0 döndür** — inter-service call'da "kupon yok" bir hata değil, normal bir durum
- **Reflection sadece Development'ta** — Production'da servis keşfini kapatmak güvenlik açısından önemli
- **Kestrel HTTP/2 yapılandırmasını unutma** — TLS olmadan gRPC çalışmaz

### EF Core Özel
- **Auto Migration sadece Development'ta** — Production'da CI/CD pipeline kullan
- **Seed data az ve sabit ise `HasData` kullan** — migration ile versiyonlanır
- **DbContext'i `sealed` yap** — override gerekmediğinde performans kazancı

### Docker Özel
- **`--no-restore` dikkatli kullan** — analyzer paketleri ile uyumsuzluk olabilir
- **Her servis kendi Dockerfile'ına sahip** — sadece ilgili dosyaları kopyala
- **Volume lifecycle'ı anla** — init script'ler sadece ilk oluşturmada çalışır

---

## 8. TODO / Tartışma Notları

- [ ] **Bölüm 7'de yapılacak:** Basket.API → Discount.Grpc gRPC client bağlantısı
- [ ] **Health Check:** gRPC Health Check Protocol eklenecek (şu an sadece REST health check var)
- [ ] **Validation:** FluentValidation eklenebilir (şu an sadece null check var)
- [ ] **Caching:** Sık sorgulanan kuponlar için in-memory cache eklenebilir
- [ ] **Pagination:** `GetAllDiscounts` endpoint'i eklenebilir
- [ ] **Audit logging:** Kupon oluşturma/silme işlemleri audit log'a yazılabilir

---

## 9. Kısa Özet (Summary)

Bu bölümde **Discount.Grpc** mikroservisini sıfırdan oluşturduk. Protocol Buffers ile contract-first API tasarımı yaparak, EF Core + PostgreSQL ile basit ama sağlam bir veritabanı katmanı kurduk. Servisi Docker Compose'a entegre edip, grpcurl ile tüm CRUD operasyonlarını (GetDiscount, CreateDiscount, UpdateDiscount, DeleteDiscount) başarıyla test ettik. Layered Architecture'ın basit servisler için neden uygun olduğunu, gRPC'nin inter-service iletişimdeki performans avantajını ve Kestrel HTTP/2 yapılandırmasının kritik önemini öğrendik.

---

## 10. Ne Öğrendim? (What I Learned) — 3 Madde

1. **gRPC contract-first tasarım zorunluluğu**: `.proto` dosyası tüm API'nin kaynağıdır — field numaraları, mesaj yapıları ve servis tanımları burada belirlenir. Proto'dan C# kodu otomatik generate edilir ve bu generate edilen base class override edilerek iş mantığı yazılır.

2. **Kestrel HTTP/2 yapılandırması kritik**: gRPC HTTP/2 gerektirir ama Kestrel varsayılan olarak HTTP/1.1 kullanır. TLS olmadan (Docker/Development ortamında) HTTP/2 kullanmak için `appsettings.json`'da açıkça `"Protocols": "Http2"` ayarlanmalıdır — aksi halde gRPC bağlantısı timeout ile başarısız olur.

3. **Mimari seçimi domain karmaşıklığına göre yapılır**: Catalog gibi çok feature'lı servisler Vertical Slice + CQRS hak eder. Ama Discount gibi 4 operasyonlu basit bir CRUD için Layered Architecture yeterlidir — overengineering'den kaçınmak da bir SOTA pratiğidir.

---

## 11. Öğrenme Pekiştirme (Reinforcement)

### Mini Quiz (10 soru)

**S1 (Doğru/Yanlış):** gRPC JSON formatı kullanarak veri aktarımı yapar.

**S2 (Kısa Cevap):** `.proto` dosyasındaki field numaraları (`= 1, = 2`) ne işe yarar ve neden değiştirilmemelidir?

**S3 (Doğru/Yanlış):** gRPC servisleri HTTP/1.1 üzerinde çalışabilir.

**S4 (Senaryo):** Basket.API bir ürün için discount sorgular ama o ürüne ait kupon yoktur. Servis ne yapmalıdır — RpcException mı atmalı, yoksa amount=0 mü döndürmeli? Neden?

**S5 (Kısa Cevap):** `GrpcServices="Server"` ve `GrpcServices="Client"` arasındaki fark nedir?

**S6 (Doğru/Yanlış):** `dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL` komutu her zaman .NET 9 uyumlu sürümü yükler.

**S7 (Kısa Cevap):** gRPC Reflection nedir ve neden sadece Development ortamında aktif edilmelidir?

**S8 (Senaryo):** Docker Compose'da yeni bir PostgreSQL veritabanı (`DiscountDb`) eklenmesi gerekiyor. `init-databases.sql`'e ekleme yaptınız ama DB oluşmadı. Neden?

**S9 (Kısa Cevap):** Discount servisi için Vertical Slice + CQRS yerine neden Layered Architecture tercih edildi?

**S10 (Doğru/Yanlış):** Mapster'ın `Adapt<T>()` metodu ile Entity ↔ gRPC message mapping yapılabilir.

---

### Görevler (3 adet)

**Görev 1 — GetAllDiscounts Endpoint Ekleme:**
Proto dosyasına `GetAllDiscounts` RPC'si ekleyin. Request boş (`google.protobuf.Empty` veya custom boş mesaj), response bir `CouponList` mesajı (repeated CouponModel) dönsün. Service'te implementasyonunu yazın ve grpcurl ile test edin.
**Beklenen çıktı:** Tüm kuponların listesi JSON formatında döner.

**Görev 2 — Validation Güçlendirme:**
`CreateDiscount`'ta sadece `ProductName` kontrolü var. `Amount` değerinin 0'dan büyük olmasını ve `Description`'ın boş olmamasını da kontrol edin. Her hata için uygun `StatusCode` ile `RpcException` fırlatın.
**Beklenen çıktı:** `amount: -5` ile çağrıldığında `INVALID_ARGUMENT` hatası.

**Görev 3 — Duplicate Kontrolü:**
`CreateDiscount`'ta aynı `ProductName` ile ikinci bir kupon oluşturulmasını engelleyin. Zaten varsa `StatusCode.AlreadyExists` döndürün.
**Beklenen çıktı:** Aynı ürün adıyla ikinci kez çağrıldığında `ALREADY_EXISTS` hatası.

---

### Cevap Anahtarı

**S1:** Yanlış. gRPC Protocol Buffers (binary format) kullanır, JSON değil.

**S2:** Field numaraları binary encoding'de veriyi tanımlamak için kullanılır. Değiştirilirse eski client/server'lar veriyi yanlış parse eder — backward compatibility bozulur.

**S3:** Yanlış. gRPC HTTP/2 gerektirir. HTTP/1.1 multiplexing ve bidirectional streaming desteklemediği için gRPC çalışmaz.

**S4:** Amount=0 döndürmelidir. "Kupon yok" bir hata değil, normal bir iş durumudur. Basket servisi her ürün için sorgulayacak — exception fırlatmak gereksiz overhead ve error handling karmaşıklığı yaratır.

**S5:** `Server` → Sadece server-side kod üretir (service base class). `Client` → Sadece client-side kod üretir (stub/proxy class). Discount.Grpc'de Server, Basket.API'de Client kullanılacak.

**S6:** Yanlış. NuGet varsayılan olarak en son stable sürümü çeker. .NET 10 paketi .NET 9 projesiyle uyumsuzdur. `--version 9.*` kullanılmalı.

**S7:** Reflection, servisin proto tanımını runtime'da paylaşmasını sağlar. Test araçları (grpcurl, Postman) proto dosyası olmadan servis keşfedebilir. Production'da güvenlik riski oluşturur — servis yapısını dışarıya açar.

**S8:** PostgreSQL'in `docker-entrypoint-initdb.d/` scriptleri sadece volume ilk oluşturulduğunda çalışır. Mevcut volume varsa script atlanır. `docker compose down -v` ile volume silinip yeniden oluşturulmalı.

**S9:** Discount servisi sadece 4 CRUD operasyonundan oluşan basit bir servis. Vertical Slice + CQRS ek klasör yapısı, handler'lar, pipeline behavior'lar gerektirir — bu karmaşıklık Discount için overkill. Layered Architecture (Service → DbContext → DB) basit, anlaşılır ve yeterli.

**S10:** Doğru. Mapster'ın `Adapt<T>()` metodu property name matching ile Entity'yi gRPC message'a (ve tersine) otomatik map eder.

---

## 📂 Dosya Yapısı (Son Durum)

```
src/Services/Discount/
└── Discount.Grpc/
    ├── Data/
    │   ├── DiscountDbContext.cs          # EF Core DbContext + Seed Data
    │   ├── Extensions.cs                # Auto Migration extension
    │   └── Migrations/
    │       ├── 20260213155941_InitialCreate.cs
    │       └── DiscountDbContextModelSnapshot.cs
    ├── Models/
    │   └── Coupon.cs                    # Entity model
    ├── Protos/
    │   └── discount.proto               # gRPC contract
    ├── Services/
    │   └── DiscountService.cs           # gRPC service implementation
    ├── Program.cs                       # DI + Middleware
    ├── appsettings.json                 # Connection string + Kestrel HTTP/2
    ├── Discount.Grpc.csproj             # Proto build ayarları + paketler
    └── Dockerfile                       # Multi-stage build
```

---

## 🐳 Docker Compose Port Haritası (Güncel)

| Servis | Internal Port | External Port | Durum |
|--------|--------------|---------------|-------|
| PostgreSQL | 5432 | 5432 | ✅ Healthy |
| Redis | 6379 | 6379 | ✅ Healthy |
| Catalog.API | 8080 | 5001 | ✅ Running |
| Basket.API | 8080 | 5002 | ✅ Running |
| **Discount.Grpc** | **8080** | **5003** | ✅ **Running** |

---

*Bölüm 6 Tamamlandı ✅ — Sonraki: Bölüm 7 — Basket → Discount gRPC İletişimi*
