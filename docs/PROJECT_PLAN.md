# PROJECT_PLAN.md — GrayScope 2

## 1. Amaç

Operasyon ve destek ekibinin, birden çok Graylog sunucusunda önceden tanımlanmış **parametrik**
sorguları çalıştırmasını sağlamak. Sorgular tek tek ya da toplu (çok değer × çok sunucu) olarak,
gerekirse canlı takiple çalıştırılır. Sonuçlar tek bir masaüstü uygulamasında incelenir, analiz
edilir ve dışa aktarılır.

GrayScope 1'in (Python/PySide6) tüm yeteneklerini korur ve şunları ekler: gelişmiş editör,
çoklu sekme, toplu/paralel çalıştırma, canlı takip ve sonuç analizi.

## 2. Kapsam

### Sürüm 0.1 (bu doküman)

| Alan | Yetenek |
|---|---|
| Profiller | Çoklu Graylog sunucusu, DPAPI ile şifreli token, bağlantı testi (sürüm bilgisiyle), varsayılan/pasif profil |
| Sorgu kütüphanesi | İç içe klasörler, arama (ad + metin), kopyalama, taşıma, açıklama |
| Editör | Lucene sözdizimi renklendirme, alan adı ve operatör otomatik tamamlama (Ctrl+Space) |
| Parametreler | `{Ad}`, `{Ad?}`, `{Ad:varsayılan}`, `{Ad?:varsayılan}` (GrayScope 1 ile uyumlu) |
| Çalıştırma | Çoklu sekme, sekme başına bağımsız iptal edilebilir çalıştırma, Ctrl+Enter / F5 |
| Toplu çalıştırma | Virgülle çok değer × çok sunucu; en fazla 4 paralel arama, en fazla 50 kombinasyon |
| Canlı takip | 5 / 10 / 30 / 60 sn aralık, yeni kayıt vurgusu, 5.000 satır tampon |
| Sonuç tablosu | Ham değere göre sıralama, vurgulayan veya filtreleyen arama (`*` `?`), filtre çipleri |
| Mesaj ayrıntısı | Tüm alanlar (5.x'te tembel yükleme), "sorguya ekle / hariç tut", kopyala, büyük görüntüleyici |
| Analiz | Zaman histogramı (tıklayınca filtreler), alan değer dağılımı (tıklayınca filtreler) |
| Değer görüntüleyici | JSON/XML biçimlendirme (DTD kapalı), kopyala |
| Dışa aktarma | CSV (UTF-8 BOM), Excel (başlık, filtre, sabit ilk satır), görünen satırlar ve görünen sıra |
| Geçmiş | Sorgu başına son 100 çalıştırma (zaman, sayı, süre, parametreler); tıklayınca parametreleri uygular |
| Veri | JSON yedekleme/geri yükleme (v3; GrayScope 1 v1/v2 okunur), GrayScope 1 veritabanından içe aktarma |
| Görünüm | Açık/koyu tema (kalıcı), açık sekmeler bir sonraki açılışta geri yüklenir |

### Backlog (sonraki sürümler)

- Graylog tarafı agregasyon (`/search/aggregate`) ile büyük veri setlerinde histogram
- Kaydedilmiş görünüm: kolon sırası/genişliği sorguyla birlikte saklama
- Kendinden imzalı sertifika için profil bazlı güven ayarı (bkz. §8)
- Sürükle-bırak ile ağaçta taşıma ve sıralama
- Kurulum paketi (MSIX veya Inno Setup)

## 3. Graylog Entegrasyonu

### 3.1 Desteklenen sürümler

| Sürüm | Arama yolu | Not |
|---|---|---|
| ≥ 5.x | `POST /api/search/messages` (Accept: `text/csv`) | Yalnızca istenen alanlar döner; `_id`, `gl2_source_index`, `timestamp` her zaman eklenir |
| 2.x–4.x | `GET /api/search/universal/{relative,absolute,keyword}` | Tüm alanlar döner |

Sürüm otomatik algılanır: scripting endpoint'i 404/405 dönerse universal API'ye geçilir ve bu bilgi
sunucu adresi bazında oturum boyunca hatırlanır.

### 3.2 Diğer endpoint'ler

| Amaç | Endpoint |
|---|---|
| Bağlantı testi | `GET /api/system` (sürüm ve hostname gösterilir) |
| Stream listesi | `GET /api/streams` (profil bazlı önbellek, formda "Yenile") |
| Alan adları (tamamlama) | `GET /api/system/fields` (profil bazlı önbellek) |
| Mesaj ayrıntısı | `GET /api/messages/{index}/{id}`; index `-` ise universal arama ile bulunur |

### 3.3 Kimlik doğrulama ve zaman aşımı

- Basic Auth `(token, "token")`, `X-Requested-By: GrayScope2`.
- Zaman aşımı: meta veri çağrıları için 10 sn, aramalar için 60 sn. Her çağrı kullanıcı tarafından iptal edilebilir.

### 3.4 Sunucular arası çalıştırma

Stream ID'leri sunucuya özgüdür. Sorgu başka bir sunucuda çalıştırılınca stream'ler **başlığa göre**
eşleştirilir. Eşleşmeyenler için uyarı gösterilir. Hiçbiri eşleşmezse o hedef hata verir; arama
sessizce "tüm stream'ler"e genişletilmez.

## 4. Veri Modeli (SQLite, `%LOCALAPPDATA%\GrayScope2\grayscope2.db`)

Şema `PRAGMA user_version` ile sürümlenir (`Database.Migrations`). Yayımlanmış bir migrasyon asla
değiştirilmez; yalnızca yenisi eklenir.

| Tablo | Alanlar |
|---|---|
| `GraylogProfile` | Id, Name (unique, nocase), BaseUrl (`.../api`), TokenProtected (`dpapi:` / `aesgcm:`), IsDefault, IsActive, CreatedAt, UpdatedAt |
| `QueryFolder` | Id, Name, ParentId (→ cascade), Position |
| `SavedQuery` | Id, Name (unique), ProfileId, FolderId (→ set null), Position, Template, Description, TimeRangeKind, RangeSeconds, RangeFrom, RangeTo, RangeKeyword, FieldsJson, DefaultSortField, DefaultSortDirection, ResultSize (0 = tümü), CreatedAt, UpdatedAt |
| `QueryStream` | Id, QueryId (→ cascade), StreamId, StreamName |
| `QueryRun` | Id, QueryId (→ cascade), RunAt, DurationMs, ResultCount, IsSuccess, ErrorMessage, ParametersJson (sorgu başına son 100) |
| `Setting` | Key, Value (tema, açık sekmeler) |

Tarihler ISO-8601 UTC metin olarak saklanır. Klasör silinince alt klasörler silinir, içindeki
sorgular köke taşınır.

## 5. Ekranlar

| Ekran | İçerik |
|---|---|
| Ana pencere | Sol: arama, Yeni Sorgu / Klasör, sorgu ağacı (sağ tık menüsü), ayarlar ve tema. Sağ: sorgu sekmeleri |
| Sorgu sekmesi | Başlık (profil rozeti, "değiştirildi" + Kaydet/Geri al, geçmiş, düzenle, kopyala, sil) → editör → parametreler → çalıştırma çubuğu (sunucular, zaman, limit, Toplu, Canlı, Çalıştır) → mesajlar → Tablo/Analiz |
| Sorgu formu | Ad, klasör, açıklama, profil, editör + parametre özeti, stream'ler (arama, yenile, hata), zaman aralığı, alan çipleri, sıralama, limit |
| Ayarlar | Graylog Profilleri (liste + form + test) · Veri ve Görünüm (tema, yedek, GrayScope 1 içe aktarma) |

## 6. Hata Senaryoları

| Senaryo | Davranış |
|---|---|
| Token geçersiz (401/403) | "Kimlik doğrulama başarısız … token'ı güncelleyin" (token asla gösterilmez) |
| Zaman aşımı | "Graylog sunucusu N saniye içinde yanıt vermedi" |
| Sunucuya ulaşılamıyor | "Graylog sunucusuna bağlanılamadı: …" |
| Lucene sözdizimi hatası (400) | "Sorgu hatası: <Graylog mesajı>" |
| Zorunlu parametre boş | Satır içi hata, istek gönderilmez |
| Toplu çalıştırmada kısmi hata | Başarılı sonuçlar gösterilir; hatalı hedefler listelenir ve uyarı bildirimi çıkar |
| Stream listesi alınamadı | Form kullanılabilir kalır, kayıtlı stream'ler korunur, "Yeniden dene" |
| Profil silme, bağlı sorgu var | Engellenir: "Bu profile bağlı N sorgu var" |
| Token bu bilgisayarda çözülemiyor (geri yükleme) | Profil "token yok" rozetiyle oluşturulur, uyarı gösterilir |
| Boş sonuç | "Bu sorgu için kayıt bulunamadı" boş durumu (hata değil) |

## 7. Kabul Kriterleri

- **FR-Run-01:** Zorunlu parametre boşsa Çalıştır satır içi hata verir; Graylog'a istek gitmez.
- **FR-Run-02:** Aynı sekmede yeni çalıştırma önceki sonuçları ve filtreleri tamamen temizler.
- **FR-Run-03:** Çalışan sorgu "Durdur" ile iptal edilebilir; diğer sekmeler etkilenmez.
- **FR-Batch-01:** Toplu modda `34, 35` ve iki sunucu seçimi 4 arama üretir. Sonuçlarda `Sunucu` ve
  `{Parametre}` kolonları bulunur ve satırlar zamana göre birleşir.
- **FR-Batch-02:** Toplu mod kapalıyken virgüllü değer tek bir değer olarak gönderilir.
- **FR-Live-01:** Canlı takip yalnızca yeni (`_id`) kayıtları ekler ve vurgular; mutlak zaman aralığıyla açılmaz.
- **FR-Sort-01:** Kolon sıralaması Graylog'a tekrar sorulmadan ham değere göre (sayısal/ISO) yapılır.
- **FR-Stream-01:** Stream listesi alınamazsa form kaydedilebilir ve mevcut stream seçimi korunur.
- **FR-Sec-01:** Token hiçbir log, hata mesajı, yedek (isteğe bağlı hariç) veya ekranda düz metin görünmez.
- **FR-Migr-01:** GrayScope 1 veritabanı içe aktarılınca profiller (anahtar dosyası varsa token'larıyla),
  klasör ağacı, sorgular ve stream'ler aktarılır. Aynı isimli kayıtların üzerine yazılmaz.

## 8. Varsayımlar ve Riskler

- **TLS:** Sertifika doğrulaması kapatılamaz. Kendinden imzalı sertifikalı sunucular için
  sertifikanın Windows güven deposuna eklenmesi gerekir (bkz. Backlog).
- **Mesaj ayrıntısı (5.x):** CSV yanıtında index `-` döner. Index universal arama ile bulunur;
  bu da başarısız olursa yalnızca sorgu alanları gösterilir ve kullanıcıya not düşülür.
- **Analiz istemci tarafındadır:** Yalnızca çekilen kayıtları kapsar (limit ile sınırlı). Bu, ekranda açıkça belirtilir.
