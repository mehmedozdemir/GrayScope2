# GrayScope 2

Birden fazla Graylog sunucusunda **parametrik sorgular** çalıştırmak, sonuçları incelemek ve
analiz etmek için bir masaüstü aracı. Örneğin "şehir bazlı 200 dışı yanıtlar" gibi önceden
tanımlı bir sorguyu bir veya birden çok şehir (NetworkId) için tek tıkla çalıştırırsınız,
sonuçları tabloda süzer, zaman histogramıyla inceler ve Excel'e aktarırsınız.

GrayScope 1'in (Python/PySide6) .NET 10 + Avalonia 12 ile yeniden yazılmış halefidir.

## Öne çıkanlar

- **Gelişmiş editör:** Lucene sözdizimi renklendirme, alan adı ve operatör otomatik tamamlama (Ctrl+Space)
- **Parametreler:** `{NetworkId}`, opsiyonel `{Kaynak?}`, varsayılanlı `{Seviye:3}`
- **Çoklu sekme:** Her sorgu kendi sekmesinde, bağımsız ve iptal edilebilir şekilde çalışır
- **Toplu çalıştırma:** `34, 35, 6` × birden çok sunucu, paralel çalışır ve tek tabloda birleşir
- **Canlı takip:** 5–60 sn aralıkla yeniler, yeni kayıtları vurgular
- **Analiz:** Zaman histogramı ve alan değer dağılımı; tıklayınca tabloyu filtreler
- **Mesaj ayrıntısı:** Tüm alanlar, "sorguya ekle / hariç tut", JSON/XML görüntüleyici
- **Dışa aktarma:** CSV ve Excel
- **Geçiş:** GrayScope 1 veritabanını tek adımda içe aktarma (token'lar dahil)
- **Güvenlik:** Token'lar Windows DPAPI ile şifreli saklanır

## Gereksinimler

| Araç | Sürüm |
|---|---|
| .NET SDK | 10.0.403 veya üstü 10.0.x (`global.json`) |
| İşletim sistemi | Windows 10/11 (Avalonia sayesinde Linux/macOS'ta da derlenir) |
| Graylog | 2.x ve üzeri (5.x+ Scripting API; eski sürümlerde otomatik olarak universal search) |

## Hızlı başlangıç

```powershell
git clone <repo> GrayScope2
cd GrayScope2
dotnet run --project src/GrayScope.App
```

İlk açılışta **Ayarlar → Graylog Profilleri** ile bir sunucu ekleyin (adres ve API token). Ardından
"Bağlantıyı Test Et" ile doğrulayın. GrayScope 1 kullanıyorsanız **Ayarlar → Veri ve Görünüm →
GrayScope 1 veritabanını içe aktar** ile `GrayScope\data\grayscope.db` dosyasını seçin.
`~/.grayscope/secret.key` mevcutsa token'lar da otomatik taşınır.

## Derleme, test ve yayınlama

```powershell
dotnet build                 # uyarılar hata sayılır
dotnet test                  # xUnit v3, Microsoft.Testing.Platform
./scripts/publish.ps1        # tek dosya, self-contained win-x64 → publish/win-x64
```

GitHub Release: `CHANGELOG.md`'ye `## [x.y.z] - YYYY-AA-GG` bölümünü ekleyip commit'leyin, ardından
`git tag vx.y.z && git push origin vx.y.z`. `Release` workflow'u testleri çalıştırır, win-x64 zip'ini
(+ SHA-256) üretir ve notları CHANGELOG'daki bölümden alarak sürümü yayınlar. `-` içeren etiketler
(ör. `v0.2.0-beta.1`) ön sürüm olarak işaretlenir.

## Yapılandırma

| Değişken | Açıklama | Varsayılan |
|---|---|---|
| `GRAYSCOPE2_DATA_DIR` | Veritabanı ve log klasörü | `%LOCALAPPDATA%\GrayScope2` |

Gizli bilgi ortam değişkenlerinde tutulmaz. Token'lar uygulamada girilir ve DPAPI ile şifrelenir.

## Mimari

```
GrayScope.App  (Avalonia, MVVM)  ──►  GrayScope.Core  (iş kuralları, arayüzler)
        │                                   ▲
        └──────────►  GrayScope.Infrastructure  (SQLite, Graylog HTTP, DPAPI, Excel)
```

Ayrıntılar: [docs/PROJECT_PLAN.md](docs/PROJECT_PLAN.md) ·
[docs/DESIGN_SYSTEM.md](docs/DESIGN_SYSTEM.md) · [docs/adr/](docs/adr/)

## Katkı

- Branch: `feature/*`, `fix/*`, `docs/*` → `main` (doğrudan `main`'e commit yok)
- Commit: Conventional Commits, ör. `feat(editor): add field completion`
- PR: CI yeşil olmalı (format, build, test, güvenlik açığı taraması). `feat`/`fix` için CHANGELOG güncellenir.
