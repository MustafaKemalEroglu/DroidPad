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
| **F8**  | Oyun modunda tuşları göster / gizle |
| **F7**  | Yazma modunu elle aç/kapat. Android ekran klavyesi açılınca mod **otomatik** devreye girer: oyun tuşları askıya alınır, yazılanlar metin kutusuna gider (Enter/Esc ile çıkar) |

**Tuş eşleme:** bir araç seçip ekrana tıklayın veya boş alana sağ tıklayın; tuşu atamak için
istenen tuşa basın. Öğeleri sürükleyerek taşıyın, `Del` ile silin.
**Makro:** komutları elle ekleyin veya *Kaydet* ile ekrandaki tıklama/kaydırmaları kaydedin;
döngü, rastgele bekleme ve süre sınırı desteklenir.

**Tuş türleri** (sağ tık menüsü): *Tap* (tek dokunuş), *Turbo* (basılı tutunca hızlı tekrar,
aralık ayarlanır), *Kaydır* (tuşa basınca başlangıç → bitiş noktası arası kaydırma).
Aynı tuş birden fazla öğeye atanırsa günlükte uyarı çıkar.

**Oyun modu:** sağ tık = Android *Geri*, fare tekerleği = kaydırma, tuş saydamlığı ayarlanabilir
(hepsi profille birlikte saklanır).

**Kenar çubuğu araçları:** Geri / Ana ekran / Son uygulamalar, ses ±, ekran görüntüsü
(`~/Pictures/DroidPad`) ve APK kurulumu.

**Makro komutları:** `click`, `swipe`, `long_press` (`Nokta,süre`), `text` (ASCII metin yazar),
`key`, `wait`, `random_wait`, döngü. Kayıt sırasında 0,6 sn'den uzun basışlar `long_press` olarak kaydedilir.

## Profiller
Ayarlar `~/.config/DroidPad/` altında tutulur. *Bu uygulama için profil olarak kaydet* ile
ön plandaki Android uygulamasına özel bir profil oluşturulur; "Uygulamaya göre otomatik geç"
açıkken o uygulama öne geldiğinde profil otomatik yüklenir.

## Bilinen sınırlar
- **Çok dokunuş:** Bağlanınca küçük bir cihaz yardımcısı (`Helper/`, scrcpy gibi `app_process`)
  Waydroid'e yüklenir; fare, joystick ve tuşlar ayrı parmaklarla aynı anda çalışır ve her olay için
  yeni süreç başlatılmaz. Yardımcı başlatılamazsa `input motionevent` yoluna düşülür: bu durumda tek
  işaretçi vardır, aynı anda yalnızca bir joystick tutulabilir ve tuş dokunuşları joystick'i kısa
  süre keser/geri yükler. Yardımcıyı yeniden derlemek için: `scripts/build-helper.sh` (JDK + internet).
- **Tam ekran:** katman tüm ekranı Android ekranına eşler; Waydroid pencere modunda hizalanmaz.
- **Fare ile bakış:** Wayland'de imleç kilitleme ve çoklu dokunuş olmadan uygulanamıyor.
