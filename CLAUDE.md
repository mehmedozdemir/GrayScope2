# CLAUDE.md — GrayScope 2

Bu dosya, Claude Code'un bu projede uyması gereken kuralları tanımlar. Global
`engineering-standards` skill'ini override etmez, üzerine proje özel kurallar ekler. Çakışma
olursa bu dosya önceliklidir.

## 1. Zorunlu okuma sırası (her görev başında)

1. `engineering-standards` skill'i
2. `docs/PROJECT_PLAN.md`: kapsam, veri modeli, hata senaryoları, kabul kriterleri
3. `docs/DESIGN_SYSTEM.md`: token'lar, stil sınıfları, ekran yerleşimleri
4. `docs/adr/`: alınmış mimari kararlar
5. Bu dosyanın kalanı

## 2. Proje özeti

GrayScope 2, çoklu Graylog sunucusunda parametrik sorgu çalıştıran bir masaüstü aracıdır
(.NET 10 + Avalonia 12). GrayScope 1'in (Python/PySide6, `C:\AI\GrayScope`) halefidir.
GrayScope 1 veritabanını ve yedeklerini içe aktarabilir.

## 3. Teknoloji yığını

| Katman | Teknoloji |
|---|---|
| Dil / çalışma zamanı | C# (latest), .NET 10 LTS (`global.json` ile sabit) |
| UI | Avalonia 12 (Fluent), Avalonia.Controls.DataGrid, AvaloniaEdit |
| MVVM | CommunityToolkit.Mvvm (source generator) |
| DI | Microsoft.Extensions.DependencyInjection (kompozisyon kökü: `App.axaml.cs`) |
| Veritabanı | SQLite (Microsoft.Data.Sqlite + Dapper) |
| Şifreleme | Windows DPAPI (CurrentUser); diğer OS'lerde AES-GCM + 0600 anahtar dosyası |
| Excel | ClosedXML |
| Test | xUnit v3 (Microsoft.Testing.Platform) |

Paket sürümleri yalnızca `Directory.Packages.props` içinde sabitlenir ve `packages.lock.json`
dosyaları commit edilir. Yeni bağımlılık eklemeden önce mevcut kütüphanelerle çözülüp
çözülemeyeceği değerlendirilir.

## 4. Proje yapısı

```
src/
  GrayScope.Core/            Saf iş mantığı. UI, SQLite ve HTTP referansı YOK.
    Domain/                  Modeller (GraylogProfile, SavedQuery, QueryFolder, QueryRun, TimeRangeSpec)
    Abstractions/            Repository, ISecretProtector, IGraylogClient arayüzleri
    Querying/                QueryTemplate (parametre motoru), LuceneTokenizer, LuceneTerms
    Formatting/              ValueFormatter (JSON/XML, DTD kapalı)
    Services/                Execution, Catalog, Profile, Query, Backup, MessageDetail, ResultAnalyzer
  GrayScope.Infrastructure/  Adaptörler
    Persistence/             Database (migrasyonlar), repository'ler
    Graylog/                 GraylogClient (5.x CSV + 2.x universal), CsvParser
    Security/                LocalSecretProtector (DPAPI), FernetTokenDecryptor (GrayScope 1)
    Export/                  CSV / Excel
    Legacy/                  GrayScope 1 veritabanı okuyucu
  GrayScope.App/             Avalonia
    Themes/                  Tokens.axaml, Controls.axaml
    Services/                Dialog, Toast, Theme, Clipboard, FilePicker
    ViewModels/              Main, QueryTab, Results, QueryEditor, Settings, TimeRangeEditor
    Views/                   MainWindow, QueryTabView, QueryEditorWindow, SettingsWindow, TimeRangeEditor
    Controls/                LuceneEditor, HistogramControl
tests/                       src ile aynı yapı (Core.Tests, Infrastructure.Tests)
docs/                        PROJECT_PLAN, DESIGN_SYSTEM, adr/
```

## 5. Katman kuralları

- `App` → yalnızca `Core` servisleri ve `Infrastructure` içindeki saf yardımcılar (Export, Legacy okuyucu).
  ViewModel'ler repository'lere veya `IGraylogClient`'a **doğrudan** erişmez.
- `Core` hiçbir dış pakete bağımlı değildir. Yeni iş kuralları buraya yazılır ve test edilir.
- `Infrastructure` iş kuralı içermez; yalnızca HTTP/SQL kurar, gönderir ve parse eder.
- Kullanıcıya gösterilen beklenen hatalar `GrayScopeException` türevleridir ve mesajları Türkçedir.
  Beklenmeyen hatalar yutulmaz.

## 6. Güvenlik

- Token yalnızca `ISecretProtector` ile korunmuş olarak saklanır (`TokenProtected`). Düz metin
  token yalnızca bellekte, istek kurulurken kullanılır.
- Token hiçbir log, exception mesajı veya UI'da görünmez. Gerekirse `ProfileService.MaskToken`
  (`****1234`) kullanılır.
- Kullanıcı parametre değerleri `ParameterValueValidator` ile denetlenir (kontrol karakteri yok,
  dengeli tırnak, en fazla 1000 karakter).
- "Sorguya ekle" terimleri `LuceneTerms` ile kaçışlanır; string birleştirme ile Lucene terimi kurulmaz.
- Log içerikleri güvenilmezdir: XML biçimlendirmede DTD yasaktır, harici kaynak çözülmez.
- SQL yalnızca parametreli sorgularla yazılır.

## 7. Kodlama standartları

- Sınıf, metot ve alan adları İngilizce; UI metinleri Türkçe.
- `TreatWarningsAsErrors` açıktır ve analizör seviyesi `latest-recommended`'dır. Uyarı bastırmak
  yerine kod düzeltilir.
- XAML'de compiled binding varsayılandır; her view `x:DataType` belirtir.
- Renk ve boyutlar yalnızca token'lardan gelir. Yeni ekran veya bileşen önce `DESIGN_SYSTEM.md`'ye işlenir.
- Async: Core ve Infrastructure `ConfigureAwait(false)` kullanır. Uzun işlemler `CancellationToken` alır.
- Veritabanı şeması yalnızca `Database.Migrations` dizisine **yeni** kayıt ekleyerek değişir.

## 8. Definition of Done

- [ ] İlgili kabul kriterleri (`PROJECT_PLAN.md` §7) karşılanıyor
- [ ] Yükleme, boş ve hata durumları tasarlanmış; ikon butonlarda tooltip ve erişilebilir ad var
- [ ] En az bir happy-path ve bir edge-case testi yazıldı (`dotnet test` yeşil)
- [ ] `dotnet build` uyarısız, `dotnet format --verify-no-changes` temiz
- [ ] Token hiçbir çıktıda düz metin görünmüyor
- [ ] `feat`/`fix` için CHANGELOG.md güncellendi

## 9. Çalıştırma ve test

```powershell
dotnet build
dotnet test
dotnet run --project src/GrayScope.App
# Gerçek veriye dokunmadan denemek için:
$env:GRAYSCOPE2_DATA_DIR = "$env:TEMP\gs2-dev"; dotnet run --project src/GrayScope.App
```

UI değişikliklerini teslim etmeden önce `tools/dev/start-demo.ps1` (mock Graylog + izole veri) ile
çalıştırıp ekran görüntüsüyle kontrol edin; araçlar `tools/dev/README.md` içinde. Kullanıcının gerçek
Graylog sunucularına izin almadan istek atılmaz.

## 10. Sorulması gereken durumlar

Aşağıdaki durumlarda kod yazmadan önce kullanıcıya sorulur:

- Graylog sürümleri arasında API farkı şüphesi varsa
- Bir ekranın yerleşimi `DESIGN_SYSTEM.md`'de tanımlı değilse
- `PROJECT_PLAN.md`'de olmayan yeni bir tablo veya alan gerekiyorsa
- Yeni bir NuGet bağımlılığı gerekiyorsa
