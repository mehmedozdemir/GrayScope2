# tools/dev — yerel doğrulama araçları

Uygulamayı gerçek Graylog sunucularına ve gerçek veri klasörüne dokunmadan uçtan uca denemek için.
Bu araçlar ürünün parçası değildir ve dağıtılmaz.

| Dosya | Ne yapar |
|---|---|
| `start-demo.ps1` | İzole veri klasörü (`%TEMP%\gs2-demo`) oluşturur, demo verisini ekler, mock'u ve uygulamayı başlatır |
| `mock_graylog.py` | Graylog 5.x API taklidi (`/system`, `/streams`, `/system/fields`, `POST /search/messages` CSV, `/messages/{index}/{id}`), port 9123 |
| `seed_demo.py` | Veritabanına 2 mock profil, klasörler ve 3 örnek sorgu ekler |
| `uia.ps1` | UI Automation: `-Action set\|invoke\|toggle\|list -Name <erişilebilir ad>` |
| `click.ps1` | Pencereye göre konumlu fare tıklaması (`-X -Y`, DPI-aware) |
| `shot.ps1` | Ana pencerenin ekran görüntüsü (`PrintWindow`, z-sırasından bağımsız) |
| `dlg.ps1` | Başlık önekiyle diyalog bulma: görüntü al (`-Out`) ve/veya buton tetikle (`-Invoke`) |

```powershell
./tools/dev/start-demo.ps1
./tools/dev/uia.ps1 -Action set -Name NetworkId -Value 34
./tools/dev/uia.ps1 -Action invoke -Name "Çalıştır"
./tools/dev/shot.ps1 -Out "$env:TEMP\gs2.png"
```

Notlar:
- `SendKeys` kullanmayın: odak garantisi olmadığından tuşlar başka bir pencereye gidebilir.
- Mock, 5.x'in index arama yolunu (universal API) taklit etmez. Bu yüzden detay panelinde
  "yalnızca sorgu alanları" notu görünmesi beklenen bir durumdur.
