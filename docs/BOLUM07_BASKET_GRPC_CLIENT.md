# Bölüm 7: Basket → Discount gRPC Client Entegrasyonu

> **Tarih:** 2026-03-08  
> **Ön Koşul:** Bölüm 6 (Discount.Grpc çalışır durumda)  
> **Konu:** Basket.API'ye gRPC client ekleyerek Discount.Grpc ile servisler arası senkron iletişim kurmak

---

## 1. Amaç & Kazanımlar

- Basket.API'ye **gRPC client** ekleyerek Discount.Grpc servisinden indirim sorgulamak
- Proto dosyasını client tarafında `GrpcServices="Client"` olarak kaydetmek ve **compile-time güvenli** stub üretmek
- `StoreBasketHandler`'da sepet kaydedilirken **otomatik indirim uygulama** akışını kurmak
- **.NET gRPC Client Factory** (`AddGrpcClient<T>`) ile DI-friendly client kaydı yapmak
- Docker DNS ile **service discovery** yapılandırmasını hazırlamak

---

## 2. Kavramlar & Tanımlar

### Uçtan Uca Akış

```
Kullanıcı: POST /basket (sepete ürün ekle)
    │
    ▼
Basket.API — StoreBasketHandler
    │
    │ (1) Her sepet kalemi için:
    │     ├── gRPC çağrısı → Discount.Grpc
    │     │                    → GetDiscount(productName)
    │     │                    ← CouponModel { amount: 150 }
    │     │
    │     └── item.Price -= discount.Amount
    │
    │ (2) İndirimli sepeti kaydet:
    │     ├── Redis (CachedBasketRepository — L1+L2 HybridCache)
    │     └── PostgreSQL/Marten (BasketRepository)
    │
    ▼
Kullanıcı: 200 OK { userName: "hamza" }
```

**Somut örnek:**
```
Sepet: IPhone X — Fiyat: 950
Discount.Grpc: IPhone X indirimi → 150
Sonuç: IPhone X — Fiyat: 800 (950 - 150)
```

### gRPC Client Factory

.NET'in `Microsoft.Extensions.Http` altyapısını kullanan, DI-friendly gRPC client yönetimi. `HttpClientFactory` mantığıyla çalışır.

| Yaklaşım | Sorun |
|----------|-------|
| `new GrpcChannel()` + `new Client()` | Manuel yaşam döngüsü, connection leak riski |
| `AddSingleton<Client>()` | HTTP/2 connection stale olabilir, DNS değişikliklerini yakalamaz |
| **`AddGrpcClient<T>()`** | HttpClientFactory yönetir: connection pooling, DNS refresh, retry policy eklenebilir |

### Proto Dosyası Paylaşım Modeli

```
Discount.Grpc/                    Basket.API/
  Protos/                           Protos/
    discount.proto ──kopyala──→       discount.proto
  .csproj:                          .csproj:
    GrpcServices="Server"             GrpcServices="Client"
         │                                  │
         ▼                                  ▼
  DiscountProtoServiceBase          DiscountProtoServiceClient
  (override et)                     (inject et, çağır)
```

---

## 3. Neden Böyle? Mimari Gerekçe

### Neden Sepet Kaydederken İndirim Sorguluyoruz?

| Alternatif | Sorun |
|-----------|-------|
| Frontend indirim sorgulasın, indirimli fiyatı göndersin | Güvenlik riski — client fiyatı manipüle edebilir |
| Checkout sırasında sorgula | Kullanıcı sepette yanlış fiyat görür |
| **StoreBasket sırasında sorgula** | Fiyat her zaman güncel, server-side güvenli |

### Tight Coupling Riski (Senkron İletişim)

```
Basket.API ──gRPC──→ Discount.Grpc
    ↑                      ↑
    │                      │
    └── Discount kapalıysa Basket da hata verir!
```

Bu bölümde Discount kapalıysa `StoreBasket` **500 Internal Server Error** döner. Gelecek çözümler:

- **Polly retry + circuit breaker** — Geçici hatalarda tekrar dene, sürekli hatada hızlı fail
- **Fallback** — Discount servisine ulaşılamazsa `Amount = 0` varsay
- **Timeout** — 3 saniyede cevap gelmezse devam et

### Neden Her Ürün İçin Ayrı gRPC Çağrısı?

| Alternatif | Trade-off |
|-----------|-----------|
| **Tek tek çağrı (mevcut)** | Basit, N adet çağrı |
| Batch RPC (`GetDiscounts(repeated string)`) | Tek çağrı ama proto değişmeli |
| Client streaming | Karmaşık, overkill |
| Client-side cache | Stale data riski |

Mevcut yaklaşım öğrenme amaçlı basit tutuldu. Production'da batch RPC veya client-side caching daha performanslı.

---

## 4. Adım Adım Uygulama

### 4.1 Proto Dosyasını Basket.API'ye Kopyala

```bash
cd ~/sota-eshop-microservices/src/Services/Basket/Basket.API
mkdir -p Protos
cp ~/sota-eshop-microservices/src/Services/Discount/Discount.Grpc/Protos/discount.proto Protos/
```

**Kontrol:** İki serviste de aynı `discount.proto` — biri Server, diğeri Client olarak kaydedilecek.

### 4.2 NuGet Paketleri Ekle

```bash
dotnet add package Grpc.Net.ClientFactory
dotnet add package Google.Protobuf
dotnet add package Grpc.Tools
```

| Paket | Görev |
|-------|-------|
| `Grpc.Net.ClientFactory` | `AddGrpcClient<T>()` extension metodu — DI entegrasyonu |
| `Google.Protobuf` | Protobuf serialization runtime |
| `Grpc.Tools` | Build sırasında `.proto` → C# code generation |

### 4.3 .csproj'a Proto Referansı Ekle

`Basket.API.csproj`'a şu `ItemGroup`'u ekle:

```xml
<ItemGroup>
  <Protobuf Include="Protos\discount.proto" GrpcServices="Client" />
</ItemGroup>
```

**Kritik fark:**
- `GrpcServices="Server"` → `DiscountProtoServiceBase` üretir (Discount.Grpc tarafı)
- `GrpcServices="Client"` → `DiscountProtoServiceClient` üretir (Basket.API tarafı)

**Build ile doğrula:**

```bash
dotnet build
```

Generated dosyalar:
- `obj/Debug/net9.0/Protos/Discount.cs` → Protobuf message class'ları
- `obj/Debug/net9.0/Protos/DiscountGrpc.cs` → `DiscountProtoServiceClient` stub

### 4.4 appsettings.json — GrpcSettings Ekle

```json
"GrpcSettings": {
  "DiscountUrl": "http://localhost:5183"
}
```

**Neden ayrı setting?**
- Local dev: `http://localhost:5183` (Discount.Grpc'nin launchSettings portu)
- Docker Compose: `http://discount-grpc:8080` (environment variable override ile)

### 4.5 Program.cs — gRPC Client Kaydı

`AddScoped<IBasketRepository>` satırından **hemen önce** eklendi:

```csharp
// --- gRPC Client: Discount Service ---
builder.Services.AddGrpcClient<Discount.Grpc.DiscountProtoService.DiscountProtoServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcSettings:DiscountUrl"]
        ?? throw new InvalidOperationException("Discount gRPC URL is required."));
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    // Development: HTTP/2 cleartext (h2c) — TLS olmadan gRPC
    // Production'da bu handler KALDIRILMALI, gerçek TLS sertifikası kullanılmalı
    return new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };
});
```

| Satır | Açıklama |
|-------|----------|
| `AddGrpcClient<T>()` | DI'a gRPC client factory kaydı |
| `options.Address` | appsettings'ten `DiscountUrl` okur |
| `ConfigurePrimaryHttpMessageHandler` | Dev ortamında TLS doğrulamasını atlar (h2c) |
| `DangerousAcceptAnyServerCertificateValidator` | **Sadece development!** Production'da gerçek sertifika şart |

### 4.6 StoreBasketHandler — İndirim Entegrasyonu

**Güncellenen dosya:** `Features/StoreBasket/StoreBasketHandler.cs`

```csharp
namespace Basket.API.Features.StoreBasket;

using Basket.API.Data;
using Basket.API.Models;
using BuildingBlocks.CQRS;
using BuildingBlocks.Results;
using Discount.Grpc;

public sealed class StoreBasketHandler(
    IBasketRepository repository,
    DiscountProtoService.DiscountProtoServiceClient discountClient)
    : ICommandHandler<StoreBasketCommand, StoreBasketResponse>
{
    public async Task<Result<StoreBasketResponse>> Handle(
        StoreBasketCommand command, CancellationToken cancellationToken)
    {
        // 1. Her sepet kalemi için Discount servisinden indirim sorgula
        await ApplyDiscountsAsync(command.Cart.Items, cancellationToken);

        // 2. İndirimli sepeti kaydet (CachedBasketRepository → BasketRepository)
        var basket = await repository.StoreBasketAsync(command.Cart, cancellationToken);

        return Result<StoreBasketResponse>.Success(new StoreBasketResponse(basket.UserName));
    }

    /// <summary>
    /// Her sepet kalemine Discount.Grpc üzerinden indirim uygular.
    /// İndirim yoksa Amount=0 döner, fiyat değişmez.
    /// </summary>
    private async Task ApplyDiscountsAsync(
        List<ShoppingCartItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            var discount = await discountClient.GetDiscountAsync(
                new GetDiscountRequest { ProductName = item.ProductName },
                cancellationToken: cancellationToken);

            // Negatif fiyat koruması
            item.Price -= discount.Amount;
            if (item.Price < 0) item.Price = 0;
        }
    }
}
```

**Yapılan değişiklikler:**

| Değişiklik | Açıklama |
|-----------|----------|
| `DiscountProtoServiceClient discountClient` | Primary constructor'a eklendi — DI inject eder |
| `ApplyDiscountsAsync()` | Her ürün için gRPC çağrısı, indirimi fiyattan düşer |
| `cancellationToken` geçirme | İstek iptal olursa gRPC çağrısı da iptal olur |
| Negatif fiyat koruması | `discount.Amount > item.Price` → fiyat 0'a düşer, eksiye gitmez |

---

## 5. Kontrol Listesi

- [x] Proto dosyası Basket.API/Protos/ altına kopyalandı
- [x] NuGet paketleri eklendi (Grpc.Net.ClientFactory, Google.Protobuf, Grpc.Tools)
- [x] `.csproj`'a `Protobuf GrpcServices="Client"` kaydedildi
- [x] `obj/` altında `Discount.cs` ve `DiscountGrpc.cs` generate edildi
- [x] `appsettings.json`'a `GrpcSettings.DiscountUrl` eklendi
- [x] `Program.cs`'te `AddGrpcClient<T>()` kaydı yapıldı
- [x] `StoreBasketHandler`'a `discountClient` inject edildi + `ApplyDiscountsAsync` eklendi
- [x] `dotnet build` succeeded (tüm projeler)
- [x] Uçtan uca test: IPhone X 950 → 800 (150 indirim uygulandı)
- [x] Uçtan uca test: Huawei Plus 500 → 500 (DB'de yok, Amount=0, fiyat değişmedi)
- [x] TotalPrice doğru hesaplandı (800×1 + 500×2 = 1800)

---

## 6. Sık Hatalar & Çözümleri

| Hata | Neden | Çözüm |
|------|-------|-------|
| `DiscountProtoService not found` | `using Discount.Grpc;` eksik veya proto build yapılmamış | `using` ekle + `dotnet build` |
| `RpcException: StatusCode.Unavailable` | Discount.Grpc kapalı veya yanlış port | Servisi başlat, port'u doğrula |
| `InvalidOperationException: Discount gRPC URL is required` | `appsettings.json`'da `GrpcSettings` bloğu eksik | Setting'i ekle |
| İndirim uygulanmıyor (fiyat değişmez) | `ProductName` case-sensitive eşleşme hatası | Seed data ile aynı yazım olmalı: `"IPhone X"` |
| Port uyuşmazlığı | Discount.Grpc farklı portta çalışıyor | `launchSettings.json` portunu kontrol et, `appsettings.json`'u güncelle |
| Docker Compose'da `docker compose up -d` çalışmıyor | `docker-compose.yml` farklı dizinde | `cd ~/sota-eshop-microservices/docker` dizininden çalıştır |

---

## 7. Best Practices

1. **gRPC URL'ini configuration'dan al** — Hardcode etme, `appsettings.json` + environment variable override kullan
2. **`DangerousAcceptAnyServerCertificateValidator` sadece development** — Production'da gerçek TLS sertifikası şart
3. **İndirim mantığını ayrı metoda al** — `ApplyDiscountsAsync` handler'ı temiz tutar
4. **`cancellationToken` her zaman geçir** — gRPC çağrısı iptal edilebilir olmalı
5. **Negatif fiyat koruması ekle** — `Math.Max(0, ...)` veya `if (price < 0) price = 0`
6. **Proto dosyalarını senkron tut** — Server ve client aynı `.proto` kullanmalı, CI/CD'de kontrol ekle

---

## 8. TODO / Tartışma Notları

- [ ] **Polly resilience** — Retry (3x exponential backoff), circuit breaker (5 hata → 30s açık), timeout (3s)
- [ ] **Fallback strategy** — Discount kapalıyken `Amount = 0` varsay, logla ve devam et
- [ ] **Çift indirim riski** — Aynı sepet iki kez StoreBasket ile güncellenirse indirim ikinci kez uygulanır (950→800→650). Çözüm: `originalPrice` alanı ekle
- [ ] **Batch discount query** — Proto'ya `GetDiscounts(repeated string)` ekle, tek çağrıda birçok ürün sorgula
- [ ] **Docker Compose service discovery** — `DiscountUrl` environment variable ile `http://discount-grpc:8080` olacak (Bölüm 8)
- [ ] **Distributed tracing** — OpenTelemetry ile Basket → Discount çağrı zincirini izle

---

## 9. Kısa Özet (Summary)

Bölüm 7 ile Basket.API ve Discount.Grpc arasında gRPC tabanlı senkron iletişim kuruldu. Proto dosyası client tarafına kopyalanıp `GrpcServices="Client"` olarak kaydedildi, `AddGrpcClient<T>()` ile DI'a eklendi. StoreBasketHandler'da her sepet kalemi için `GetDiscountAsync()` çağrılarak indirim miktarı sorgulanıyor ve `item.Price -= discount.Amount` ile fiyattan düşülüyor. İndirim yoksa Amount=0 dönerek fiyat değişmeden kalıyor. Akış: Kullanıcı → Basket.API → gRPC → Discount.Grpc → PostgreSQL → CouponModel → handler'da indirim uygula → Redis + PostgreSQL'e kaydet.

---

## 10. Ne Öğrendim? (What I Learned) — 3 Madde

1. **gRPC Client Factory** (`AddGrpcClient<T>()`) ile DI container'a gRPC client kaydedilir ve handler'lara primary constructor injection ile verilir — connection pooling, DNS refresh ve lifecycle management otomatik yönetilir, manuel channel oluşturmaya gerek kalmaz.

2. **Proto dosyası paylaşımı** ile client tarafında `GrpcServices="Client"` kaydı yapıldığında build sırasında `DiscountProtoServiceClient` stub class'ı üretilir — `GetDiscountAsync()` gibi strongly-typed, compile-time güvenli metod çağrıları yapılabilir.

3. **Senkron gRPC iletişiminin en büyük riski tight coupling'dir** — Discount servisi kapalıyken Basket.API hata verir; bu risk Polly (retry + circuit breaker + fallback) ile azaltılabilir ama tamamen ortadan kaldırılamaz, bu yüzden kritik olmayan akışlarda asenkron iletişim (RabbitMQ) tercih edilir.

---

## 11. Öğrenme Pekiştirme (Reinforcement)

### Mini Quiz (10 soru)

**S1:** `.csproj`'taki `GrpcServices` attribute'ünün `"Server"` ve `"Client"` değerleri arasındaki fark nedir? Her biri ne üretir?

**S2:** `AddGrpcClient<T>()` kullanmak yerine `new GrpcChannel() + new Client()` ile manuel client oluşturmanın riski nedir?

**S3:** Basket.API'deki `StoreBasketHandler`'da indirim sorgulaması hangi aşamada yapılıyor ve neden bu aşamada?

**S4:** Discount.Grpc'de bir ürün için kupon bulunamadığında ne döner? Bu Basket.API tarafında nasıl ele alınır?

**S5:** `DangerousAcceptAnyServerCertificateValidator` ne yapar? Production'da kullanılabilir mi?

**S6:** Aynı sepet iki kez `StoreBasket` ile güncellenirse IPhone X fiyatı ne olur? (Başlangıç: 950, indirim: 150)

**S7:** `cancellationToken`'ı gRPC çağrısına geçirmezseniz ne olur?

**S8:** Docker Compose ortamında `DiscountUrl` değeri ne olmalı? Local dev'den farkı nedir?

**S9:** `ApplyDiscountsAsync` metodunda 5 ürünlü sepet için kaç gRPC çağrısı yapılır? Bunu azaltmanın yolu nedir?

**S10:** Proto dosyasındaki `product_name` (snake_case) alanı C#'ta hangi isme dönüşür? Bu dönüşümü kim yapar?

---

### Cevap Anahtarı

**C1:** `GrpcServices="Server"` → `DiscountProtoServiceBase` abstract class üretir (override ederek service logic yazarsın). `GrpcServices="Client"` → `DiscountProtoServiceClient` concrete class üretir (DI ile inject edip `GetDiscountAsync()` gibi metotları çağırırsın).

**C2:** Connection leak riski — `GrpcChannel` IDisposable, yönetilmezse socket tükenir. Ayrıca DNS değişikliklerini yakalayamaz ve HTTP/2 connection stale olabilir. `AddGrpcClient<T>()` bunları HttpClientFactory altyapısı ile otomatik yönetir.

**C3:** `StoreBasket` (sepet kaydetme) aşamasında, `repository.StoreBasketAsync()` çağrılmadan **önce** yapılıyor. Neden: Server-side fiyat kontrolü sağlar — client fiyatı manipüle edemez, kullanıcı sepette doğru (indirimli) fiyatı görür.

**C4:** Discount.Grpc `Amount = 0` döner (Bölüm 6'daki no-discount fallback). Basket.API'de `item.Price -= 0` → fiyat değişmez, hata fırlatılmaz, akış normal devam eder.

**C5:** HTTP handler'da TLS sertifika doğrulamasını tamamen devre dışı bırakır — self-signed veya geçersiz sertifikaları kabul eder. **Production'da kesinlikle kullanılmamalı** — man-in-the-middle saldırısına açık hale getirir.

**C6:** İlk StoreBasket: 950 - 150 = 800. İkinci StoreBasket: 800 - 150 = **650** (çift indirim!). Çözüm: Client her zaman orijinal fiyatı göndermeli veya sepette `originalPrice` alanı ayrı tutulmalı.

**C7:** Kullanıcı HTTP isteğini iptal etse bile gRPC çağrısı çalışmaya devam eder — Discount.Grpc gereksiz iş yapar, Basket.API gereksiz kaynak tüketir. Best practice: her zaman `cancellationToken` geçirmek.

**C8:** Docker Compose: `http://discount-grpc:8080` — Docker DNS container adını resolve eder, internal port 8080. Local dev: `http://localhost:5183` — launchSettings portu. Fark: environment variable override (`GrpcSettings__DiscountUrl`) ile Docker Compose'da otomatik değişir.

**C9:** **5 gRPC çağrısı** — her ürün için ayrı `GetDiscountAsync()`. Azaltma yolu: Proto'ya `GetDiscounts(repeated string product_names)` batch RPC eklemek → tek çağrıda tüm ürünler sorgulanır.

**C10:** `product_name` → `ProductName` (PascalCase). Bu dönüşümü **Protobuf code generator** (Grpc.Tools) yapar — proto'daki snake_case alanları C#'ta PascalCase property'lere çevirir. Bu Protobuf C# code generation'ın standart davranışıdır.

---

### Görevler

**Görev 1 — Yeni İndirim Ekle ve Test Et:**
1. Discount.Grpc'ye grpcurl ile yeni kupon ekle:
```bash
grpcurl -plaintext -d "{\"coupon\": {\"product_name\": \"MacBook Pro\", \"description\": \"MacBook Discount\", \"amount\": 200}}" localhost:5183 discount.DiscountProtoService/CreateDiscount
```
2. Basket.API'den "MacBook Pro" ile StoreBasket çağır (fiyat: 1500)
3. GetBasket ile fiyatın 1300 (1500 - 200) olduğunu doğrula

**Görev 2 — Discount Servisi Kapalıyken Test:**
1. Discount.Grpc'yi durdur (Ctrl+C)
2. StoreBasket çağır
3. Hata mesajını incele — ne tür exception alıyorsun?
4. Discount.Grpc'yi yeniden başlat
5. StoreBasket tekrar çağır — çalışıyor mu?

**Görev 3 — Samsung 10 Testi:**
1. Seed data'da Samsung 10 var mı kontrol et:
```bash
grpcurl -plaintext -d "{\"product_name\": \"Samsung 10\"}" localhost:5183 discount.DiscountProtoService/GetDiscount
```
2. Varsa: 3 ürünlü sepet oluştur (IPhone X + Samsung 10 + bilinmeyen ürün), tüm fiyatları ve totalPrice'ı doğrula.

---

## 12. Mimari Harita (Bölüm 7 Sonrası)

```
                    ┌──────────────┐
                    │  Kullanıcı   │
                    └──────┬───────┘
                           │ REST (HTTP)
              ┌────────────┼────────────┐
              ▼            ▼            ▼
      ┌──────────┐  ┌──────────┐  (gelecek)
      │Catalog   │  │Basket    │  ┌──────────┐
      │API       │  │API       │  │Ordering  │
      │:5118     │  │:5119     │  │API       │
      └────┬─────┘  └──┬───┬──┘  └──────────┘
           │            │   │
           │            │   │ gRPC (HTTP/2)  ← YENİ
           │            │   │
           │            │   ▼
           │            │  ┌──────────┐
           │            │  │Discount  │
           │            │  │Grpc      │
           │            │  │:5183     │
           │            │  └────┬─────┘
           │            │       │
           ▼            ▼       ▼
      ┌─────────┐  ┌───────┐  ┌─────────┐
      │PostgreSQL│  │ Redis │  │PostgreSQL│
      │CatalogDb │  │:6379  │  │DiscountDb│
      │:5432     │  └───────┘  │:5432     │
      └─────────┘              └─────────┘
```

---

## 13. Sonraki Adım

**Bölüm 8: Docker Compose Orchestration**
- Tüm servislerin docker-compose.yml yapılandırması
- Environment variable ile service discovery (`DiscountUrl` → `http://discount-grpc:8080`)
- Networks, health checks, depends_on
