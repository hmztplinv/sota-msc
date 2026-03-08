# Bölüm 8: Docker Compose Orchestration

> **Tarih:** 2026-03-08  
> **Ön Koşul:** Bölüm 7 (Basket → Discount gRPC client entegrasyonu tamamlanmış)  
> **Konu:** Tüm servislerin Docker Compose ile containerize edilmesi, service discovery, health checks

---

## 1. Amaç & Kazanımlar

- Docker Compose ile tüm mikroservisleri **tek komutla** ayağa kaldırmak
- **Docker DNS** ile servisler arası iletişimi sağlamak (container adı = hostname)
- **Environment variable override** ile appsettings.json değerlerini Docker ortamına uyarlamak
- **Health check + depends_on** ile servis başlatma sırasını garanti etmek
- gRPC servislerinin Docker container'da health check sorunlarını çözmek

---

## 2. Kavramlar & Tanımlar

### Docker DNS (Service Discovery)

Docker Compose aynı network'teki container'ları otomatik DNS ile birbirine bağlar:

```
basket-api container'ı içinde:
  "discount-grpc" → 172.18.0.5 (Docker otomatik resolve eder)
  "redis"         → 172.18.0.3
  "postgres"      → 172.18.0.2
```

**Kural:** `container_name` = hostname. URL'de container adını kullan:
- `http://discount-grpc:8080` (internal port, external değil!)
- `redis:6379`
- `Host=postgres;Port=5432`

### Environment Variable Override (.NET)

.NET'te `__` (çift alt çizgi) → `:` dönüşümü yapar:

```
Environment Variable:          .NET Configuration'da karşılığı:
─────────────────────          ───────────────────────────────
GrpcSettings__DiscountUrl  →   GrpcSettings:DiscountUrl
ConnectionStrings__Redis   →   ConnectionStrings:Redis
ConnectionStrings__Database →  ConnectionStrings:Database
```

**Öncelik sırası (yüksekten düşüğe):**
1. Environment variable (Docker Compose)
2. appsettings.{Environment}.json
3. appsettings.json

### depends_on + condition

```yaml
basket-api:
  depends_on:
    redis:
      condition: service_healthy      # Redis healthy olana kadar bekle
    discount-grpc:
      condition: service_healthy      # Discount healthy olana kadar bekle
```

**Sadece `depends_on` (condition olmadan):** Container'ın start olmasını bekler ama healthy olmasını beklemez — uygulama henüz hazır olmayabilir.

---

## 3. Neden Böyle? Mimari Gerekçe

### Neden Docker DNS (Consul/Eureka değil)?

| Alternatif | Trade-off |
|-----------|-----------|
| **Docker DNS** | Zero config, built-in, öğrenme odağını karmaşıklaştırmaz |
| Consul/Eureka | Service registry, dynamic discovery ama ek altyapı |
| Kubernetes DNS | Production-grade ama öğrenme eğrisi yüksek |

Docker Compose seviyesinde Docker DNS yeterli. Production K8s'e geçildiğinde Kubernetes DNS aynı mantıkla çalışır.

### Neden Internal Port (8080)?

```
Dış dünya → localhost:5002 → basket-api container:8080
                                    │
                                    └── http://discount-grpc:8080 (internal)
                                                    │
                                         discount-grpc container:8080
```

Container'lar arası iletişimde **internal port** kullanılır (8080). External port (5001, 5002, 5003) sadece host makineden erişim içindir.

---

## 4. Adım Adım Uygulama

### 4.1 Discount.Grpc'ye Health Check Endpoint Ekleme

**Sorun:** Discount.Grpc'de `/health` endpoint'i yoktu → Docker `depends_on: condition: service_healthy` kullanılamıyordu.

**Program.cs'e eklenen:**

```csharp
app.MapGet("/health", () => Results.Ok("Healthy"));
```

### 4.2 Kestrel Protocol Sorunu ve Çözümü

**Sorun zinciri:**

1. Discount.Grpc `"Protocols": "Http2"` — sadece HTTP/2 kabul ediyor
2. Docker health check `curl -f http://localhost:8080/health` — HTTP/1.1 ile istek atıyor
3. HTTP/2 only server, HTTP/1.1 isteği → **400 Bad Request** → container **unhealthy**

**Denenen çözüm 1 — `Http1AndHttp2`:**
```json
"Protocols": "Http1AndHttp2"
```
**Sonuç:** Health check çalıştı ama gRPC çağrıları bozuldu. TLS olmadan (h2c) `Http1AndHttp2` modunda server HTTP/1.1'e düşüyor, gRPC HTTP/2 zorunlu olduğu için `HTTP_1_1_REQUIRED` hatası.

**Doğru çözüm — `Http2` + `curl --http2-prior-knowledge`:**
```json
"Protocols": "Http2"
```
```yaml
healthcheck:
  test: ["CMD-SHELL", "curl --http2-prior-knowledge -f http://localhost:8080/health || exit 1"]
```

**`--http2-prior-knowledge` ne yapar:** TLS/ALPN negotiation olmadan doğrudan HTTP/2 (h2c) ile bağlanır. gRPC servislerinin health check'i için standart yöntem.

**Öğrenilen kural:**

| Senaryo | Kestrel Protocol | Health Check |
|---------|-----------------|--------------|
| gRPC only (TLS yok) | `Http2` | `curl --http2-prior-knowledge` |
| gRPC + REST (TLS var) | `Http1AndHttp2` | `curl -f` (normal) |
| REST only | `Http1` veya `Http1AndHttp2` | `curl -f` (normal) |

### 4.3 docker-compose.yml — basket-api Güncellemeleri

**Eklenen environment variable:**

```yaml
- GrpcSettings__DiscountUrl=http://discount-grpc:8080
```

**Eklenen dependency:**

```yaml
depends_on:
  redis:
    condition: service_healthy
  discount-grpc:
    condition: service_healthy
```

### 4.4 Dockerfile — curl Kurulumu

Discount.Grpc Dockerfile'ına curl eklendi (health check için gerekli):

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
```

**Not:** Catalog.API ve Basket.API Dockerfile'larında curl zaten mevcuttu.

---

## 5. Nihai Docker Compose Yapısı

### Servis Haritası

```
┌─────────────────────────────────────────────────────────┐
│                    eshop-network                        │
│                                                         │
│  ┌──────────┐  ┌──────────┐  ┌──────────────┐          │
│  │ postgres │  │  redis   │  │              │          │
│  │  :5432   │  │  :6379   │  │              │          │
│  └────┬─────┘  └────┬─────┘  │              │          │
│       │              │        │              │          │
│       ▼              ▼        │              │          │
│  ┌──────────┐  ┌──────────┐  │  ┌────────────┐         │
│  │ catalog  │  │ basket   │──┼─→│ discount   │         │
│  │  -api    │  │  -api    │  │  │   -grpc    │         │
│  │ :5001    │  │ :5002    │  │  │  :5003     │         │
│  └──────────┘  └──────────┘  │  └────────────┘         │
│                              │       │                  │
│                              │       ▼                  │
│                              │  ┌──────────┐           │
│                              │  │ postgres │           │
│                              │  │ (shared) │           │
│                              │  └──────────┘           │
│                              │                          │
└─────────────────────────────────────────────────────────┘
```

### Port Tablosu

| Servis | Internal Port | External Port | Health Check |
|--------|---------------|---------------|--------------|
| postgres | 5432 | 5432 | `pg_isready` |
| redis | 6379 | 6379 | `redis-cli ping` |
| catalog-api | 8080 | 5001 | `curl -f http://localhost:8080/health` |
| basket-api | 8080 | 5002 | `curl -f http://localhost:8080/health` |
| discount-grpc | 8080 | 5003 | `curl --http2-prior-knowledge -f http://localhost:8080/health` |

### Başlatma Sırası (depends_on zinciri)

```
postgres ──healthy──→ catalog-api
postgres ──healthy──→ discount-grpc ──healthy──→ basket-api
redis ────healthy──→ basket-api
```

---

## 6. Sık Hatalar & Çözümleri

| Hata | Neden | Çözüm |
|------|-------|-------|
| `container discount-grpc is unhealthy` | curl HTTP/1.1 ile istek, Kestrel Http2 only | `curl --http2-prior-knowledge` kullan |
| `HTTP_1_1_REQUIRED (0xd)` | `Http1AndHttp2` + TLS yok → server HTTP/1.1'e düşüyor | Kestrel'i `Http2` yap, client h2c ile bağlansın |
| `docker compose up -d` → `no configuration file` | Yanlış dizin | `cd ~/sota-eshop-microservices/docker` |
| Health check `curl: not found` | Container image'da curl yüklü değil | Dockerfile'a `RUN apt-get install -y curl` ekle |
| Docker build cache sorunu | Dockerfile değişikliği algılanmıyor | `docker compose build --no-cache <service>` |
| gRPC connection refused | Yanlış URL veya port | Docker DNS: `http://container-name:internal-port` |

---

## 7. Best Practices

1. **Internal port kullan** — Container'lar arası iletişimde her zaman internal port (8080), external port (5001-5003) sadece host erişimi için
2. **Environment variable ile override** — Hardcode URL yok, `GrpcSettings__DiscountUrl` Docker Compose'da, `GrpcSettings:DiscountUrl` appsettings.json'da
3. **Health check zorunlu** — `depends_on: condition: service_healthy` ile başlatma sırası garanti et
4. **gRPC health check** — `Http2` only server'larda `curl --http2-prior-knowledge` kullan
5. **`--no-cache` ile rebuild** — Dockerfile değişikliklerinde cache sorunlarını önle
6. **Shared network** — Tüm servisler aynı `eshop-network`'te olmalı

---

## 8. TODO / Tartışma Notları

- [ ] **Observability** — Prometheus + Grafana + Jaeger container'larını ekle
- [ ] **RabbitMQ** — Message broker container'ı ekle (Bölüm 14)
- [ ] **SQL Server** — Ordering servisi için container ekle (Bölüm 11)
- [ ] **docker-compose.override.yml** — Dev vs Production ayrımı
- [ ] **Volume management** — PostgreSQL data persistence strategy
- [ ] **Log aggregation** — Serilog → Seq container'ı

---

## 9. Kısa Özet (Summary)

Bölüm 8 ile tüm mikroservisler (Catalog.API, Basket.API, Discount.Grpc) Docker Compose üzerinden tek komutla ayağa kaldırıldı. Docker DNS ile servisler arası iletişim sağlandı — Basket.API `http://discount-grpc:8080` üzerinden Discount.Grpc'ye gRPC çağrısı yapıyor. Environment variable override ile Docker ortamına özel yapılandırma (`GrpcSettings__DiscountUrl`) uygulandı. gRPC servislerinin health check sorunu (Http2 only + curl HTTP/1.1) `curl --http2-prior-knowledge` ile çözüldü.

---

## 10. Ne Öğrendim? (What I Learned) — 3 Madde

1. **Docker DNS ile service discovery** sıfır konfigürasyon gerektirir — aynı network'teki container'lar birbirini container adıyla (hostname) bulur; URL'de internal port kullanılır (external port sadece host erişimi).

2. **.NET environment variable override** `__` (çift alt çizgi) → `:` dönüşümü yapar; Docker Compose'daki `GrpcSettings__DiscountUrl` otomatik olarak `appsettings.json`'daki `GrpcSettings:DiscountUrl` değerini ezer — kod değişikliği gerekmez.

3. **gRPC servisleri TLS olmadan (h2c) Http2 only çalıştığında** standart `curl` (HTTP/1.1) health check başarısız olur; `curl --http2-prior-knowledge` ile doğrudan HTTP/2 bağlantısı kurularak sorun çözülür — bu gRPC container health check'lerinin standart yöntemidir.

---

## 11. Öğrenme Pekiştirme (Reinforcement)

### Mini Quiz (8 soru)

**S1:** Docker Compose'da `basket-api` container'ı `discount-grpc`'ye nasıl ulaşır? URL nedir?

**S2:** `GrpcSettings__DiscountUrl` environment variable'ı .NET tarafında hangi configuration key'e map olur?

**S3:** `depends_on` ile `depends_on: condition: service_healthy` arasındaki fark nedir?

**S4:** Kestrel `"Protocols": "Http2"` iken neden `curl -f http://localhost:8080/health` başarısız olur?

**S5:** `Http1AndHttp2` neden TLS olmadan gRPC çağrılarını bozar?

**S6:** Container'lar arası iletişimde external port (5003) mı yoksa internal port (8080) mi kullanılır? Neden?

**S7:** `docker compose build --no-cache` ne zaman gereklidir?

**S8:** Health check `start_period: 15s` ne işe yarar?

### Cevap Anahtarı

**C1:** Docker DNS ile container adını hostname olarak kullanır: `http://discount-grpc:8080`. Aynı `eshop-network`'te olduğu için Docker otomatik DNS resolution yapar.

**C2:** `GrpcSettings:DiscountUrl`. .NET'te `__` → `:` dönüşümü otomatik yapılır. Bu değer `appsettings.json`'daki aynı key'i override eder.

**C3:** Sadece `depends_on`: Container'ın start olmasını bekler (process çalışıyor). `condition: service_healthy`: Container'ın health check'ini geçmesini bekler (uygulama hazır). Fark önemli — DB container start olabilir ama henüz bağlantı kabul etmiyor olabilir.

**C4:** `curl` default olarak HTTP/1.1 ile istek atar. Kestrel `Http2` modunda HTTP/1.1 isteklerini reddeder → 400 Bad Request. Çözüm: `curl --http2-prior-knowledge` ile doğrudan HTTP/2 (h2c) bağlantısı.

**C5:** TLS olmadan `Http1AndHttp2` modunda HTTP/2 negotiation (ALPN) yapılamaz → server HTTP/1.1'e fallback eder → gRPC HTTP/2 zorunlu olduğu için `HTTP_1_1_REQUIRED` hatası alır.

**C6:** Internal port (8080). External port (5003) sadece host makineden erişim içindir. Container'lar Docker network üzerinden doğrudan internal port'a bağlanır.

**C7:** Dockerfile değiştiğinde Docker build cache eski layer'ları kullanabilir. `--no-cache` tüm layer'ları sıfırdan build eder. Özellikle `RUN apt-get install` gibi OS-level değişikliklerde gerekli.

**C8:** Container start olduktan sonra ilk 15 saniye health check başarısız olsa bile unhealthy sayılmaz. Uygulamanın başlangıç süresine (migration, warmup) tolerans tanır.

### Görevler

**Görev 1 — Tüm Health Check'leri Doğrula:**
```bash
curl -s http://localhost:5001/health
curl -s http://localhost:5002/health
curl -s http://localhost:5003/health  # Bu neden farklı çalışır?
```

**Görev 2 — Docker Logs İnceleme:**
```bash
docker logs basket-api 2>&1 | grep -i "discount\|grpc"
```
Basket.API loglarında Discount.Grpc çağrısını görebiliyor musun?

**Görev 3 — Discount Container'ı Durdur ve Basket'ı Test Et:**
1. `docker stop discount-grpc`
2. `curl` ile StoreBasket çağır — ne hatası alıyorsun?
3. `docker start discount-grpc`
4. Health check'in geçmesini bekle
5. StoreBasket tekrar çağır — çalışıyor mu?
