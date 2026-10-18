# Battery protocols

Byte indexes are given the way Windows returns reports (`HidD_GetFeature`, `ReadFile`): **index 0 is the report ID**, even when the report ID is 0. "Command collection" and "listen collection" are the HID top-level collections (usage page / usage) used to send requests and to receive unsolicited input reports.

Status: ✅ confirmed on hardware, 🧪 taken from Glorious CORE's behavior but not yet confirmed.

## Sinowealth mice (VID 258A) ✅

Model O / D / O- / D- Wireless, O / D Pro, Series One / Two Pro, O2 / D2 Pro (1K and 4K/8K). Confirmed on a Model O Wireless (2011 cable, 2022 receiver).

- Command collection: usage page `FFFF`, usage `0000`, 65-byte feature report (ID 0 + 64 bytes). The same request works over the cable and through the receiver.
- Request (set feature): `[3]=02` (target: mouse), `[4]=02` (data length), `[6]=83` (battery). Everything else 0.
- Wait about 100 ms, then get feature report 0.

| Index | Meaning |
|---|---|
| 1 | Status: `A1` ok, `A4` receiver can't reach the mouse (asleep or off), `A0` busy, `A2` failed, `A3` unsupported |
| 6 | Echo of `83` |
| 7 | Charging: `0` no, `1` charging, `2`/`3` full |
| 8 | Percent (0–100) |

Example from a Model O Wireless on its cable: `00 A1 00 02 02 00 83 01 54` means 84 %, charging.

## PixArt mice (VID 093A) 🧪

Model O2 / D2 / I2 / O2 Mini Wireless. The receiver PIDs end in `D` (822D, 824D, 821D, 826D), the cable PIDs in `A`, and Bluetooth in `B`.

- Command collection: `FF00` / `0001`. Listen collection: `FF00` / `FF00`.

| Link | Report | Request |
|---|---|---|
| Cable | feature ID 3, 64 bytes | `03 08 FA 14` |
| Receiver | feature ID 3, 64 bytes | `03 08 FB 14` |
| Bluetooth | feature ID 5, 21 bytes | `05 08 14` |

- Reply (get feature, same ID): `[2]=14`, `[3]` percent (`FF` = not ready, retry), `[4]` charging.
- The receiver also pushes input report `06 FB <percent> <charging>`. For the I2 this is reportedly the only thing that works, because feature reads time out.
- Glorious CORE treats "cable PID present" as charging.

## Model O3 / D3 Wireless (VID 3794) 🧪

- Command collection: `FF00` / `0001`, feature ID 3, 64 bytes.
- Request: `03 BB 82 00` (`BB` = mouse, `82` = battery info).
- Reply over the cable: `[7]` charging (1), `[8]` percent. `[3]`/`[4]` describe the backup battery (state, percent).
- Through the receiver (`A300`), the reply is wrapped and shifted by two bytes: `[9]` charging, `[10]` percent.

## GMMK 3 PRO HE Wireless (VID 342D / 3794) 🧪

- Command collection: `FFFF` / `0002`, feature ID 0, 65 bytes. Listen collection: `FFFF` / `0001`.
- Cable and the secondary receiver: `[1]=83`, `[8]=7C` (checksum `FF - 83`). Reply: `[1]=83`, `[2]` percent, `[3]` state (`0` discharging, `1` charging, `2` full).
- Primary receiver (VID 342D): `[1]=F7`. Reply: `[2]` percent.
- Bluetooth (`FF55` / `0202`) uses output report 6 with a `55` prefix. Battery arrives as an input report `06 xx 83 <percent> <state>`.

Settings (from CORE 2's `GMMK3HEDeviceHandler`; see [src/Devices/Gmmk3HeConfig.cs](../src/Devices/Gmmk3HeConfig.cs)). Byte indices are in the 65-byte buffer, where `[0]` is the report ID. The checksum at `[8]` is `FF - (sum([1..7]) & FF)`. CORE waits 550 ms after every write; there is no ack. Over Bluetooth the same command goes in output report `06 55 <cmd...>`.

| Setting | Bytes |
|---|---|
| Report rate | `[1]=04`, `[2]` profile (0–2), `[3]` rate: 8000=0, 4000=1, 2000=2, 1000=3, 500=4, 250=5, 125=6 |
| Profile | `[1]=05`, `[2]` profile, `[3]` layer. Lighting is not restored, so re-send it |
| Lighting | `[1]=07` main / `08` side, `[2]` effect, `[3]` speed `20 - ceil(pct/5)`, `[4]` brightness `ceil(pct/5)`, `[5]` `8` effect colors / `9` custom color, `[6..8]` RGB, `[9]` checksum of `[1..8]` |
| Debounce | `[1]=11`, `[3]` = round(ms / 5). CORE offers 0, 2, 8, 16 ms |
| Sleep | `[1]=12`, `[8]=ED`, then u16 LE seconds per link: Bluetooth `[9]` (deep `[13]`), 2.4 GHz `[11]` (`[15]`), cable `[17]` (`[19]`). 0 = never, deep sleep `FFFF` |

Effect values: 0 off, 1 Glorious, 2 Wave 1, 3 Wave 2, 4 Spiral, 5 Acid, 6 Breathing, 7 Static, 8 Ripple, 9 Pass without trace, 10 Fast run, 11 Matrix 2, 12 Matrix 3, 13 Rainbow, 14 Heartbeat, 15 Digital times, 16 Kamehameha, 17 Ping pong, 18 Surmount.

## GMMK 3 PRO Wireless, non-HE (VID 342D) 🧪

- Cable: feature ID 7, request `07 14`, reply `[2]` percent (`FF` = on USB, level unknown).
- Receiver and Bluetooth: requests go through a chunked relay. BetterGlorious doesn't implement this yet; it listens for the event the board pushes: `?? 80 <percent>`, or `06 66 80 <percent>` over Bluetooth.
- The board never reports charging. Cable present = charging.

## GMMK Numpad 🧪

- Cable (`320F:5088`): feature ID 7, request `07 8A`, reply `[2]` percent, `[3]` charging, `[4..5]` firmware version.
- Bluetooth (`093A:2860`): the answer arrives as input report `04 8A <percent> <charging>`.

# Settings: Sinowealth mice ✅

Confirmed on a Model O Wireless (firmware 0.3.8.1). The frame is the same as for battery: feature report 0, 65 bytes, `[3]=02` (target mouse), `[4]` payload length, `[5]` class, `[6]` command, payload from `[7]`. The reply comes back in the same layout. `[1]` is the status (`A1` ok, `A0` busy, `A2` failed or empty, `A3` unsupported, `A4` mouse unreachable).

**Getters are the setter's command number with bit 7 set** (set DPI `01/01`, get DPI `01/81`). Glorious CORE never reads settings back, but the firmware answers every getter below.

| Setting | Class/set | Get | Payload (set) | Reply (get), from `[7]` |
|---|---|---|---|---|
| Active profile | `00/05` | `00/85` | `[profile 1–3]` | `[profile]` |
| Firmware version | | `00/81` | | `[a, b, c, d]` |
| Sleep timer | `00/07` | `00/87` | `[secHi, secLo]`, `FF FF` = never | same |
| Debounce | `00/08` | `00/88` + `[profile]` | `[profile, ms]` | `[profile, ms]` |
| Polling rate | `01/00` | `01/80` | `[code]`: 125 `08`, 250 `04`, 500 `02`, 1000 `01` | `[code]` |
| DPI stages | `01/01` | `01/81` + `[profile]` | `[profile, n, (Xhi Xlo Yhi Ylo)×n]`, raw DPI, max 6 | same |
| Active DPI stage | `01/02` | `01/82` + `[profile]` | `[profile, stage 1-based]` | same |
| Lift-off distance | `01/08` | `01/88` | `[0 low / 1 high]` | same |
| LED effect | `02/00` | `02/80` + `[profile]` | `[profile, FF, effect, 0, speed, R, G, B]` | `[profile, 0, effect, 0, speed, R, G, B]` |
| DPI colors | `02/01` | `02/81` + `[profile]` | `[profile, (R G B)×6]` | same |
| LED brightness | `02/02` | (always reads 0) | `[1 cable / 0 wireless, 0–255]` | |
| Button | `03/00` | `03/80` + `[profile, button]` | `[profile, button, 0, action×7]` | same |
| Macro header | `04/01` | `04/81` + `[idHi, idLo]` | `[idHi, idLo, 0, 0, lenHi, lenLo]` | same; `A2` = empty slot |
| Macro delete | `04/02` | | `[idHi, idLo]` | |
| Macro data | `04/03`, length `3A` | `04/83` | `[idHi, idLo, 0, 0, offHi, offLo, 51, data×51]` | data from `[14]` |

Effects: 0 off, 1 Glorious, 2 seamless breathing, 3 breathing, 4 static, 5 breathing single color, 6 tail, 7 rave, 8 wave. Speed is a delay, 1 is fastest. Motion sync (`01/09`) answers `A3` on this model.

**Button ids:** left `01`, right `02`, middle `03`, back `04`, forward `05`, wheel up `10`, wheel down `11`, DPI `14`.

**Actions (7 bytes, zero-padded):**

| Action | Bytes |
|---|---|
| Mouse button | `01 01 code` (L 1, R 2, M 3, back 4, forward 5, wheel up 10, wheel down 11) |
| DPI | `07 01 x` (up 1, down 2, cycle 6, cycle reverse 7) |
| Sniper DPI while held | `07 05 05 Xhi Xlo Yhi Ylo` |
| Key | `04 02 mods usage` (mods: Ctrl 1, Shift 2, Alt 4, Win 8) |
| Media | `05 02 usageHi usageLo` (consumer page, e.g. `00 CD` play/pause) |
| Launch program | `05 02 button 0F`. The mouse then sends input report `01 0F button` on collection `000C/0001`, and the host starts the program. |
| Macro | once `10 03 idHi idLo 01`, while held `11 02 idHi idLo 00`, toggle `12 02 idHi idLo 00` |
| Profile next / previous | `08 01 04` / `08 01 03` |
| Battery indicator | `0C 01 01` |
| Disabled | `00 00` |

**Macro event stream:** key `02 usage` down and `03 usage` up; modifier `09 bit` down and `0A bit` up (LCtrl 1, LShift 2, LAlt 4, LWin 8, right side ×16); mouse `01 mask` (buttons held after the event: L 1, R 2, M 4, back 8, forward 16). Between events the delay goes as `20 ms` (≤ 255) or `21 hi lo`. Example: `01 01 20 3C 01 00` = left down, wait 60 ms, release.
