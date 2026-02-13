# Bölüm 2: Catalog.API — Vertical Slice + CQRS Handlers + Carter Endpoints + Pipeline Behaviors

## 1. Amaç & Kazanımlar

Bu bölümde Catalog.API mikroservisinin **tüm CRUD operasyonlarını** Vertical Slice Architecture ile uyguladık. Her feature kendi klasöründe yaşıyor — handler, endpoint, validator, DTO hepsi bir arada.

**Kazanımlar:**
- Vertical Slice Architecture'ın feature folder yapısını uygulamak
- Generic CQRS abstractions (`ICommand<T>`, `IQuery<T>`) ile handler yazmak
- Result pattern'i handler'larda kullanmak (exception'sız hata yönetimi)
- Carter ile Minimal API endpoint'lerini modüler organize etmek
- REPR pattern (Request-Endpoint-Response) uygulamak
- Generic Pipeline Behaviors (Logging + Validation) yazmak
- FluentValidation ile otomatik validation pipeline'ı kurmak

---

## 2. Kavramlar & Tanımlar

### Vertical Slice Architecture (Dikey Dilim Mimarisi)
Her feature (use case) kendi klasöründe yaşar — handler, DTO, endpoint, validator bir arada. Geleneksel katmanlı mimarinin (Controllers/, Services/, Repositories/) aksine **feature bazlı** organize eder. Bir feature'a dokunduğunda ilgili tüm dosyalar aynı yerde bulunur.

### CQRS — Command Query Responsibility Segregation (Komut-Sorgu Sorumluluk Ayrımı)
Okuma (Query) ve yazma (Command) işlemlerini ayrı model/handler'larla yapmak. **Command** veriyi değiştirir, **Query** sadece okur. Bu ayrım:
- Bağımsız ölçeklendirme (read replica vs write master)
- Farklı optimizasyon (query'ler cache'lenebilir, command'lar validation gerektirir)
- Pipeline behavior'larda ayırt edebilme (sadece command'lara transaction uygulama gibi)

### REPR Pattern — Request-Endpoint-Response
Her API endpoint'i üç parçadan oluşur:
- **Request** → gelen veri (DTO veya query string)
- **Endpoint** → routing + MediatR dispatch
- **Response** → dönüş tipi (Result → HTTP status code mapping)

### Pipeline Behavior (Boru Hattı Davranışı)
MediatR'ın middleware konsepti. Her `Send()` çağrısında handler'dan **önce ve sonra** çalışır. Sıralama önemli — registration sırasına göre çalışır. Logging → Validation → Handler şeklinde zincirlenir.

### Carter
Minimal API'ları `ICarterModule` interface'i ile modüler organize eden kütüphane. Her modül kendi endpoint'lerini tanımlar, `app.MapCarter()` ile otomatik keşfedilir.

### FluentValidation (Akıcı Doğrulama)
Strongly-typed validation kuralları tanımlamak için kütüphane. `AbstractValidator<T>` ile her command/query için ayrı validator yazılır. Pipeline behavior aracılığıyla MediatR pipeline'ına entegre edilir.

### IResultBase
`Result` ve `Result<T>` için ortak interface. Pipeline Behavior'ların generic constraint olarak kullanması için gerekli — `where TResponse : IResultBase` ile hem `Result` hem `Result<T>` dönen handler'ları tek behavior ile handle eder.

---

## 3. Neden Böyle? Mimari Gerekçe

### Neden Vertical Slice — Katmanlı Mimari Değil?

| Özellik | Katmanlı (Layered) | Vertical Slice |
|---------|-------------------|----------------|
| Organizasyon | Teknik katman (Controllers/, Services/) | Feature bazlı (GetProducts/, CreateProduct/) |
| Değişiklik etkisi | Bir feature için 4-5 klasör arası zıplama | Tüm ilgili dosyalar aynı yerde |
| Bağımlılık | Katmanlar arası tight coupling riski | Feature'lar birbirinden bağımsız |
| Ölçeklendirme | Tüm katman büyür | Sadece yeni feature klasörü eklenir |

**Trade-off:** Küçük projelerde over-engineering hissi verebilir. Ama mikroservislerde feature sayısı arttıkça avantajı belirginleşir.

### Neden Handler'da Result Pattern — Exception Değil?

```csharp
// ❌ Exception ile flow control
if (product is null)
    throw new NotFoundException("Product not found");

// ✅ Result pattern ile
if (product is null)
    return Error.NotFound("Product.NotFound", "...");
```

**Avantajlar:**
- Exception fırlatmak **pahalı** (stack trace oluşturma maliyeti)
- "Ürün bulunamadı" bir hata değil, **beklenen bir durum** — exception olmamalı
- Compiler ile tip güvenliği — `Result<T>` dönen metot her zaman handle edilmeli
- Pipeline behavior'larda catch gerekmez

### Neden Ayrı Request DTO + Command?

```
CreateProductRequest (API contract) → CreateProductCommand (internal)
```

- **API contract** dışarıya açık — versiyonlama, backward compatibility
- **Command** internal — validation attribute'ları, ek field'lar eklenebilir
- Değişiklik propagation'ı kontrollü — API değişmeden internal logic değişebilir

### Neden ISender — IMediator Değil?

```csharp
// ✅ ISender — sadece Send metodu, minimal interface
async (ISender sender) => await sender.Send(query)

// ❌ IMediator — Send + Publish + CreateStream, gereksiz yüzey
async (IMediator mediator) => await mediator.Send(query)
```

**Interface Segregation Principle** — endpoint sadece Send yapıyor, Publish'e ihtiyacı yok.

---

## 4. Adım Adım Uygulama

### 4.1 Feature Folder Yapısı

```bash
mkdir -p Features/{GetProducts,GetProductById,CreateProduct,UpdateProduct,DeleteProduct}
```

Sonuç:
```
Catalog.API/
  Features/
    GetProducts/
      GetProductsHandler.cs        # Query + Result + Handler
      GetProductsEndpoint.cs       # Carter endpoint
    GetProductById/
      GetProductByIdHandler.cs
      GetProductByIdEndpoint.cs
    CreateProduct/
      CreateProductHandler.cs      # Command + Result + Handler
      CreateProductEndpoint.cs
      CreateProductValidator.cs    # FluentValidation
    UpdateProduct/
      UpdateProductHandler.cs
      UpdateProductEndpoint.cs
      UpdateProductValidator.cs
    DeleteProduct/
      DeleteProductHandler.cs
      DeleteProductEndpoint.cs
```

### 4.2 Query Handler'lar (Okuma İşlemleri)

**GetProducts — Pagination ile listeleme:**
```csharp
public sealed record GetProductsQuery(int PageNumber = 1, int PageSize = 10)
    : IQuery<GetProductsResult>;

public sealed record GetProductsResult(
    IEnumerable<Product> Products,
    long TotalCount,
    int PageNumber,
    int PageSize);

internal sealed class GetProductsHandler(IDocumentSession session)
    : IQueryHandler<GetProductsQuery, GetProductsResult>
{
    public async Task<Result<GetProductsResult>> Handle(
        GetProductsQuery query, CancellationToken cancellationToken)
    {
        var products = await session.Query<Product>()
            .ToPagedListAsync(query.PageNumber, query.PageSize, cancellationToken);

        return new GetProductsResult(
            products, products.TotalItemCount,
            query.PageNumber, query.PageSize);
    }
}
```

**Kritik noktalar:**
- `ToPagedListAsync()` → Marten built-in pagination, `TotalItemCount` otomatik
- `IQuery<T>` → `IRequest<Result<T>>` wrap'ı, handler `Result<T>` döner
- `sealed record` → immutable, equality by value, inheritance kapalı
- `internal sealed class` → handler dışarıdan erişilemez, sadece MediatR kullanır

**GetProductById — Tek ürün okuma:**
```csharp
public sealed record GetProductByIdQuery(Guid Id) : IQuery<GetProductByIdResult>;

internal sealed class GetProductByIdHandler(IDocumentSession session)
    : IQueryHandler<GetProductByIdQuery, GetProductByIdResult>
{
    public async Task<Result<GetProductByIdResult>> Handle(...)
    {
        var product = await session.LoadAsync<Product>(query.Id, cancellationToken);

        if (product is null)
            return Error.NotFound("Product.NotFound", $"Product with id '{query.Id}' not found.");

        return new GetProductByIdResult(product);
    }
}
```

**Kritik noktalar:**
- `LoadAsync<T>(id)` → Marten'ın ID bazlı okuma metodu, `Query<T>()` + `Where()` yerine optimized
- `Error.NotFound()` → implicit conversion ile `Result<T>.Failure()` oluşur

### 4.3 Command Handler'lar (Yazma İşlemleri)

**CreateProduct — Yeni ürün oluşturma:**
```csharp
public sealed record CreateProductCommand(
    string Name, List<string> Categories, string Description,
    string ImageFile, decimal Price) : ICommand<CreateProductResult>;

public sealed record CreateProductResult(Guid Id);

internal sealed class CreateProductHandler(IDocumentSession session)
    : ICommandHandler<CreateProductCommand, CreateProductResult>
{
    public async Task<Result<CreateProductResult>> Handle(...)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Categories = command.Categories,
            Description = command.Description,
            ImageFile = command.ImageFile,
            Price = command.Price
        };

        session.Store(product);                              // Unit of Work'e ekle
        await session.SaveChangesAsync(cancellationToken);   // Transaction commit

        return new CreateProductResult(product.Id);
    }
}
```

**Kritik noktalar:**
- `session.Store()` → Marten'ın Unit of Work'üne ekler, henüz DB'ye yazmaz
- `SaveChangesAsync()` → tek seferde DB'ye yazar (atomic transaction)
- `ICommand<CreateProductResult>` → değer döndüren command (oluşan ID)
- Implicit conversion → `new CreateProductResult(...)` otomatik `Result<T>.Success()` olur

**UpdateProduct — Mevcut ürün güncelleme:**
```csharp
// Pattern: Load → Validate → Mutate → Save
var product = await session.LoadAsync<Product>(command.Id, cancellationToken);

if (product is null)
    return Error.NotFound(...);

product.Name = command.Name;        // Mutate
// ... diğer alanlar

session.Update(product);            // Marten'a değişikliği bildir
await session.SaveChangesAsync(cancellationToken);

return new UpdateProductResult(true);
```

**DeleteProduct — Ürün silme (değer döndürmeyen command):**
```csharp
public sealed record DeleteProductCommand(Guid Id) : ICommand;    // Generic parametresiz!

internal sealed class DeleteProductHandler(IDocumentSession session)
    : ICommandHandler<DeleteProductCommand>                        // Tek generic parametre
{
    public async Task<Result> Handle(...)                          // Result, Result<T> değil!
    {
        // Load → Validate → Delete → Save
        session.Delete(product);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
```

### 4.4 Carter Endpoint'ler

**Genel pattern — Result → HTTP status code mapping:**
```csharp
public sealed class XxxEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", async (..., ISender sender) =>
        {
            var result = await sender.Send(query);

            return result.IsSuccess
                ? Results.Ok(result.Value)          // 200
                : Results.NotFound(result.Error);   // 404
        })
        .WithName("...")
        .WithTags("Products")
        .Produces<TResult>()
        .ProducesProblem(404);
    }
}
```

**Endpoint — HTTP Mapping Tablosu:**

| Endpoint | HTTP Method | Başarı | Hata |
|----------|-------------|--------|------|
| GetProducts | GET `/api/products?pageNumber=1&pageSize=10` | 200 OK + body | 400 Bad Request |
| GetProductById | GET `/api/products/{id:guid}` | 200 OK + body | 404 Not Found |
| CreateProduct | POST `/api/products` | 201 Created + Location | 400 Bad Request |
| UpdateProduct | PUT `/api/products/{id:guid}` | 200 OK | 404 Not Found |
| DeleteProduct | DELETE `/api/products/{id:guid}` | 204 No Content | 404 Not Found |

**REST Convention'lar:**
- `{id:guid}` route constraint → sadece valid GUID kabul eder
- POST → 201 Created + `Location: /api/products/{id}` header
- DELETE → 204 No Content (body yok)
- PUT'da ID route'dan gelir, body'de tekrarlanmaz

### 4.5 Pipeline Behaviors

**Registration sırası (Program.cs):**
```csharp
builder.Services.AddMediatR(config =>
{
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);
    config.AddOpenBehavior(typeof(LoggingBehavior<,>));      // 1. sıra
    config.AddOpenBehavior(typeof(ValidationBehavior<,>));    // 2. sıra
});

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
```

**Çalışma sırası:**
```
Request gelir
  → LoggingBehavior [START] log
    → ValidationBehavior → validator var mı? → kurallar geçer mi?
      → Handler (iş mantığı)
    ← ValidationBehavior (hata varsa burada keser, handler'a ulaşmaz)
  ← LoggingBehavior [END] log + süre
Response döner
```

**LoggingBehavior — Her request'i loglar:**
```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        logger.LogInformation("[START] {RequestName}", typeof(TRequest).Name);
        var sw = Stopwatch.StartNew();

        var response = await next();    // → sonraki behavior veya handler

        sw.Stop();
        if (sw.ElapsedMilliseconds > 500)
            logger.LogWarning("[SLOW] {RequestName} took {Ms}ms", ...);

        logger.LogInformation("[END] {RequestName} completed in {Ms}ms", ...);
        return response;
    }
}
```

**ValidationBehavior — FluentValidation auto-trigger:**
```csharp
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)    // DI: tüm validator'ları inject
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResultBase                     // Sadece Result dönen handler'lar
{
    public async Task<TResponse> Handle(...)
    {
        if (!validators.Any()) return await next();   // Validator yoksa geç

        // Tüm validator'ları paralel çalıştır
        var results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, ct)));

        var errors = results.SelectMany(r => r.Errors).ToList();

        if (errors.Count == 0) return await next();   // Hata yoksa devam

        // Hata varsa → Result.Failure dön, handler'a ulaşma!
        var errorMessage = string.Join("; ", errors.Select(e => e.ErrorMessage));
        return (TResponse)CreateValidationResult(typeof(TResponse), error);
    }
}
```

**Neden `IResultBase` constraint?**
- `where TResponse : IResultBase` olmadan, behavior `Result.Failure()` dönemez
- Bu constraint sayesinde hem `Result` hem `Result<T>` dönen handler'ları tek behavior handle eder
- Validator'sız query'ler (GetProducts gibi) doğrudan `next()`'e geçer

### 4.6 IResultBase Interface

```csharp
// BuildingBlocks.Results/IResultBase.cs
public interface IResultBase
{
    bool IsSuccess { get; }
    bool IsFailure { get; }
    Error Error { get; }
}

// Result sınıfına implement:
public class Result : IResultBase { ... }
// Result<T> otomatik olarak Result'tan inherit ettiği için IResultBase'i de alır
```

### 4.7 Validator Yapısı

```csharp
public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required.")
            .MaximumLength(150).WithMessage("...");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than zero.");
    }
}
```

**FluentValidation otomatik keşif:**
- `AddValidatorsFromAssembly()` → assembly'deki tüm `AbstractValidator<T>`'leri register eder
- Request geldiğinde DI container'dan `IValidator<CreateProductCommand>` çözülür
- ValidationBehavior inject eder ve çalıştırır

---

## 5. Kontrol Listesi

- [x] Build başarılı (0 error, 0 warning)
- [x] 5 feature folder oluşturuldu
- [x] 5 CQRS handler yazıldı ve çalışıyor
- [x] 5 Carter endpoint yazıldı
- [x] GetProducts pagination çalışıyor
- [x] GetProductById — NotFound error Result pattern ile dönüyor
- [x] CreateProduct — 201 Created + Location header
- [x] UpdateProduct — Load → Validate → Mutate → Save pattern
- [x] DeleteProduct — 204 No Content
- [x] LoggingBehavior — [START]/[END] + slow query warning
- [x] ValidationBehavior — invalid request 400 + error mesajları
- [x] CreateProductValidator + UpdateProductValidator
- [x] IResultBase interface eklendi
- [x] Postman collection hazırlandı
- [x] Git commit + push

---

## 6. Sık Hatalar & Çözümleri

### ❌ "Product does not contain a definition for 'Category'"
**Sebep:** Property ismi `Categories` (çoğul), `Category` (tekil) değil.
**Çözüm:** Model'deki property ismiyle birebir eşleştir.

### ❌ Handler'da `throw new NotImplementedException()` kalan explicit interface
**Sebep:** `IQueryHandler<TQuery, TResponse>` aslında `IRequestHandler<TQuery, Result<TResponse>>` — handler'ın return tipi `Task<Result<T>>` olmalı.
**Çözüm:** Handle metodunun return tipini `Task<Result<T>>` yap, explicit interface implementation'ı sil.

### ❌ `CS0109: The new keyword is not required` warning
**Sebep:** `Result<T>.Success(T)` ile `Result.Success()` farklı imzalar — `new` gereksiz.
**Çözüm:** `public new static Result<T> Success(T value)` → `public static Result<T> Success(T value)`

### ❌ Validation hataları dönmüyor
**Olası sebepler:**
1. `AddValidatorsFromAssembly()` eklenmemiş
2. `AddOpenBehavior(typeof(ValidationBehavior<,>))` eklenmemiş
3. Validator sınıfı `AbstractValidator<TCommand>` yerine yanlış tip kullanıyor

### ❌ Marten `Store()` çağrılıyor ama DB'ye yazılmıyor
**Sebep:** `SaveChangesAsync()` çağrılmamış.
**Açıklama:** `Store()` sadece Unit of Work'e ekler, `SaveChangesAsync()` transaction commit yapar.

---

## 7. Best Practices

### Vertical Slice Kuralları
- ✅ Her feature kendi klasöründe — handler, endpoint, validator, DTO bir arada
- ✅ Feature'lar arası bağımlılık yok — birbirlerini import etmezler
- ✅ Shared yapılar BuildingBlocks'ta — DRY ama loosely coupled

### CQRS Kuralları
- ✅ Query hiçbir zaman veriyi değiştirmez
- ✅ Command her zaman Result döner (başarı veya hata)
- ✅ Handler `internal sealed` — sadece MediatR erişir
- ✅ Record types — immutable request/response

### Endpoint Kuralları
- ✅ Endpoint'te iş mantığı yok — sadece mapping + dispatch
- ✅ `ISender` kullan, `IMediator` değil (Interface Segregation)
- ✅ Result → HTTP status code mapping net ve tutarlı
- ✅ `.WithName()`, `.WithTags()`, `.Produces<T>()` — Scalar/OpenAPI documentation

### Pipeline Behavior Kuralları
- ✅ Generic olarak yaz — tüm servisler kullanabilsin
- ✅ Registration sırası önemli — Logging → Validation → Handler
- ✅ Validation hata dönerse handler'a **ulaşmaz** (early return)
- ✅ Validator yoksa behavior otomatik geçer (opt-in)

---

## 8. TODO / Tartışma Notları

- **// TODO:** Mapster ile Request → Command mapping otomatikleştir (şu an manual)
- **// TODO:** GetProductsByCategory query ekle (Marten LINQ filtering)
- **// TODO:** ExceptionHandlingBehavior ekle (beklenmeyen exception'ları Result'a çevir)
- **// TODO:** CachingBehavior ekle (ICacheable interface ile opt-in cache)
- **// TODO:** Bölüm 3'te Scalar (OpenAPI) documentation entegrasyonu
- **// TODO:** Pagination'ı cursor-based'e çevir (büyük dataset'ler için daha performanslı)
- **// TODO:** MediatR lisans uyarısı — production'da Lucky Penny lisansı gerekecek

---

## 9. Kısa Özet (Summary)

Bu bölümde Catalog.API'nin tüm CRUD operasyonlarını Vertical Slice Architecture ile uyguladık. Her feature (GetProducts, GetProductById, CreateProduct, UpdateProduct, DeleteProduct) kendi klasöründe handler, endpoint ve validator ile yaşıyor. CQRS pattern'i ile okuma ve yazma sorumlulukları ayrıldı; Result pattern ile exception'sız hata yönetimi sağlandı. Carter ile Minimal API endpoint'leri modüler organize edildi. Generic LoggingBehavior ve ValidationBehavior pipeline'a eklenerek cross-cutting concerns merkezi ve reusable hale getirildi.

---

## 10. Ne Öğrendim? (What I Learned) — 3 Madde

1. **Vertical Slice Architecture** feature bazlı organizasyon sağlar — bir feature'ın tüm dosyaları (handler, endpoint, validator, DTO) aynı klasörde yaşar. Katmanlı mimarideki "5 klasör arası zıplama" sorunu ortadan kalkar.

2. **Pipeline Behaviors** MediatR'ın middleware sistemidir — generic olarak yazıldığında tüm handler'lara otomatik uygulanır. LoggingBehavior her request'i loglar, ValidationBehavior handler'a ulaşmadan önce validation yapar. `IResultBase` constraint'i ile hem `Result` hem `Result<T>` handler'ları tek behavior'la handle edilir.

3. **Result pattern + Endpoint mapping** birlikte çalışır — handler `Result<T>` döner, endpoint `IsSuccess` kontrolü ile HTTP status code'a çevirir. Exception fırlatma maliyeti yok, compiler tip güvenliği var, ve her endpoint'in başarı/hata senaryoları `.Produces<T>()` ile dokümante edilir.

---

## 11. Öğrenme Pekiştirme (Reinforcement)

### Mini Quiz (10 Soru)

**S1 (Doğru/Yanlış):** Vertical Slice Architecture'da her feature kendi klasöründe yaşar ve feature'lar arası bağımlılık olmamalıdır.

**S2 (Kısa Cevap):** `ICommand<CreateProductResult>` handler'ı hangi tip döner?

**S3 (Senaryo):** Handler'da `session.Store(product)` çağırdın ama `SaveChangesAsync()` çağırmadın. Ne olur?

**S4 (Doğru/Yanlış):** DeleteProduct handler'ı `ICommand` (generic parametresiz) kullanır çünkü silme işlemi değer döndürmez.

**S5 (Kısa Cevap):** Endpoint'te `IMediator` yerine neden `ISender` kullanıyoruz?

**S6 (Senaryo):** CreateProductValidator tanımladın ama Program.cs'e `AddValidatorsFromAssembly()` eklemedin. Ne olur?

**S7 (Kısa Cevap):** LoggingBehavior hangi durumda `Warning` seviyesinde log yazar?

**S8 (Doğru/Yanlış):** ValidationBehavior'da validator bulunamazsa request reject edilir.

**S9 (Senaryo):** POST `/api/products` ile `"price": -5` gönderdin. HTTP response ne olur ve handler çalışır mı?

**S10 (Kısa Cevap):** REST convention'a göre başarılı bir DELETE isteği hangi HTTP status code döner ve neden body yoktur?

---

### Görevler (3 Adet)

**Görev 1 — DeleteProduct Validator Ekle:**
DeleteProductCommand için bir validator yaz. Kural: `Id` boş (empty Guid) olamaz. Postman'den `00000000-0000-0000-0000-000000000000` ile test et — validation hatası dönmeli.

**Görev 2 — Pagination Test:**
Postman'den `GET /api/products?pageNumber=1&pageSize=2` çağır. Response'da `totalCount: 5` ama sadece 2 ürün listeli olmalı. `pageNumber=3` ile boş sayfa dön — products array boş, totalCount hala 5.

**Görev 3 — GetProductsByCategory Query Ekle:**
Yeni bir feature folder `Features/GetProductsByCategory/` oluştur. `GET /api/products/category/{category}` endpoint'i ile belirli kategorideki ürünleri listele. Marten LINQ: `session.Query<Product>().Where(p => p.Categories.Contains(category))`.

---

### Cevap Anahtarı

**S1:** ✅ Doğru. Her feature bağımsızdır, sadece BuildingBlocks'taki shared yapılara bağımlılık olabilir.

**S2:** `Task<Result<CreateProductResult>>`. `ICommand<T>` → `IRequest<Result<T>>` wrap'ı nedeniyle handler her zaman `Result<T>` döner.

**S3:** Ürün DB'ye **yazılmaz**. `Store()` Marten'ın Unit of Work'üne ekler ama transaction commit olmaz. `SaveChangesAsync()` çağrılmadan değişiklik kaybolur.

**S4:** ✅ Doğru. `ICommand` (parametresiz) → handler `Task<Result>` döner. `ICommand<T>` olsaydı `Task<Result<T>>` dönecekti.

**S5:** Interface Segregation Principle. Endpoint sadece `Send()` yapıyor, `Publish()` veya `CreateStream()`'e ihtiyacı yok. `ISender` daha minimal bir interface.

**S6:** DI container `IValidator<CreateProductCommand>` çözemez. ValidationBehavior inject ettiği `IEnumerable<IValidator<T>>` boş gelir → `.Any()` false → direkt `next()` çağrılır → **validation atlanır**, handler çalışır.

**S7:** Request süresi **500ms'yi aştığında**. `if (sw.ElapsedMilliseconds > 500)` kontrolü ile yavaş query'ler tespit edilir.

**S8:** ❌ Yanlış. Validator yoksa `validators.Any()` false döner ve direkt `next()` çağrılır — request handler'a normal şekilde ulaşır. Bu **opt-in** pattern'dir.

**S9:** HTTP 400 Bad Request döner, response body'de `"Price must be greater than zero."` mesajı bulunur. Handler **çalışmaz** — ValidationBehavior hata tespit eder ve Result.Failure döner, pipeline handler'a ulaşmadan kesilir.

**S10:** 204 No Content. Silme işlemi başarılı olduğunda dönecek anlamlı bir veri yoktur — kaynak zaten silindi. Body göndermek gereksiz bandwidth kullanımı olur. REST convention: DELETE başarılıysa 204, POST oluşturma başarılıysa 201 + Location header.
