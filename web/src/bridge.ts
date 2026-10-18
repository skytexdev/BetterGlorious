// Talks to the native host (WebPanel.cs) over WebView2's message channel.
// Opened in a normal browser (npm run dev) it falls back to mock data.

import { useEffect, useRef, useState } from 'react'

export type Link = 'usb' | 'receiver' | 'bluetooth'
export type Kind = 'mouse' | 'keyboard' | 'headset' | 'mousepad' | 'other'
export type Charge = 'unknown' | 'discharging' | 'charging' | 'full'
export type ReadState = 'ok' | 'asleep' | 'notready' | 'waiting'

export interface Device {
  key: string
  name: string
  kind: Kind
  family: string
  hasBattery: boolean
  verified: boolean
  inTray: boolean
  links: Link[]
  activeLink: Link | null
  color: string
  configurable: boolean
  brightness: number | null
  brightnessWireless?: number | null
  launch: Record<string, string>
  config: MouseConfig | null
  canUndo: boolean
  autoProfile: import('./components/AutoProfile').AutoProfileState | null
  canRestore: boolean
  percent: number
  charge: Charge
  state: ReadState
  time: string
}

export interface MouseConfig {
  profile: number
  firmware: string
  dpi: number[]
  dpiColors: string[]
  activeStage: number
  pollingHz: number
  debounceMs: number
  liftOff: number
  sleepSeconds: number
  effect: number
  effectSpeed: number
  effectColor: string
  effectColors?: string[]
  effectCustom?: boolean
  maxStages: number
  minDpi: number
  maxDpi: number
  buttons: Record<string, number[]>
  macros: Record<string, number[]>
  maxMacros: number
  motionSync?: boolean
  features: {
    readback: boolean; buttons: boolean; macros: boolean; motionSync: boolean; profiles: number; sleepMinutes: number[]
    // Keyboards and other families override the mouse defaults:
    dpi?: boolean; liftOff?: boolean; keyboard?: boolean; sleepNever?: boolean
    sleepRange?: [number, number]; liftOffLabels?: string[]; separateBrightness?: boolean
    pollingRates?: number[]; debounceOptions?: number[]
    effects?: { id: number; key: string; color: boolean }[]
  }
}

export interface AppSettings {
  startup: boolean
  notifications: boolean
  pollSeconds: number
  lowBattery: number
  tray: 'per_device' | 'lowest'
  language: 'auto' | 'tr' | 'en'
  minimizeToTray: boolean
}

export interface AppState {
  version: string
  update?: { version: string; url: string } | null
  softMacros: import('./softmacros').SoftMacro[]
  foreground: { exe: string; title: string }
  settings: AppSettings
  devices: Device[]
}

type Message = { type: string; data: unknown }

interface WebView {
  postMessage(msg: unknown): void
  addEventListener(type: 'message', fn: (e: { data: Message }) => void): void
}

const webview: WebView | undefined = (window as unknown as { chrome?: { webview?: WebView } }).chrome?.webview

export function send(cmd: string, args: Record<string, unknown> = {}) {
  if (webview) webview.postMessage({ cmd, ...args })
  else mockHandle(cmd, args)
}

type Listener = (m: Message) => void
const listeners = new Set<Listener>()
function emit(m: Message) {
  listeners.forEach((l) => l(m))
}
webview?.addEventListener('message', (e) => emit(e.data))

// ---- Settings changes -------------------------------------------------------------------
//
// A change is shown right away and stays on screen until the host has written it to the device and
// read it back ("cfgDone" with the same id). Read-backs of earlier changes that arrive in between
// can't flip the screen back to an old value, and only the newest change per setting counts.

interface Pending { key: string; field: string; value: unknown; id: number; at: number }
const pending = new Map<string, Pending>()
const pendingListeners = new Set<() => void>()
let nextId = 1

function slot(key: string, field: string, value: unknown) {
  const v = value as Record<string, unknown> | null
  if (field === 'button' && v) return `${key}|button:${v.id}`
  if (field === 'dpiColor' && v) return `${key}|dpiColor:${v.index}`
  return `${key}|${field}`
}

/** Send one device setting. Use this instead of send('cfg', ...). */
export function sendCfg(key: string, field: string, value: unknown) {
  const id = nextId++
  if (field !== 'macro' && field !== 'deleteMacro') pending.set(slot(key, field, value), { key, field, value, id, at: Date.now() })
  pendingListeners.forEach((l) => l())
  send('cfg', { key, field, value, id })
}

function settle(id: number) {
  for (const [k, p] of pending) if (p.id === id) pending.delete(k)
  pendingListeners.forEach((l) => l())
}

// Never let a lost ack pin a value forever.
window.setInterval(() => {
  let changed = false
  for (const [k, p] of pending) if (Date.now() - p.at > 20000) { pending.delete(k); changed = true }
  if (changed) pendingListeners.forEach((l) => l())
}, 5000)

/** The state as the user expects to see it: the device's values with pending changes on top. */
function overlay(state: AppState): AppState {
  if (pending.size === 0) return state
  const devices = state.devices.map((d) => {
    const mine = [...pending.values()].filter((p) => p.key === d.key)
    if (mine.length === 0 || !d.config) return d
    const c: MouseConfig = { ...d.config, buttons: { ...d.config.buttons }, dpiColors: [...d.config.dpiColors] }
    let brightness = d.brightness
    let brightnessWireless = d.brightnessWireless
    for (const { field, value } of mine) {
      const v = value as never as Record<string, unknown>
      switch (field) {
        case 'polling': c.pollingHz = value as number; break
        case 'debounce': c.debounceMs = value as number; break
        case 'liftOff': c.liftOff = value as number; break
        case 'motionSync': c.motionSync = value as boolean; break
        case 'sleep': c.sleepSeconds = value as number; break
        case 'profile': c.profile = value as number; break
        case 'activeStage': c.activeStage = value as number; break
        case 'dpi': c.dpi = value as number[]; c.activeStage = Math.min(c.activeStage, c.dpi.length - 1); break
        case 'dpiColor': c.dpiColors[v.index as number] = v.color as string; break
        case 'effect': c.effect = v.effect as number; c.effectColor = v.color as string; c.effectSpeed = v.speed as number; c.effectCustom = !!v.custom; break
        case 'brightness': brightness = value as number; break
        case 'brightnessWireless': brightnessWireless = (value as number) < 0 ? null : (value as number); break
        case 'button': { const a = [...(v.action as number[])]; while (a.length < 7) a.push(0); c.buttons[String(v.id)] = a; break }
      }
    }
    return { ...d, config: c, brightness, brightnessWireless }
  })
  return { ...state, devices }
}

export function useAppState() {
  const [raw, setRaw] = useState<AppState | null>(null)
  const [, setTick] = useState(0)
  const [toast, setToast] = useState<string | null>(null)
  useEffect(() => {
    const bump = () => setTick((n) => n + 1)
    pendingListeners.add(bump)
    const l: Listener = (m) => {
      if (m.type === 'state') setRaw(m.data as AppState)
      else if (m.type === 'cfgDone') settle((m.data as { id: number }).id)
      else if (m.type === 'toast') {
        setToast(m.data as string)
        window.setTimeout(() => setToast(null), 2600)
      }
    }
    listeners.add(l)
    send('ready')
    return () => {
      listeners.delete(l)
      pendingListeners.delete(bump)
    }
  }, [])
  return { state: raw && overlay(raw), toast }
}

/** Subscribe to one message type from the host (e.g. 'picked' after a file dialog). */
export function useMessage<T>(type: string, fn: (data: T) => void) {
  const ref = useRef(fn)
  ref.current = fn
  useEffect(() => {
    const l: Listener = (m) => {
      if (m.type === type) ref.current(m.data as T)
    }
    listeners.add(l)
    return () => {
      listeners.delete(l)
    }
  }, [type])
}

// ---- Browser mock -------------------------------------------------------------------------

const mock: AppState = {
  version: 'dev',
  softMacros: [],
  foreground: { exe: 'javaw.exe', title: 'Minecraft 1.21.4' },
  settings: { startup: true, notifications: true, pollSeconds: 60, lowBattery: 20, tray: 'per_device', language: 'auto', minimizeToTray: true },
  devices: [
    { key: 'ModelOWireless', name: 'Model O Wireless', kind: 'mouse', family: 'Sinowealth', hasBattery: true, verified: true, inTray: true, links: ['usb', 'receiver'], activeLink: 'usb', color: 'black', configurable: true, brightness: 100, launch: {}, config: { profile: 1, firmware: '0.3.8.1', dpi: [400, 800, 1600, 3200], dpiColors: ['#ffff00', '#0000ff', '#ff0000', '#00ff00'], activeStage: 2, pollingHz: 1000, debounceMs: 10, liftOff: 0, sleepSeconds: 420, effect: 1, effectSpeed: 60, effectColor: '#ff0000', maxStages: 6, minDpi: 100, maxDpi: 19000, buttons: { '1': [1, 1, 1, 0, 0, 0, 0], '2': [1, 1, 2, 0, 0, 0, 0], '3': [1, 1, 3, 0, 0, 0, 0], '4': [1, 1, 4, 0, 0, 0, 0], '5': [4, 2, 1, 6, 0, 0, 0], '16': [1, 1, 16, 0, 0, 0, 0], '17': [1, 1, 17, 0, 0, 0, 0], '20': [7, 1, 6, 0, 0, 0, 0] }, macros: { '1': [1, 1, 32, 60, 1, 0] }, maxMacros: 8, features: { readback: true, buttons: true, macros: true, motionSync: false, profiles: 3, sleepMinutes: [1, 3, 5, 7, 10, 15] } }, canUndo: true, canRestore: true, autoProfile: { enabled: true, fallback: 1, rules: [{ exe: 'javaw.exe', title: 'Minecraft', label: 'Minecraft 1.21.4', profile: 2 }, { exe: 'cs2.exe', title: '', label: 'Counter-Strike 2', profile: 3 }] }, percent: 84, charge: 'charging', state: 'ok', time: '14:07' },
    { key: 'GMMK3PROHE65WirelessANSI', name: 'GMMK 3 PRO HE 65 Wireless ANSI', kind: 'keyboard', family: 'Gmmk3He', hasBattery: true, verified: false, inTray: true, links: ['receiver'], activeLink: 'receiver', color: 'black', configurable: true, brightness: 80, launch: {}, config: { profile: 1, firmware: '', dpi: [], dpiColors: [], activeStage: 0, pollingHz: 1000, debounceMs: 16, liftOff: 0, motionSync: false, sleepSeconds: 420, effect: 1, effectSpeed: 50, effectColor: '#4cc2ff', maxStages: 0, minDpi: 0, maxDpi: 0, buttons: {}, macros: {}, maxMacros: 0, features: { readback: false, buttons: false, macros: false, motionSync: false, profiles: 3, dpi: false, liftOff: false, keyboard: true, pollingRates: [125, 250, 500, 1000, 2000, 4000, 8000], debounceOptions: [0, 2, 8, 16], sleepMinutes: [1, 3, 5, 7, 10, 15, 30], sleepNever: true, effects: ['kbOff', 'kbGlorious', 'kbWave1', 'kbWave2', 'kbSpiral', 'kbAcid', 'kbBreathing', 'kbStatic', 'kbRipple', 'kbPassTrace', 'kbFastRun', 'kbMatrix2', 'kbMatrix3', 'kbRainbow', 'kbHeartbeat', 'kbDigital', 'kbKamehameha', 'kbPingpong', 'kbSurmount'].map((key, id) => ({ id, key, color: [6, 7, 8, 9, 10].includes(id) })) } }, canUndo: false, canRestore: false, autoProfile: null, percent: 17, charge: 'discharging', state: 'ok', time: '14:06' },
    { key: 'ModelD2Wireless', name: 'Model D2 Wireless', kind: 'mouse', family: 'PixartV2', hasBattery: true, verified: false, inTray: false, links: ['receiver'], activeLink: 'receiver', color: 'white', configurable: false, brightness: null, launch: {}, config: null, canUndo: false, canRestore: false, autoProfile: null, percent: 62, charge: 'discharging', state: 'asleep', time: '13:40' },
  ],
}

function mockHandle(cmd: string, args: Record<string, unknown>) {
  if (cmd === 'ready' || cmd === 'refresh') setTimeout(() => emit({ type: 'state', data: structuredClone(mock) }), 50)
  if (cmd === 'setting') {
    ;(mock.settings as unknown as Record<string, unknown>)[args.key as string] = args.value
    emit({ type: 'state', data: structuredClone(mock) })
  }
  if (cmd === 'tray') {
    const d = mock.devices.find((x) => x.key === args.key)
    if (d) d.inTray = Boolean(args.value)
    emit({ type: 'state', data: structuredClone(mock) })
  }
  if (cmd === 'color') {
    const d = mock.devices.find((x) => x.key === args.key)
    if (d) d.color = args.value as string
    emit({ type: 'state', data: structuredClone(mock) })
  }
  if (cmd === 'cfg') {
    const c = mock.devices.find((x) => x.key === args.key)?.config as unknown as Record<string, unknown> | undefined
    if (c) {
      const map: Record<string, string> = { polling: 'pollingHz', debounce: 'debounceMs', liftOff: 'liftOff', sleep: 'sleepSeconds', activeStage: 'activeStage', dpi: 'dpi' }
      if (map[args.field as string]) c[map[args.field as string]] = args.value
      if (args.field === 'effect') { const v = args.value as Record<string, unknown>; c.effect = v.effect; c.effectSpeed = v.speed; c.effectColor = v.color }
      const id = args.id
      window.setTimeout(() => {
        emit({ type: 'state', data: structuredClone(mock) })
        emit({ type: 'cfgDone', data: { id, ok: true } })
      }, 600)
    }
  }
  if (cmd === 'softMacros') {
    mock.softMacros = JSON.parse(args.json as string)
    emit({ type: 'state', data: structuredClone(mock) })
  }
  if (cmd === 'copyDiagnostics') emit({ type: 'toast', data: 'Diagnostics copied (mock)' })
}
