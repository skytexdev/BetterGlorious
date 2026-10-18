# BetterGlorious

An open-source replacement for Glorious CORE: battery in the Windows tray, and the settings of your Glorious mouse in one small app.

- **Battery in the tray.** One icon per device with the percentage, colored by level (blue = charging, green, yellow, red). Low battery notifications.
- **Mouse settings.** DPI stages and colors, polling rate, debounce, lift-off distance, sleep timer, lighting (previewed live on the picture of your mouse), profiles 1–3.
- **Keyboard settings** (GMMK 3 / 3 PRO HE, experimental). Polling rate up to 8000 Hz, debounce, sleep timer, lighting effects, profiles.
- **Button map.** Click a button on the picture of your mouse and choose what it does: mouse buttons, keys and shortcuts, media keys, DPI, sniper, profile switch, launch a program, or a macro.
- **Macros.** Built step by step (keys, clicks, delays) with modes *once*, *while held*, *toggle* and *repeat N times*, minimum hold time, end delay and random timing. A rapid-click template is included.
- **Automatic profiles.** Pick a game or app from the running windows; the mouse switches profile when it comes to the front.
- **Undo.** Every change can be undone, and *Restore* puts the mouse back to how it was when the app opened.
- One exe, no installer, no background service, no admin rights.
- 12 languages: English, Deutsch, Español, Français, Italiano, Polski, Português (Brasil), Русский, Türkçe, 日本語, 한국어, 简体中文.

> Not affiliated with or endorsed by Glorious LLC. "Glorious", "GMMK" and model names are trademarks of their owners and are used here only to say which hardware this works with.

## Supported devices

| Family | Devices | Status |
|---|---|---|
| Sinowealth (VID 258A) | Model O / D / O- / D- Wireless, Model O / D Pro Wireless, Series One Pro, Series 2 Pro, Model O2 / D2 Pro (1K and 4K/8K) | **Confirmed** on Model O Wireless |
| PixArt (VID 093A) | Model O2 / D2 / I2 / O2 Mini Wireless | Experimental |
| Model O3 / D3 Wireless (VID 3794) | | Experimental |
| GMMK 3 PRO HE Wireless (65 / 75 / 100) | | Experimental |
| GMMK 3 PRO Wireless (65 / 75 / 100) | | Experimental |
| GMMK Numpad | | Experimental |

Wired-only devices are listed in the panel but have no battery to show.

"Experimental" means the protocol comes from studying Glorious CORE but nobody has confirmed it on that hardware yet. If you own one, please [open an issue](../../issues) saying whether it works. Use **About → Copy diagnostics** in the panel and paste the result into the issue.

## Install

1. Download `BetterGlorious-vX.Y.Z-win-x64.zip` from [Releases](../../releases/latest) and extract it, or build it yourself (below).
2. Run `BetterGlorious.exe` (keep the other files next to it). The panel opens and an icon appears next to the clock.
3. Windows 11 hides new tray icons at first. Drag the icon out of the `^` overflow menu, or turn it on under *Settings → Personalization → Taskbar → Other system tray icons*.
4. Turn on **Settings → Start with Windows** if you want it to start automatically.

Settings and logs are stored in `%APPDATA%\BetterGlorious`.

## Build

You need Windows 10/11 and [Node.js](https://nodejs.org) 22 or newer. The app itself is built with the C# compiler that ships with Windows (.NET Framework 4.x), so there is no .NET SDK to install.

```bat
cd web
npm ci
npm run build
cd ..
build.bat
```

The result is in `bin\`: `BetterGlorious.exe`, the WebView2 DLLs and the `ui` folder. The UI is React + TypeScript + Tailwind, rendered by the WebView2 runtime that ships with Windows. For UI work, run `npm run dev` in `web\` to open it in a browser with mock devices.

On a PC with Smart App Control turned on, your own unsigned builds won't start. Use the signed release instead, or the CI build once signing is set up (see [Code signing policy](#code-signing-policy)).

## How it works

Glorious wireless devices answer HID feature-report requests on a vendor-defined collection. Some also push battery updates as input reports. BetterGlorious sends the same requests Glorious CORE sends, and listens for the same reports. Each device family has its own protocol class in [src/Devices/Protocols.cs](src/Devices/Protocols.cs). The byte layouts are documented in [docs/PROTOCOLS.md](docs/PROTOCOLS.md).

The device table [src/Devices/Catalog.Generated.cs](src/Devices/Catalog.Generated.cs) is generated from the device definitions in Glorious CORE 2.1 with [tools/gencatalog.py](tools/gencatalog.py). The product renders in `web/public/devices` are the ones Glorious CORE 2.1 ships, in every colorway, with only their empty margins trimmed by [tools/genimages.py](tools/genimages.py). They belong to Glorious; if Glorious asks, they will be removed.

## Roadmap

- [x] Battery for all wireless families, multiple devices at once
- [x] Model O/D family: DPI stages and colors, polling rate, debounce, lift-off distance, sleep timer, profiles 1–3
- [x] Model O/D family: lighting effects, color, speed, brightness
- [x] Model O/D family: button remapping (mouse, DPI, sniper, keys, media, macros, launch a program)
- [x] Software macros with hold/toggle/repeat modes, rapid click, automatic profile per app
- [ ] Settings for the PixArt (O2/D2/I2) and O3/D3 families. Protocols are documented, but nobody has tested them on hardware yet.
- [x] GMMK 3 / 3 PRO HE: polling rate, debounce, sleep, lighting, profiles (experimental)
- [ ] GMMK 3 HE actuation point and rapid trigger

Firmware updates, pairing and factory reset are intentionally out of scope. Use Glorious CORE for those.

## Code signing policy

Release builds are signed so that Windows SmartScreen and Smart App Control let them run.

- Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).
- Only binaries that this repository's GitHub Actions workflow builds from tagged source are signed. Nothing is signed by hand.
- Committers and reviewers: the repository owner. Approvers: the repository owner. Each signing request is approved manually.
- Privacy: BetterGlorious sends no data anywhere. Its only network request is reading GitHub's public releases page to tell you when a new version is out. It talks to your USB devices, and all settings stay in `%APPDATA%\BetterGlorious` on your computer.

See [docs/RELEASING.md](docs/RELEASING.md) for how releases are made.

## Translations

Panel text lives in [web/src/locales](web/src/locales) and tray text in [src/Locales](src/Locales), one JSON file per language. English (`en.json`) is the source, and any missing key falls back to it. To fix a translation, edit the value and open a pull request. To add a language, copy `en.json` in both folders, translate it, then add the code to `LANGUAGES` in [web/src/i18n.ts](web/src/i18n.ts) and `Codes` in [src/Core/Strings.cs](src/Core/Strings.cs).

## License

MIT, see [LICENSE](LICENSE).
