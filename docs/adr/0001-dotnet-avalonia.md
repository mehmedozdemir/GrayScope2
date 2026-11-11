# ADR 0001 — GrayScope 2 için .NET 10 + Avalonia 12

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-11

## Bağlam

GrayScope 1 Python 3.11 + PySide6 ile yazıldı (~4.800 satır). Yeni sürümde şunlar hedefleniyor:
gelişmiş sorgu editörü, çoklu sekme, toplu ve paralel çalıştırma, canlı takip ve sonuç analizi.
Ekibin diğer araçlarının çoğu .NET ile yazılmış (GraylogMcp, QrMaster, MqttExplorer).

Değerlendirilen seçenekler:

| Seçenek | Artı | Eksi |
|---|---|---|
| **.NET 10 + Avalonia 12** | Ekip bilgisi, MVVM, güçlü DataGrid, AvaloniaEdit, tek dosya EXE, gerekirse Linux/macOS | Avalonia 12 görece yeni (API değişiklikleri) |
| .NET + WPF (WPF-UI) | En olgun Windows ekosistemi | Yalnızca Windows |
| Tauri 2 + React/TS | Küçük kurulum, esnek UI | Rust + TS bilgisi gerekir, iki dil |
| Electron + React/TS | Çok sayıda bileşen | Ağır (~150 MB), yüksek bellek |

## Karar

**.NET 10 (LTS) + Avalonia 12 + CommunityToolkit.Mvvm.** Proje üç katmana ayrılır:
Core (saf mantık), Infrastructure (SQLite, Graylog, şifreleme) ve App (Avalonia).

Kullanıcı başlangıçta ".NET 9" seçti. Makinede .NET 9 SDK yok, .NET 10 ise güncel LTS
(daha uzun destek). Bu yüzden hedef .NET 10 oldu.

## Sonuçlar

- Token koruması Fernet anahtar dosyası yerine **Windows DPAPI**'ye geçti. Anahtar dosyası
  sızdırma veya kaybetme riski ortadan kalktı. GrayScope 1 token'ları Fernet çözücü ile bir kez taşınıyor.
- İş mantığı (parametre motoru, toplu çalıştırma, analiz) UI'dan bağımsız ve xUnit ile test ediliyor.
- Avalonia DataGrid'de dinamik kolonlar kodla kuruluyor (`QueryTabView.axaml.cs`).
  TreeDataGrid ticari lisanslı olduğu için kullanılmadı.
