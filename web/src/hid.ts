// Button actions and macro event streams for Sinowealth mice (Model O/D family), encoded
// exactly as the firmware stores them. See docs/PROTOCOLS.md.

import { t, tf } from './i18n'

// ---- Keyboard usages ------------------------------------------------------------------------

const codeToUsage: Record<string, number> = {}
'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('').forEach((c, i) => (codeToUsage[`Key${c}`] = 0x04 + i))
'1234567890'.split('').forEach((c, i) => (codeToUsage[`Digit${c}`] = 0x1e + i))
Object.assign(codeToUsage, {
  Enter: 0x28, Escape: 0x29, Backspace: 0x2a, Tab: 0x2b, Space: 0x2c, Minus: 0x2d, Equal: 0x2e,
  BracketLeft: 0x2f, BracketRight: 0x30, Backslash: 0x31, Semicolon: 0x33, Quote: 0x34, Backquote: 0x35,
  Comma: 0x36, Period: 0x37, Slash: 0x38, CapsLock: 0x39, PrintScreen: 0x46, ScrollLock: 0x47, Pause: 0x48,
  Insert: 0x49, Home: 0x4a, PageUp: 0x4b, Delete: 0x4c, End: 0x4d, PageDown: 0x4e, ArrowRight: 0x4f,
  ArrowLeft: 0x50, ArrowDown: 0x51, ArrowUp: 0x52, NumLock: 0x53, NumpadDivide: 0x54, NumpadMultiply: 0x55,
  NumpadSubtract: 0x56, NumpadAdd: 0x57, NumpadEnter: 0x58, NumpadDecimal: 0x63, IntlBackslash: 0x64, ContextMenu: 0x65,
})
for (let i = 1; i <= 12; i++) codeToUsage[`F${i}`] = 0x3a + i - 1
for (let i = 13; i <= 24; i++) codeToUsage[`F${i}`] = 0x68 + i - 13
for (let i = 1; i <= 9; i++) codeToUsage[`Numpad${i}`] = 0x59 + i - 1
codeToUsage.Numpad0 = 0x62

const usageToLabel: Record<number, string> = {}
for (const [code, usage] of Object.entries(codeToUsage)) {
  usageToLabel[usage] = code.replace(/^Key|^Digit/, '').replace('Arrow', '').replace('Numpad', 'Num ')
}
Object.assign(usageToLabel, { 0x2c: 'Space', 0x28: 'Enter', 0x29: 'Esc', 0x2a: 'Backspace', 0x2d: '-', 0x2e: '=', 0x2f: '[', 0x30: ']', 0x31: '\\', 0x33: ';', 0x34: "'", 0x35: '`', 0x36: ',', 0x37: '.', 0x38: '/' })

// Modifier keys: event.code -> bit in the modifier mask (same bits for actions and macros).
const modifierBits: Record<string, number> = {
  ControlLeft: 0x01, ShiftLeft: 0x02, AltLeft: 0x04, MetaLeft: 0x08,
  ControlRight: 0x10, ShiftRight: 0x20, AltRight: 0x40, MetaRight: 0x80,
}
const modifierNames: [number, string][] = [
  [0x11, 'Ctrl'], [0x22, 'Shift'], [0x44, 'Alt'], [0x88, 'Win'],
]

export function usageOf(code: string): number | undefined {
  return codeToUsage[code]
}
export function modifierBit(code: string): number | undefined {
  return modifierBits[code]
}
export function keyLabel(usage: number) {
  return usageToLabel[usage] ?? `0x${usage.toString(16)}`
}
function modsLabel(mask: number) {
  return modifierNames.filter(([bits]) => mask & bits).map(([, n]) => n)
}

// ---- Button actions -------------------------------------------------------------------------

export type Action =
  | { kind: 'mouse'; code: number }
  | { kind: 'dpi'; code: number }
  | { kind: 'sniper'; dpi: number }
  | { kind: 'key'; mods: number; usage: number }
  | { kind: 'media'; usage: number }
  | { kind: 'macro'; id: number; mode: 'once' | 'hold' | 'toggle' }
  | { kind: 'launch' }
  | { kind: 'profile'; up: boolean }
  | { kind: 'battery' }
  | { kind: 'disabled' }
  | { kind: 'unknown'; bytes: number[] }

export const MOUSE = { left: 1, right: 2, middle: 3, back: 4, forward: 5, wheelUp: 0x10, wheelDown: 0x11 }
export const DPI = { up: 1, down: 2, cycleUp: 6, cycleDown: 7 }
export const MEDIA = { playPause: 0xcd, next: 0xb5, prev: 0xb6, stop: 0xb7, mute: 0xe2, volUp: 0xe9, volDown: 0xea }

export function decodeAction(b: number[]): Action {
  const [t, n, a, c, d] = b
  if (t === 0x00) return { kind: 'disabled' }
  if (t === 0x01) return { kind: 'mouse', code: a }
  if (t === 0x07 && n === 0x01) return { kind: 'dpi', code: a }
  if (t === 0x07 && n === 0x05) return { kind: 'sniper', dpi: (c << 8) | d }
  if (t === 0x04) return { kind: 'key', mods: a, usage: c }
  if (t === 0x05 && d === 0x0f) return { kind: 'launch' }
  if (t === 0x05) return { kind: 'media', usage: (a << 8) | c }
  if (t === 0x10) return { kind: 'macro', id: (a << 8) | c, mode: 'once' }
  if (t === 0x11) return { kind: 'macro', id: (a << 8) | c, mode: 'hold' }
  if (t === 0x12) return { kind: 'macro', id: (a << 8) | c, mode: 'toggle' }
  if (t === 0x08) return { kind: 'profile', up: a === 0x04 }
  if (t === 0x0c) return { kind: 'battery' }
  return { kind: 'unknown', bytes: b }
}

export function encodeAction(a: Action, buttonId: number): number[] {
  switch (a.kind) {
    case 'disabled': return [0, 0]
    case 'mouse': return [0x01, 0x01, a.code]
    case 'dpi': return [0x07, 0x01, a.code]
    case 'sniper': return [0x07, 0x05, 0x05, a.dpi >> 8, a.dpi & 0xff, a.dpi >> 8, a.dpi & 0xff]
    case 'key': return [0x04, 0x02, a.mods, a.usage]
    case 'media': return [0x05, 0x02, a.usage >> 8, a.usage & 0xff]
    case 'macro': return a.mode === 'once' ? [0x10, 0x03, a.id >> 8, a.id & 0xff, 0x01] : [a.mode === 'hold' ? 0x11 : 0x12, 0x02, a.id >> 8, a.id & 0xff, 0x00]
    case 'launch': return [0x05, 0x02, buttonId, 0x0f]
    case 'profile': return [0x08, 0x01, a.up ? 0x04 : 0x03]
    case 'battery': return [0x0c, 0x01, 0x01]
    case 'unknown': return a.bytes
  }
}


export const mouseLabels: Record<number, string> = {
  1: t.hidLeftClick, 2: t.hidRightClick, 3: t.hidMiddleClick,
  4: t.hidBack, 5: t.hidForward, 0x10: t.hidScrollUp, 0x11: t.hidScrollDown,
}
export const dpiLabels: Record<number, string> = {
  1: t.hidDpiUp, 2: t.hidDpiDown, 6: t.hidDpiCycle, 7: t.hidDpiCycleReverse,
}
export const mediaLabels: Record<number, string> = {
  0xcd: t.hidPlayPause, 0xb5: t.hidNextTrack, 0xb6: t.hidPreviousTrack,
  0xb7: t.hidStop, 0xe2: t.hidMute, 0xe9: t.hidVolumeUp, 0xea: t.hidVolumeDown,
}

export function describeAction(a: Action, macroName?: (id: number) => string): string {
  switch (a.kind) {
    case 'disabled': return t.hidDisabled
    case 'mouse': return mouseLabels[a.code] ?? `Mouse ${a.code}`
    case 'dpi': return dpiLabels[a.code] ?? `DPI ${a.code}`
    case 'sniper': return `Sniper · ${a.dpi} DPI`
    case 'key': return [...modsLabel(a.mods), keyLabel(a.usage)].join(' + ')
    case 'media': return mediaLabels[a.usage] ?? `Media ${a.usage.toString(16)}`
    case 'macro': return `${macroName ? macroName(a.id) : `${t.macro} ${a.id}`} · ${a.mode === 'once' ? t.hidOnce : a.mode === 'hold' ? t.hidWhileHeld : t.hidToggle}`
    case 'launch': return t.hidLaunchProgram
    case 'profile': return a.up ? t.hidNextProfile : t.hidPreviousProfile
    case 'battery': return t.hidBatteryIndicator
    case 'unknown': return a.bytes.slice(0, 5).map((x) => x.toString(16).padStart(2, '0')).join(' ')
  }
}

// ---- Macros ---------------------------------------------------------------------------------

export type MacroEvent =
  | { type: 'key'; down: boolean; usage: number }
  | { type: 'mod'; down: boolean; bit: number }
  | { type: 'mouse'; mask: number } // mask of buttons held after this event (L1 R2 M4 Back8 Fwd16)
export interface TimedEvent { delay: number; event: MacroEvent } // delay = wait before this event

export function decodeMacro(b: number[]): TimedEvent[] {
  const out: TimedEvent[] = []
  let delay = 0
  for (let i = 0; i < b.length; ) {
    const op = b[i]
    if (op === 0x20) { delay += b[i + 1]; i += 2; continue }
    if (op === 0x21) { delay += (b[i + 1] << 8) | b[i + 2]; i += 3; continue }
    let ev: MacroEvent | null = null
    if (op === 0x01) ev = { type: 'mouse', mask: b[i + 1] }
    else if (op === 0x02 || op === 0x03) ev = { type: 'key', down: op === 0x02, usage: b[i + 1] }
    else if (op === 0x09 || op === 0x0a) ev = { type: 'mod', down: op === 0x09, bit: b[i + 1] }
    if (!ev) break // unknown opcode: stop rather than misread the rest
    out.push({ delay: out.length === 0 ? 0 : delay, event: ev })
    delay = 0
    i += 2
  }
  return out
}

export function encodeMacro(events: TimedEvent[]): number[] {
  const out: number[] = []
  events.forEach((t, i) => {
    if (i > 0 && t.delay > 0) {
      const d = Math.min(0xffff, Math.round(t.delay))
      if (d <= 0xff) out.push(0x20, d)
      else out.push(0x21, d >> 8, d & 0xff)
    }
    const e = t.event
    if (e.type === 'mouse') out.push(0x01, e.mask)
    else if (e.type === 'key') out.push(e.down ? 0x02 : 0x03, e.usage)
    else out.push(e.down ? 0x09 : 0x0a, e.bit)
  })
  return out
}

const mouseBitNames: [number, string][] = [[1, t.hidLeft], [2, t.hidRight], [4, t.hidMiddle], [8, t.hidBack], [16, t.hidForward]]
const modBitNames: Record<number, string> = { 1: 'L Ctrl', 2: 'L Shift', 4: 'L Alt', 8: 'L Win', 0x10: 'R Ctrl', 0x20: 'R Shift', 0x40: 'R Alt', 0x80: 'R Win' }

export function describeEvent(e: MacroEvent, prevMask: number): { label: string; down: boolean } {
  if (e.type === 'key') return { label: keyLabel(e.usage), down: e.down }
  if (e.type === 'mod') return { label: modBitNames[e.bit] ?? 'Mod', down: e.down }
  const changed = e.mask ^ prevMask
  const name = mouseBitNames.find(([bit]) => changed & bit)?.[1] ?? 'Mouse'
  return { label: tf('hidClick', { name }), down: (e.mask & changed) !== 0 }
}

/** Browser MouseEvent.button -> bit in the macro mouse mask. */
export const mouseButtonBit: Record<number, number> = { 0: 1, 2: 2, 1: 4, 3: 8, 4: 16 }
