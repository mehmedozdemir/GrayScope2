# DESIGN_SYSTEM.md — GrayScope 2

Avalonia 12 + Fluent tema üzerine proje token'ları. Görünümler ham renk veya boyut kullanmaz;
yalnızca `Themes/Tokens.axaml` anahtarlarını `DynamicResource` ile kullanır. Stil sınıfları
`Themes/Controls.axaml` içindedir.

**İlke:** Bu bir veri aracıdır, dokunmatik arayüz değildir. Yoğun ama ferah bir düzen, sakin
yüzeyler ve tek bir vurgu rengi hedeflenir. Hiyerarşi önce boyut ve ağırlıkla, renk ancak durum
için kullanılır.

## 1. Tipografi

| Token | Boyut | Kullanım |
|---|---|---|
| `FontSizeXl` | 15 | Ekran/sorgu başlığı (`TextBlock.title`, SemiBold) |
| `FontSizeLg` | 14 | Kart başlığı (`TextBlock.heading`) |
| `FontSizeMd` | 12,5 | Gövde, kontroller (Fluent `ControlContentThemeFontSize` buna ezildi) |
| `FontSizeSm` | 11,5 | İkincil metin, form etiketleri (`muted`, `label`) |
| `FontSizeXs` | 10,5 | Bölüm başlıkları (`section`: BÜYÜK HARF, harf aralığı 0,6), rozetler |
| `FontSizeMono` | 12 | Editör dışı monospace değerler (parametre, alan değeri) |

- Yazı tipleri: Inter (UI) ve `MonoFont` = Cascadia Mono → Consolas.
- Lucene editörü 12,5; tablo hücreleri 12 (yoğun ve taranabilir).
- Fluent'in büyük varsayılanları ezildi: sekme başlığı 24 → 12,5, tooltip 11,5.

## 2. Renk

| Token | Koyu | Açık | Kullanım |
|---|---|---|---|
| `BgBase` | `#0D0F12` | `#F5F6F8` | Pencere zemini |
| `BgSurface` | `#13161B` | `#FFFFFF` | Kart, kenar çubuğu, tablo, başlık çubuğu |
| `BgElevated` | `#1A1E24` | `#F0F2F5` | Buton, segment zemini, rozet |
| `BgHover` / `BgPressed` | `#20252C` / `#262C34` | `#EAEDF1` / `#E2E6EC` | Etkileşim |
| `Border` / `BorderSubtle` / `BorderStrong` | `#252A31` / `#1C2026` / `#343A43` | `#E0E3E8` / `#ECEEF2` / `#CDD2DA` | Kenarlık, tablo çizgisi, hover kenarı |
| `TextPrimary` / `TextSecondary` | `#E4E7EC` / `#8D95A3` | `#1B1E24` / `#5F6775` | Metin |
| `Accent` / `AccentHover` / `AccentMuted` | `#6B8EF5` / `#7F9DF7` / `#1B2438` | `#3563D9` / `#2B57C7` / `#EAF0FD` | Birincil aksiyon, seçili toggle |
| `Selection` | `#1E2A44` | `#E3EAFB` | Tablo, liste ve ağaç seçimi |
| `Success` / `Warning` / `Danger` | `#4CB66A` / `#D4A23C` / `#EF6461` | koyu tonlar | Durum (nokta, ikon, ince kenar) |
| `Hit` / `NewRow` | `#4A3D14` / `#14281E` | `#FFECA8` / `#E5F5EA` | Arama vurgusu, canlı takip yeni satırı |

- Fluent buton, toggle ve DataGrid fırçaları bu token'lara bağlandı. Gri dolgulu Fluent
  butonları yerine ince kenarlı `BgElevated` kullanılır; seçili toggle `AccentMuted` zemin ve accent metinle gösterilir.
- Tablo başlığı `BgSurface` ve ikincil metinle çizilir (siyah şerit yok).
- Durum renkleri geniş alan boyamaz; nokta, ikon veya 1px kenar olarak kullanılır. Tek istisna hata bandıdır (`DangerMuted`).

## 3. Yoğunluk ve şekil

| Öğe | Değer |
|---|---|
| Giriş, buton, combo yüksekliği | 28 |
| İkon butonu | 28×28 (`sm`: 22×22, ikon 11) |
| Ağaç öğesi / liste öğesi | 26 / padding 10,6 |
| Tablo satırı / başlığı | 28 / 30 |
| Köşe yarıçapı | kontroller 4, editör 6, kart 8 |
| Boşluk | 4'ün katları: 4 · 8 · 12 · 16 · 20 · 24 |

## 4. Stil sınıfları

| Sınıf | Açıklama |
|---|---|
| `Button.primary` | Accent dolgulu. **Bir ekranda en fazla bir tane.** |
| `Button.ghost` / `DropDownButton.ghost` | Kenarsız; hover'da zemin. Araç çubukları |
| `Button.icon` (+ `sm`) | Şeffaf ikon butonu; ikon ikincil renkte, hover'da birincil |
| `Button.link` | Metin bağlantısı (Kaydet, Geri al, Filtreleri temizle) |
| `Button.danger` | Yıkıcı aksiyon; her zaman onay penceresi açar |
| `Button.row` | Liste satırı gibi davranan buton (geçmiş, değer dağılımı) |
| `Border.surface` | Kart yüzeyi |
| `Border.editor` | Editör/girdi çerçevesi; odakta accent kenar (`:focus-within`) |
| `Border.segmented` + `RadioButton` | Segmentli kontrol (Tablo/Analiz, zaman aralığı tipi, sıralama yönü) |
| `Border.divider` | Araç çubuğunda 1×18 dikey ayraç |
| `Border.badge` (+ `accent`, `danger`) | Hap rozet |
| `.hover-host` › `StackPanel.hover-actions` | Aksiyonlar yalnızca satırın üzerine gelince görünür |
| `TextBlock.title` / `heading` / `section` / `label` / `muted` / `error` / `kbd` | Metin rolleri |
| `TabControl.documents` | Sorgu sekmeleri; kapat düğmesi hover veya seçiliyken görünür |
| `NumericUpDown.compact` | Spinner'sız, sağa hizalı sayı girişi (ok tuşları çalışır) |

## 5. Pencere çerçevesi

- Tüm pencereler `ExtendClientAreaToDecorationsHint` ile çizilir. Uygulama adı ve pencere
  düğmeleri Avalonia'nın tema uyumlu dekorasyonlarıyla gelir; Windows'un vurgu renkli başlık
  çubuğu kullanılmaz.
- `Controls/WindowTitleBar` 34px yüzey ve sürükleme alanı sağlar. Ana pencerede ortada aktif
  sorgunun adı gösterilir.
- Tam ekran düğmesi gizlidir. Boyutlandırılamayan diyaloglarda küçült/büyüt düğmeleri kapalıdır.

## 6. Ana pencere

```
┌─ GrayScope 2 ─────────────── Şehir bazlı 200 dışı yanıtlar ──────────── – □ × ┐
│ SORGULAR    + 📁 │ [Sekme 1 ●] [Sekme 2]                                         │
│ [Ara…         ]  │ Başlık  [⛁ profil]  ● Kaydedilmemiş · Kaydet · Geri al  ⟲✎⧉🗑 │
│ ▾ Operasyon      │ ┌ Lucene editörü ───────────────────────────────────────────┐ │
│   ▾ HTTP         │ └───────────────────────────────────────────────────────────┘ │
│     Sorgu A      │ NetworkId [34     ]                                            │
│   Sorgu B        │ ⛁ Sunucu ▾  ◷ Son 1 saat ▾ │ Limit [150] │ [Toplu][Canlı][10sn] ▶│
│                  │ ┌[Tablo|Analiz] 149 kayıt  çipler…  [🔍 Ara][⛛][▯]│⤓ Dışa aktar┐│
│                  │ │ grid ...                              │ MESAJ AYRINTISI    ││
│ ⚙ ◐              │ └───────────────────────────────────────┴────────────────────┘│
└──────────────────┴────────────────────────────────────────────────────────────────┘
  264px (Ctrl+B)                     esnek
```

- Ağaçta bir sorgu seçmek onu açar; zaten açıksa sekmesine geçer. Profil adı yalnızca varsayılan
  olmayan profildeki sorgularda küçük ikincil metin olarak gösterilir.
- Boş durumlar sakin bir ikon, kısa başlık ve tek satır yönlendirmeyle tasarlanmıştır (karşılama,
  sekme yok, henüz çalıştırılmadı, sonuç yok, satır seçilmedi).
- Yükleme durumu: sonuç alanının üstünde 2px belirsiz ilerleme çubuğu; sekmede mavi nokta
  (çalışıyor) ve yeşil nokta (canlı); kaydedilmemiş değişiklik için turuncu nokta.
- Hatalar: doğrulama satır içinde ikonla; Graylog hatası ince kırmızı kenarlı bantta,
  seçilebilir metinle; toplu çalıştırmada hedef bazlı uyarı listesi.

## 7. Sonuç tablosu

- Kolonlar dinamiktir; varsayılan genişlikler `timestamp` 180, `message` 520, diğerleri 170.
- `timestamp` yerel saatle `dd.MM.yyyy HH:mm:ss.fff` gösterilir; sıralama ham ISO değerle yapılır.
- Toplu çalıştırmada `Sunucu` ve `{Parametre}` bağlam kolonları başta, Medium ağırlıkta yer alır.
- Arama varsayılan olarak **vurgular**; ⛛ açıkken **filtreler**. Joker karakterler `*` ve `?`.
- Sağ tık: hücreyi kopyala, satırı JSON kopyala, büyük görüntüleyici, tabloyu filtrele, sorguya
  ekle, hariç tut. Çift tık: değer görüntüleyici. Ctrl+C: seçili hücre.
- Detay panelinde alan adı ikincil renkte, değer monospace; aksiyonlar (+ − kopyala genişlet) yalnızca hover'da görünür.

## 8. Bildirimler

Sağ altta en fazla 4 toast görünür. Renkli nokta ve mesajdan oluşur. Başarı/bilgi 4 sn, hata
7 sn sonra kaybolur ve kapatılabilir. Renk tek ayırt edici değildir; her toast kendi mesaj metnini taşır.

## 9. Klavye

| Kısayol | Aksiyon |
|---|---|
| Ctrl+Enter / F5 | Aktif sekmeyi çalıştır |
| Enter (parametre kutusunda) | Çalıştır |
| Ctrl+Space (editörde) | Alan/operatör önerileri |
| Ctrl+F | Sonuçlarda ara (Tablo görünümüne geçer) |
| Ctrl+N | Yeni sorgu |
| Ctrl+W | Sekmeyi kapat |
| Ctrl+B | Kenar çubuğunu gizle/göster |
| Ctrl+, | Ayarlar |
| Ctrl+S (formda) | Kaydet |
| Esc | Değer görüntüleyiciyi / ayarları kapat |
