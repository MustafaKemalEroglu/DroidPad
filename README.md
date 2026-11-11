# DroidPad

Waydroid için klavye–fare tuş eşleme ve makro aracı. Waydroid'in üzerine yerleşen şeffaf bir
katman, klavye tuşlarını ve fare dokunuşlarını `adb` üzerinden Android'e dokunma olarak iletir.
Arayüz MuMu Player'ın tuş eşleme ve makro panellerinden esinlenmiştir.

## Gereksinimler
- Linux + [Waydroid](https://waydro.id) (oturum çalışıyor olmalı: `waydroid session start`)
- `adb` (android-tools)
- .NET 10 SDK (derlemek için)

## Kurulum
```bash
./scripts/install.sh      # yayınlar, ~/.local/bin/droidpad ve uygulama menüsü girdisi ekler
# veya geliştirme için:
dotnet run --project DroidPad.csproj
```

## Kullanım
Waydroid'i **tam ekran** açın (`waydroid show-full-ui`), ardından DroidPad'i başlatın.
Sağdaki yan çubuk ve kısayollar:

| Kısayol | İşlev |
|---|---|
| **F12** | Tuş eşleme (düzenleme) modu |
| **F11** | Makro paneli |
| **F10** | Günlük ve bağlantı durumu |
| **F9**  | Makroyu başlat / durdur |

**Tuş eşleme:** bir araç seçip ekrana tıklayın veya boş alana sağ tıklayın; tuşu atamak için
istenen tuşa basın. Öğeleri sürükleyerek taşıyın, `Del` ile silin.
**Makro:** komutları elle ekleyin veya *Kaydet* ile ekrandaki tıklama/kaydırmaları kaydedin;
döngü, rastgele bekleme ve süre sınırı desteklenir.

## Profiller
Ayarlar `~/.config/DroidPad/` altında tutulur. *Bu uygulama için profil olarak kaydet* ile
ön plandaki Android uygulamasına özel bir profil oluşturulur; "Uygulamaya göre otomatik geç"
açıkken o uygulama öne geldiğinde profil otomatik yüklenir.

## Bilinen sınırlar
- **Tek dokunuş:** `input motionevent` tek işaretçi destekler; aynı anda yalnızca bir joystick
  tutulabilir ve tuş dokunuşları joystick'i kısa süre keser/geri yükler.
- **Tam ekran:** katman tüm ekranı Android ekranına eşler; Waydroid pencere modunda hizalanmaz.
- **Fare ile bakış:** Wayland'de imleç kilitleme ve çoklu dokunuş olmadan uygulanamıyor.
