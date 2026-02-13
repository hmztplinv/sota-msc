# Bölüm 4: Basket.API — Proje İskeleti + Redis + HybridCache + CQRS Handlers

> **Tarih:** 2025-02-13  
> **Faz:** FAZ 2 — Basket Mikroservisi  
> **Servis:** Basket.API  
> **Mimari:** Vertical Slice Architecture + CQRS (MediatR)  
> **Veri Deposu:** Redis (HybridCache L1+L2)

---

## 1. Amaç & Kazanımlar

Bu bölümde Basket.API mikroservisini sıfırdan inşa ettik. Catalog.API'de öğrendiğimiz Vertical Slice + CQRS pattern'ini bu kez farklı bir veri deposu (Redis) üzerinde uyguladık.

**Ne yaptık:**
- Basket.API projesini oluşturduk ve BuildingBlocks'a bağladık
- ShoppingCart + ShoppingCartItem domain modellerini tasarladık
- IBasketRepository abstraction'ı + HybridCache implementasyonu yazdık
- GetBasket, StoreBasket, DeleteBasket CQRS handler'larını feature slice olarak oluşturduk
- FluentValidation ile StoreBasket doğrulaması ekledik
- Carter endpoint'leri ile REST API expose ettik
- Program.cs'te Redis + HybridCache + MediatR pipeline yapılandırdık
- Dockerfile (multi-stage) + Docker Compose entegrasyonu yaptık
- Uçtan uca CRUD test + Redis cache doğrulaması gerçekleştirdik

**Ne öğrendik:**
- HybridCache'in klasik IDistributedCache'den farkı ve avantajları
- Redis'in document store olarak kullanımı (sepet verisi)
- Namespace çakışması problemi ve çözümü (Basket → Features)
- Feature-based klasör yapısının farklı servislerde tutarlı uygulanması

---

## 2. Kavramlar & Tanımlar

### 2.1 Redis
**Redis (Remote Dictionary Server):** In-memory veri yapısı deposu. String, hash, list, set gibi veri tipleri destekler. Basket servisinde kullanıcı sepetlerini saklamak için kullanıyoruz.

**Neden Redis?**
- Sepet verisi geçici (temporary) — kalıcı veritabanı gerektirmez
- Çok hızlı okuma/yazma (sub-millisecond latency)
- TTL (Time-to-Live) ile otomatik silme desteği
- Distributed yapıda çalışabilir (cluster mode)

### 2.2 HybridCache (.NET 9 SOTA)
**.NET 9 ile gelen yeni caching mekanizması.** İki katmanlı cache yapısı sunar:

```
İstek → L1 (In-Memory Cache) → Miss? → L2 (Redis/Distributed Cache) → Miss? → Factory Callback
```

| Katman | Nerede? | Hız | Kapasite | Paylaşım |
|--------|---------|-----|----------|----------|
| **L1** | Uygulama belleği | ~nanosaniye | Sınırlı (RAM) | Sadece bu instance |
| **L2** | Redis (ağ üzerinden) | ~milisaniye | Büyük | Tüm instance'lar |

**Klasik IDistributedCache vs HybridCache:**

| Özellik | IDistributedCache | HybridCache |
|---------|-------------------|-------------|
| L1 Cache | ❌ Yok | ✅ Otomatik in-memory |
| Serialization | Manuel JSON | ✅ Otomatik |
| Stampede Protection | ❌ Yok | ✅ Var |
| Tip Güvenliği | ❌ byte[] | ✅ Generic `T` |
| .NET Sürümü | .NET Core 1.0+ | .NET 9+ |

**Stampede Protection Nedir?**
Aynı cache key'i için aynı anda 100 request gelirse, klasik yaklaşımda 100 kez factory çağrılır. HybridCache sadece 1 kez çağırır, diğer 99 request sonucu bekler.

### 2.3 HybridCache Redis Depolama Yapısı
HybridCache, Redis'te **hash** tipinde saklar:

| Hash Field | İçerik | Açıklama |
|------------|--------|----------|
| `data` | JSON serialize edilmiş obje | ShoppingCart verisi |
| `absexp` | Absolute expiration (ticks) | Ne zaman expire olacak |
| `sldexp` | Sliding expiration | -1 = yok |

Redis'te `GET` komutu çalışmaz çünkü hash tipinde. `HGETALL` ile okunur:
```bash
docker exec redis redis-cli HGETALL "basket:username"
```

### 2.4 Vertical Slice Architecture (Tekrar)
Her feature kendi klasöründe, kendi dosyalarıyla yaşar:

```
Features/
├── GetBasket/
│   ├── GetBasketQuery.cs      → Request (ne isteniyor?)
│   ├── GetBasketHandler.cs    → Business logic
│   └── GetBasketEndpoint.cs   → HTTP binding
├── StoreBasket/
│   ├── StoreBasketCommand.cs  → Request + Response DTO
│   ├── StoreBasketHandler.cs  → Business logic
│   ├── StoreBasketValidator.cs → Validation rules
│   └── StoreBasketEndpoint.cs → HTTP binding
└── DeleteBasket/
    ├── DeleteBasketCommand.cs → Request + Response DTO
    ├── DeleteBasketHandler.cs → Business logic
    └── DeleteBasketEndpoint.cs → HTTP binding
```

### 2.5 Repository Pattern (Cache Layer)
Basket'te Repository, veritabanına değil cache'e erişim soyutlamasıdır:

```
Endpoint → MediatR → Handler → IBasketRepository → HybridCache → Redis
```

Bu soyutlama sayesinde:
- Handler'lar cache teknolojisinden bağımsız
- Test'te mock repository kullanılabilir
- Bölüm 5'te **Decorator Pattern** ile caching davranışı eklenebilir

---

## 3. Neden Böyle? Mimari Gerekçe

### 3.1 Neden Redis? (vs PostgreSQL)
Sepet verisi **geçici** ve **sık erişilen** veridir. PostgreSQL'de saklamak:
- Gereksiz disk I/O
- JOIN gerektirmeyen basit key-value yapı
- Relational model overhead (tablo, index, migration)

Redis bu senaryoya mükemmel uyar: hızlı, basit, TTL ile otomatik temizlik.

### 3.2 Neden HybridCache? (vs Pure Redis / IDistributedCache)

**Alternatif 1 — Pure IDistributedCache:**
```csharp
// Her seferinde Redis'e gidip geliyorsun
var bytes = await _cache.GetAsync("basket:user");
var basket = JsonSerializer.Deserialize<ShoppingCart>(bytes);
```
- ❌ Her okumada network hop
- ❌ Manuel serialization
- ❌ Stampede yok

**Alternatif 2 — Manuel L1 + L2:**
```csharp
// Kendi MemoryCache + IDistributedCache kombinasyonun
if (!_memoryCache.TryGetValue(key, out basket))
{
    basket = await _distributedCache.GetAsync(key);
    _memoryCache.Set(key, basket, TimeSpan.FromMinutes(5));
}
```
- ❌ Cache invalidation karmaşıklığı
- ❌ Stampede kontrolü yok
- ❌ Boilerplate kod

**Seçimimiz — HybridCache:**
```csharp
var basket = await cache.GetOrCreateAsync(key, factory: ...);
```
- ✅ L1 + L2 otomatik
- ✅ Stampede protection
- ✅ Otomatik serialization
- ✅ Tek satır API
- ✅ .NET 9 resmi destekli

### 3.3 Neden Features/ klasörü? (Namespace Çakışması)
İlk denemede `Basket/` klasörü kullandık. Bu durumda:
- Proje namespace'i: `Basket.API`
- Feature klasörü namespace'i: `Basket.API.Basket`
- C# compiler `Basket.API.Models` referansını `Basket.API.Basket.API.Models` olarak çözmeye çalıştı → 23 hata!

**Çözüm:** `Basket/` → `Features/` — namespace çakışması ortadan kalktı.

**Ders:** Proje adı ile aynı isimde klasör oluşturma. Feature klasörlerini `Features/` altında topla.

### 3.4 Design Decision: Boş Sepet vs 404

```csharp
// GetBasketHandler
return Result<ShoppingCart>.Success(basket ?? new ShoppingCart(query.UserName));
```

**Neden 404 değil?**
- E-ticaret UX'inde kullanıcının "henüz sepeti yok" durumu normal bir durum
- Frontend her kullanıcı için sepet gösterir — boş olsa bile
- 404 exception'a neden olur, boş sepet zararsız bir default'tur
- Amazon, Trendyol gibi platformlar da aynı yaklaşımı kullanır

---

## 4. Adım Adım Uygulama

### Adım 1: Proje Oluşturma

```bash
# 1. Klasör yapısı
mkdir -p src/Services/Basket/Basket.API

# 2. Proje oluştur
dotnet new webapi -n Basket.API -o src/Services/Basket/Basket.API --no-openapi

# 3. Solution'a ekle
dotnet sln add src/Services/Basket/Basket.API/Basket.API.csproj

# 4. BuildingBlocks referansları
dotnet add src/Services/Basket/Basket.API reference src/BuildingBlocks/BuildingBlocks.CQRS/BuildingBlocks.CQRS.csproj
dotnet add src/Services/Basket/Basket.API reference src/BuildingBlocks/BuildingBlocks.Results/BuildingBlocks.Results.csproj
```

### Adım 2: NuGet Paketleri

```bash
# Ortak paketler (Catalog ile aynı)
dotnet add src/Services/Basket/Basket.API package Carter --version 8.2.1
dotnet add src/Services/Basket/Basket.API package FluentValidation --version 12.1.1
dotnet add src/Services/Basket/Basket.API package FluentValidation.DependencyInjectionExtensions --version 12.1.1
dotnet add src/Services/Basket/Basket.API package Mapster --version 7.4.0
dotnet add src/Services/Basket/Basket.API package Serilog.AspNetCore --version 10.0.0
dotnet add src/Services/Basket/Basket.API package Microsoft.AspNetCore.OpenApi --version 9.0.2
dotnet add src/Services/Basket/Basket.API package Scalar.AspNetCore --version 2.12.39

# Basket'e özel — Redis + HybridCache
dotnet add src/Services/Basket/Basket.API package Microsoft.Extensions.Caching.StackExchangeRedis
dotnet add src/Services/Basket/Basket.API package Microsoft.Extensions.Caching.Hybrid

# Health check
dotnet add src/Services/Basket/Basket.API package AspNetCore.HealthChecks.Redis
```

**Paket karşılaştırması:**

| Paket | Catalog.API | Basket.API | Açıklama |
|-------|:-----------:|:----------:|----------|
| Carter | ✅ | ✅ | Endpoint modülleri |
| FluentValidation | ✅ | ✅ | Validation pipeline |
| Mapster | ✅ | ✅ | DTO mapping |
| Serilog | ✅ | ✅ | Structured logging |
| Scalar | ✅ | ✅ | API documentation |
| Marten | ✅ | ❌ | Document DB (sadece Catalog) |
| StackExchangeRedis | ❌ | ✅ | Redis connection |
| HybridCache | ❌ | ✅ | L1+L2 cache |
| HealthChecks.NpgSql | ✅ | ❌ | PostgreSQL health |
| HealthChecks.Redis | ❌ | ✅ | Redis health |

### Adım 3: Domain Modelleri

**`Models/ShoppingCart.cs`**
```csharp
namespace Basket.API.Models;

public sealed class ShoppingCart
{
    public string UserName { get; set; } = default!;
    public List<ShoppingCartItem> Items { get; set; } = [];
    
    // Hesaplanmış toplam fiyat — Redis'te saklanmaz
    public decimal TotalPrice => Items.Sum(item => item.Price * item.Quantity);
    
    // Parameterless constructor — JSON deserialization için
    public ShoppingCart() { }
    
    // Business constructor
    public ShoppingCart(string userName) { UserName = userName; }
}
```

**`Models/ShoppingCartItem.cs`**
```csharp
namespace Basket.API.Models;

public sealed class ShoppingCartItem
{
    public int Quantity { get; set; }
    public string Color { get; set; } = default!;
    public decimal Price { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = default!;
}
```

**Tasarım kararları:**
- `sealed` → Gereksiz inheritance yok (SOTA)
- `TotalPrice` computed property → Redis'te ayrıca saklanmaz, her okumada hesaplanır
- Parameterless constructor → `System.Text.Json` deserialization zorunluluğu
- `ShoppingCartItem` → Catalog Product'ın "sepet projeksiyonu" — sadece gerekli alanlar

### Adım 4: Repository Abstraction + HybridCache

**`Data/IBasketRepository.cs`**
```csharp
namespace Basket.API.Data;

using Basket.API.Models;

public interface IBasketRepository
{
    Task<ShoppingCart?> GetBasketAsync(string userName, CancellationToken cancellationToken = default);
    Task<ShoppingCart> StoreBasketAsync(ShoppingCart basket, CancellationToken cancellationToken = default);
    Task<bool> DeleteBasketAsync(string userName, CancellationToken cancellationToken = default);
}
```

**`Data/BasketRepository.cs`**
```csharp
namespace Basket.API.Data;

using Basket.API.Models;
using Microsoft.Extensions.Caching.Hybrid;

public sealed class BasketRepository(HybridCache cache) : IBasketRepository
{
    private static string CacheKey(string userName) => $"basket:{userName}";

    public async Task<ShoppingCart?> GetBasketAsync(string userName, CancellationToken cancellationToken = default)
    {
        // L1 → L2 → factory (null = cache'e yazma)
        var basket = await cache.GetOrCreateAsync(
            CacheKey(userName),
            cancellationToken: cancellationToken,
            factory: _ => ValueTask.FromResult<ShoppingCart?>(null));
        return basket;
    }

    public async Task<ShoppingCart> StoreBasketAsync(ShoppingCart basket, CancellationToken cancellationToken = default)
    {
        // Hem L1 hem L2'ye yazar
        await cache.SetAsync(CacheKey(basket.UserName), basket, cancellationToken: cancellationToken);
        return basket;
    }

    public async Task<bool> DeleteBasketAsync(string userName, CancellationToken cancellationToken = default)
    {
        // Hem L1 hem L2'den siler
        await cache.RemoveAsync(CacheKey(userName), cancellationToken);
        return true;
    }
}
```

**HybridCache API Özeti:**

| Metot | Davranış |
|-------|----------|
| `GetOrCreateAsync(key, factory)` | L1 → L2 → factory. Factory sonucu cache'e yazılır |
| `SetAsync(key, value)` | L1 + L2'ye yazar |
| `RemoveAsync(key)` | L1 + L2'den siler |

### Adım 5: Program.cs

```csharp
// --- Redis + HybridCache ---
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Redis connection string is required.");
});

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        LocalCacheExpiration = TimeSpan.FromMinutes(5),   // L1 (memory)
        Expiration = TimeSpan.FromMinutes(30)              // L2 (Redis)
    };
});

// --- Repository ---
builder.Services.AddScoped<IBasketRepository, BasketRepository>();
```

**Cache TTL Stratejisi:**
- **L1 = 5 dakika:** Kısa — memory pressure'ı düşük tut, sık güncellenen sepetler stale kalmasın
- **L2 = 30 dakika:** Uzun — Redis'te daha uzun tut, kullanıcı geri dönebilir

### Adım 6: CQRS Handlers (Feature Slices)

**GetBasket (Query):**
```
GET /basket/{userName} → GetBasketQuery → GetBasketHandler → IBasketRepository → HybridCache
```
- Sepet yoksa boş sepet döner (404 değil)

**StoreBasket (Command):**
```
POST /basket → StoreBasketRequest → Mapster → StoreBasketCommand → Handler → Repository → HybridCache
```
- FluentValidation ile doğrulanır (UserName, Items, Quantity, Price)
- 201 Created döner

**DeleteBasket (Command):**
```
DELETE /basket/{userName} → DeleteBasketCommand → Handler → Repository → HybridCache.RemoveAsync
```
- L1 + L2'den siler

### Adım 7: FluentValidation

```csharp
public sealed class StoreBasketCommandValidator : AbstractValidator<StoreBasketCommand>
{
    public StoreBasketCommandValidator()
    {
        RuleFor(x => x.Cart).NotNull().WithMessage("Sepet boş olamaz.");
        RuleFor(x => x.Cart.UserName).NotEmpty().WithMessage("UserName zorunludur.");
        RuleFor(x => x.Cart.Items).NotEmpty().WithMessage("Sepette en az bir ürün olmalı.");
        
        RuleForEach(x => x.Cart.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.Price).GreaterThanOrEqualTo(0);
        });
    }
}
```

**Not:** `RuleForEach` + `ChildRules` → Collection içindeki her item için ayrı validation kuralları tanımlar.

### Adım 8: Dockerfile + Docker Compose

**Dockerfile (multi-stage):** Catalog.API ile aynı pattern:
```
Stage 1 (build): SDK image → restore → publish
Stage 2 (runtime): ASP.NET image → curl + non-root user → ENTRYPOINT
```

**Docker Compose'a eklenenler:**
```yaml
redis:
  image: redis:7-alpine
  healthcheck: redis-cli ping

basket-api:
  environment:
    - ConnectionStrings__Redis=redis:6379
  depends_on:
    redis: { condition: service_healthy }
  ports:
    - "5002:8080"
```

**Port konvansiyonu:**
| Servis | External Port |
|--------|---------------|
| Catalog.API | 5001 |
| Basket.API | 5002 |
| Discount.Grpc | 5003 (planlanan) |
| Ordering.API | 5004 (planlanan) |

---

## 5. Kontrol Listesi

- [x] Basket.API build başarılı
- [x] Docker container healthy (basket-api + redis)
- [x] Health check `/health` → 200 OK (Healthy)
- [x] GET `/basket/{userName}` — boş sepet dönüyor
- [x] POST `/basket` — sepet oluşturuluyor (201 Created)
- [x] GET `/basket/{userName}` — dolu sepet + doğru TotalPrice
- [x] DELETE `/basket/{userName}` — sepet siliniyor
- [x] Redis'te key doğrulaması — `KEYS "*"` ile `basket:*` görünüyor
- [x] Redis hash yapısı doğrulandı — `HGETALL` ile JSON data görünüyor
- [x] L1 + L2 cache akışı çalışıyor
- [x] FluentValidation pipeline'da aktif
- [x] Serilog request logging çalışıyor
- [x] Git commit + push yapıldı

---

## 6. Sık Hatalar & Çözümleri

### Hata 1: Namespace Çakışması (23 Error)
**Hata:**
```
error CS0234: The type or namespace name 'API' does not exist in the namespace 'Basket.API.Basket'
```

**Neden:** `Basket.API` projesi içinde `Basket/` adında klasör → `Basket.API.Basket` namespace'i oluşuyor → Compiler `Basket.API.Models` ifadesini `Basket.API.Basket.API.Models` olarak çözmeye çalışıyor.

**Çözüm:** Feature klasörünü `Basket/` → `Features/` olarak yeniden adlandır:
```bash
mv src/Services/Basket/Basket.API/Basket src/Services/Basket/Basket.API/Features
find src/Services/Basket/Basket.API/Features -name "*.cs" -exec sed -i 's/Basket\.API\.Basket\./Basket.API.Features./g' {} \;
```

**Genel Kural:** Proje adı ile aynı isimde alt klasör oluşturma!

### Hata 2: Redis WRONGTYPE Error
**Hata:**
```
WRONGTYPE Operation against a key holding the wrong kind of value
```

**Neden:** HybridCache, Redis'te `hash` tipinde saklar. `GET` komutu `string` tipi bekler.

**Çözüm:** `HGETALL` kullan:
```bash
docker exec redis redis-cli TYPE "basket:cachetest"     # → hash
docker exec redis redis-cli HGETALL "basket:cachetest"   # → data + JSON
```

### Hata 3: sed Komutu Çalışmıyor
**Neden:** sed'de escape karakterler doğru yazılmamış veya dosya path'i yanlış.

**Çözüm:** Nokta (`.`) karakterini escape et:
```bash
sed -i 's/Basket\.API\.Basket\./Basket.API.Features./g'
```

### Hata 4: BuildingBlocks Path Bulunamıyor
**Hata:**
```
Could not find project or directory `src/BuildingBlocks/BuildingBlocks/BuildingBlocks.csproj`
```

**Çözüm:** `find` ile doğru path'i bul:
```bash
find src/BuildingBlocks -name "*.csproj"
```
Bizim projede: `BuildingBlocks.CQRS` ve `BuildingBlocks.Results` ayrı projeler.

---

## 7. Best Practices

### 7.1 Cache Key Convention
```
basket:{userName}
```
- Prefix ile namespace ayrımı (ileride `product:`, `order:` gibi key'ler de olabilir)
- Küçük harf + iki nokta üst üste separator → Redis topluluğunun yaygın convention'ı

### 7.2 HybridCache TTL Stratejisi
| Katman | Süre | Neden? |
|--------|------|--------|
| L1 (Memory) | 5 dk | Kısa — memory pressure + stale data riski |
| L2 (Redis) | 30 dk | Orta — kullanıcı geri dönebilir, ama sonsuza kadar tutma |

Production'da bu değerler traffic pattern'e göre ayarlanır.

### 7.3 Boş Sepet Dönme Stratejisi
```csharp
return basket ?? new ShoppingCart(query.UserName);
```
Frontend hiçbir zaman null/404 ile uğraşmaz. Her zaman geçerli bir ShoppingCart objesi alır.

### 7.4 Sealed Classes
Tüm handler, validator, endpoint, repository, model sınıfları `sealed`. Performans + güvenlik:
- JIT optimizer sealed class'lar için daha agresif devirtualization yapabilir
- Gereksiz inheritance zinciri engellenir

### 7.5 Primary Constructor (C# 12+)
```csharp
public sealed class BasketRepository(HybridCache cache) : IBasketRepository
```
Field declaration + constructor injection tek satırda. Daha az boilerplate.

---

## 8. TODO / Tartışma Notları

- **TODO (Bölüm 5):** CachedBasketRepository — Decorator pattern ile caching layer ayrıştırılacak
- **TODO (Bölüm 5):** Polly retry + circuit breaker — Redis bağlantı kopmaları için resilience
- **TODO (Bölüm 5):** CustomExceptionHandler — Basket'e özel exception handling
- **TODO (Bölüm 7):** gRPC client — Discount servisinden indirim bilgisi çekilecek
- **TODO (Bölüm 14):** MassTransit — Checkout flow, Basket → Ordering async messaging
- **TODO:** Sepet item güncelleme (quantity değiştirme) — şu an tüm sepet replace ediliyor
- **TODO:** Sepet boyutu limiti — çok fazla item eklenmesini engelleme
- **TODO:** Cache invalidation stratejisi — ürün fiyatı değişince sepetteki fiyat stale kalabilir

---

## 9. Kısa Özet (Summary)

Bölüm 4'te Basket.API mikroservisini sıfırdan oluşturduk. Redis üzerinde HybridCache (L1 in-memory + L2 Redis) ile çalışan bir sepet servisi geliştirdik. Catalog.API'de öğrendiğimiz Vertical Slice + CQRS + Carter + FluentValidation + MediatR pipeline pattern'ini Basket'e de uyguladık. Namespace çakışması problemini çözerek Features/ klasör yapısına geçtik. Docker Compose'a Redis servisi ekleyerek tüm CRUD akışını uçtan uca test ettik ve Redis'teki cache yapısını (hash tipinde JSON data + expiration bilgisi) doğruladık. Bölüm 5'te resilience pattern'ler ve decorator pattern ile bu altyapıyı güçlendireceğiz.

---

## 10. Ne Öğrendim? (What I Learned) — 3 Madde

1. **HybridCache, .NET 9'un SOTA caching çözümü.** Klasik IDistributedCache'in yerini alıyor — L1+L2 otomatik, stampede protection dahil, tek satır API. Redis'te hash tipinde saklar (data + absexp + sldexp).

2. **Namespace çakışması, C#'ta yaygın bir tuzak.** Proje adı (`Basket.API`) ile aynı adda alt klasör (`Basket/`) oluşturunca compiler namespace resolution'da karışıyor. Çözüm: Feature klasörlerini `Features/` altında toplamak.

3. **Vertical Slice + CQRS pattern'i servisler arası transfer edilebilir.** Catalog (PostgreSQL + Marten) ve Basket (Redis + HybridCache) çok farklı veri depoları kullanmasına rağmen aynı mimari pattern uygulandı — handler'lar, endpoint'ler, validation aynı yapıda.

---

## 11. Öğrenme Pekiştirme (Reinforcement)

### Mini Quiz (10 Soru)

**1. HybridCache'te L1 ve L2 ne anlama gelir?**
a) L1 = Disk, L2 = Network  
b) L1 = In-Memory, L2 = Redis (Distributed)  
c) L1 = Redis, L2 = SQL Server  
d) L1 = CDN, L2 = Application Server  

**2. Doğru/Yanlış: HybridCache, Redis'te string tipinde saklar.**

**3. Stampede protection ne işe yarar?**
a) DDoS saldırılarını önler  
b) Aynı key için eşzamanlı factory çağrılarını tek çağrıya düşürür  
c) Redis connection pool yönetimi yapar  
d) Cache expiration'ı otomatik yeniler  

**4. ShoppingCart'ta TotalPrice neden computed property?**

**5. Doğru/Yanlış: Basket.API'de kullanıcının sepeti yoksa 404 Not Found dönülür.**

**6. `Basket.API` projesi içinde `Basket/` klasörü oluşturursak ne olur?**
a) Hiçbir sorun olmaz  
b) Build hatası — namespace çakışması  
c) Runtime exception  
d) Docker build başarısız olur  

**7. HybridCache.GetOrCreateAsync'te factory null dönerse ne olur?**
a) Exception fırlatır  
b) Cache'e null yazar  
c) Cache'e yazmaz, null döner  
d) Default değer döner  

**8. Redis health check hangi komut ile çalışır?**
a) `GET health`  
b) `PING`  
c) `STATUS`  
d) `INFO`  

**9. Docker Compose'da Basket.API hangi port'tan erişilebilir?**
a) 5001  
b) 5002  
c) 6379  
d) 8080  

**10. Senaryo: Redis bağlantısı koptu. HybridCache.GetOrCreateAsync çağrıldığında ne olur?**
a) L1'den (memory) dönebilir, L2'ye erişemezse exception  
b) Her zaman exception fırlatır  
c) Otomatik olarak PostgreSQL'e fallback yapar  
d) Boş sonuç döner  

### Görevler

**Görev 1:** Redis'e bağlanıp tüm basket key'lerini listele ve birinin içeriğini görüntüle.
- Beklenen çıktı: `basket:*` key listesi + JSON data

**Görev 2:** Scalar UI üzerinden (`http://localhost:5002/scalar/v1`) StoreBasket endpoint'ine geçersiz veri gönder (boş userName). Validation hatasını gözlemle.
- Beklenen çıktı: 400 Bad Request + validation error mesajı

**Görev 3:** L1 cache'in çalıştığını doğrula — sepet oluştur, Redis'ten key'i sil (`DEL`), ama API'den hâlâ oku (L1'den dönmeli).
```bash
# 1. Sepet oluştur
curl -s -X POST http://localhost:5002/basket -H "Content-Type: application/json" -d '...'
# 2. Redis'ten sil
docker exec redis redis-cli DEL "basket:username"
# 3. API'den oku — L1 cache'te varsa dönecek (5 dk TTL içinde)
curl -s http://localhost:5002/basket/username
```

### Cevap Anahtarı

1. **b) L1 = In-Memory, L2 = Redis (Distributed)** — L1 uygulama belleğinde, L2 ağ üzerinden Redis'te.

2. **Yanlış.** HybridCache, Redis'te **hash** tipinde saklar (data, absexp, sldexp field'ları ile).

3. **b) Aynı key için eşzamanlı factory çağrılarını tek çağrıya düşürür.** 100 request aynı key'i isterse, factory 1 kez çağrılır, diğerleri sonucu bekler.

4. **TotalPrice her okumada güncel hesaplanır.** Redis'te ayrıca saklamaya gerek yok — Items değiştiğinde TotalPrice otomatik doğru olur. Ayrıca data consistency sağlar.

5. **Yanlış.** Sepet yoksa boş sepet (`Items: [], TotalPrice: 0`) döner. UX açısından daha iyi — frontend null/404 ile uğraşmaz.

6. **b) Build hatası — namespace çakışması.** `Basket.API.Basket` namespace'i, compiler'ın `Basket.API.xxx` using'lerini yanlış çözmesine neden olur.

7. **c) Cache'e yazmaz, null döner.** Factory null dönerse HybridCache bunu "veri yok" olarak yorumlar ve cache'e yazmaz.

8. **b) PING.** Redis health check `redis-cli ping` komutunu kullanır, beklenen yanıt `PONG`.

9. **b) 5002.** Docker Compose'da `ports: "5002:8080"` — external 5002, internal 8080.

10. **a) L1'den dönebilir, L2'ye erişemezse exception.** Eğer L1'de varsa hızlıca döner. L1'de yoksa L2'ye (Redis) gider — bağlantı kopuksa exception. Bölüm 5'te Polly retry + circuit breaker ile bu senaryoyu handle edeceğiz.

---

## 12. Dosya Yapısı Özeti

```
src/Services/Basket/Basket.API/
├── Data/
│   ├── IBasketRepository.cs           # Repository abstraction
│   └── BasketRepository.cs            # HybridCache implementation
├── Features/
│   ├── GetBasket/
│   │   ├── GetBasketQuery.cs          # IQuery<ShoppingCart>
│   │   ├── GetBasketHandler.cs        # IQueryHandler
│   │   └── GetBasketEndpoint.cs       # GET /basket/{userName}
│   ├── StoreBasket/
│   │   ├── StoreBasketCommand.cs      # ICommand<StoreBasketResponse> + DTOs
│   │   ├── StoreBasketHandler.cs      # ICommandHandler
│   │   ├── StoreBasketValidator.cs    # FluentValidation
│   │   └── StoreBasketEndpoint.cs     # POST /basket
│   └── DeleteBasket/
│       ├── DeleteBasketCommand.cs     # ICommand<DeleteBasketResponse> + DTOs
│       ├── DeleteBasketHandler.cs     # ICommandHandler
│       └── DeleteBasketEndpoint.cs    # DELETE /basket/{userName}
├── Models/
│   ├── ShoppingCart.cs                # Sepet entity
│   └── ShoppingCartItem.cs           # Sepet item entity
├── Dockerfile                          # Multi-stage build
├── Program.cs                          # DI + Redis + HybridCache + Pipeline
├── appsettings.json                    # Redis connection + Serilog config
└── Basket.API.csproj                   # Package references
```

---

## 13. Catalog.API vs Basket.API Karşılaştırması

| Özellik | Catalog.API | Basket.API |
|---------|-------------|------------|
| **Mimari** | Vertical Slice + CQRS | Vertical Slice + CQRS |
| **Veri Deposu** | PostgreSQL + Marten | Redis + HybridCache |
| **Cache** | Yok | L1 (Memory) + L2 (Redis) |
| **Feature Klasörü** | Products/ | Features/ |
| **CRUD** | 5 handler | 3 handler |
| **Validation** | CreateProduct, UpdateProduct | StoreBasket |
| **Health Check** | NpgSql | Redis |
| **Docker Port** | 5001 | 5002 |
| **Scalar Theme** | Mars | BluePlanet |
| **Seed Data** | CatalogInitialData | Yok (cache = geçici) |

---

*Sonraki Bölüm: Bölüm 5 — Basket.API CQRS + Resilience Patterns (Polly retry, circuit breaker, CachedBasketRepository decorator)*
