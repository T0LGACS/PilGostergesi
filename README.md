# Pil Göstergesi

Kablosuz cihazlarının pil seviyesini Windows görev çubuğunda, saatin hemen solunda gösterir.

- Her cihaz için cihaz simgesi, renkli pil ve yüzde
- Şarj olurken şimşek, %10'un altında kırmızı yanıp sönme
- Tam ekran oyunlarda kendini gizler
- Siyah ayar penceresi: renkler, eşikler, yenileme süresi, Windows açılışında başlatma
- Güncellemeleri bu repodaki son sürümden kendisi indirir

## Desteklenen cihazlar

| Cihaz | Bağlantı |
|---|---|
| Razer fare / klavye / kulaklık | Dongle, kablo |
| Logitech PRO X 2 LIGHTSPEED kulaklık | Dongle |
| Logitech kablosuz fare / klavye (HID++) | Lightspeed, Unifying, Bolt, Bluetooth |
| DualSense, DualSense Edge, DualShock 4 | USB, Bluetooth |
| Xbox kolları | Windows'un bildirdiği |
| Pilini Windows'un bildiği Bluetooth cihazları | Bluetooth |

## Kurulum

1. [Releases](https://github.com/T0LGACS/PilGostergesi/releases/latest) sayfasından `PilGostergesi.exe` dosyasını indir.
2. Kalıcı bir klasöre koy (ör. `%LocalAppData%\PilGostergesi`) ve çalıştır.
3. Gerekirse [.NET 10 Masaüstü Çalışma Zamanı](https://dotnet.microsoft.com/download/dotnet/10.0)'nı kur.

Göstergeye sol tıklayınca ayarlar, sağ tıklayınca menü açılır.

## Derleme

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Yeni sürüm: `PilGostergesi.csproj` içindeki `<Version>` değerini artır, derle ve `publish\PilGostergesi.exe` dosyasını `vX.Y.Z` etiketli bir GitHub sürümüne ekle.
