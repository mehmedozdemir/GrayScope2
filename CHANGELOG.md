# Changelog

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir. Format
[Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) temel alınır ve proje
[Semantic Versioning](https://semver.org/lang/tr/) kurallarına uyar.

## [Unreleased]

### Added
- GrayScope 1'in .NET 10 + Avalonia 12 ile yeniden yazımı: üç katmanlı mimari
  (Core / Infrastructure / App), merkezi paket sabitleme, uyarısız derleme ve CI pipeline'ı.
- Graylog istemcisi: 5.x Scripting API (CSV) ile 2.x–4.x universal search arasında otomatik
  geçiş, ayrı meta veri ve arama zaman aşımları, Türkçe hata eşlemesi (401/403, 400, zaman aşımı, bağlantı).
- Lucene editörü: sözdizimi renklendirme, alan adı ve operatör otomatik tamamlama.
- Parametre motoru (`{Ad}`, `{Ad?}`, `{Ad:v}`, `{Ad?:v}`) ve değer doğrulama.
- Çoklu sekme; sekme başına iptal edilebilir çalıştırma; açık sekmelerin oturumlar arası geri yüklenmesi.
- Toplu çalıştırma: virgüllü değerler × çok sunucu, en fazla 4 paralel arama, stream'lerin sunucular
  arasında başlığa göre eşlenmesi, hedef bazlı hata raporu.
- Canlı takip (5/10/30/60 sn), yeni kayıt vurgusu, 5.000 satır tampon.
- Sonuç tablosu: ham değere göre sıralama, vurgulayan veya filtreleyen arama, filtre çipleri,
  sağ tık menüsü, "sorguya ekle / hariç tut".
- Mesaj ayrıntı paneli (5.x'te tembel tam alan yükleme) ve JSON/XML değer görüntüleyici.
- Analiz: zaman histogramı ve alan değer dağılımı (tıklayınca filtreleme).
- Çalıştırma geçmişi (sorgu başına son 100) ve geçmişten parametre uygulama.
- CSV (UTF-8 BOM) ve Excel dışa aktarma.
- Token koruması: Windows DPAPI (diğer OS'lerde AES-GCM).
- JSON yedekleme/geri yükleme (v3; GrayScope 1 v1/v2 okunur) ve GrayScope 1 veritabanından içe
  aktarma (Fernet token çözümü dahil).
- Açık/koyu tema, karşılama ekranı, klavye kısayolları.
- `Release` GitHub Actions workflow'u: `v*.*.*` etiketiyle test, tek dosya win-x64 paketi,
  SHA-256 ve CHANGELOG'dan sürüm notlarıyla GitHub Release.

### Changed
- Arayüz yeniden tasarlandı: temel punto 14 → 12,5, başlıklar 15, bölüm etiketleri büyük harf
  ve harf aralıklı; kontrol yüksekliği 32 → 28; tablo satırı 28, hücre punto 12.
- Düşük kontrastlı grafit palet. Gri dolgulu butonlar yerine ince kenarlı yüzeyler; seçili
  toggle'lar ve seçimler yumuşak accent tonunda; tablo başlığı yüzey renginde.
- Windows vurgu renkli başlık çubuğu yerine tema uyumlu, uygulamaya entegre başlık çubuğu
  (ortada aktif sorgu adı); tam ekran düğmesi ve diyaloglarda gereksiz küçült/büyüt düğmeleri kaldırıldı.
- Tablo/Analiz sekmeleri araç çubuğuyla tek satırda birleşti (segmentli kontrol); zaman aralığı
  tipi ve sıralama yönü de segmentli kontrole geçti.
- Detay panelindeki alan aksiyonları ve sekme kapatma düğmesi yalnızca üzerine gelince görünüyor.
- Sorgu formu bölümlere ayrıldı (Genel, Sorgu, Stream'ler, Alanlar, Zaman aralığı ve sonuç).

### Security
- XML biçimlendirmede DTD işleme yasaklandı (XXE ve entity-genişleme DoS'una karşı).
