// Software macros: played by BetterGlorious on the PC (Razer Synapse style), not by the mouse.
// A mouse button is mapped onboard to F13–F24; the app catches that key and plays the macro.
import { send } from './bridge'
import type { TimedEvent } from './hid'

export type SoftMode = 'once' | 'hold' | 'toggle' | 'repeat'

export interface SoftMacro {
  id: number
  name: string
  trigger: number | null // 0..11 = F13..F24
  mode: SoftMode
  repeat: number
  minHold: number // ms every press stays down at least
  gap: number // ms to wait before the next repetition
  jitter?: number // ± ms random variation on every delay
  events: TimedEvent[]
  owner?: { device: string; button: number } // set for macros created from a button (rapid fire)
  kind?: 'rapid' | 'macro'
}

export const TRIGGERS = 12
export const F13_USAGE = 0x68

export function saveAll(list: SoftMacro[]) {
  send('softMacros', { json: JSON.stringify(list) })
}

export function nextId(list: SoftMacro[]) {
  return list.reduce((m, x) => Math.max(m, x.id), 0) + 1
}

/** A free F13–F24 slot, preferring the one the macro already has. */
export function freeTrigger(list: SoftMacro[], keep?: number | null): number | null {
  if (keep != null) return keep
  const used = new Set(list.map((m) => m.trigger).filter((t) => t != null))
  for (let i = 0; i < TRIGGERS; i++) if (!used.has(i)) return i
  return null
}

/** Button action bytes that make the mouse send F13+slot. */
export function triggerAction(slot: number): number[] {
  return [0x04, 0x02, 0x00, F13_USAGE + slot]
}

/** Slot for a button whose onboard action is F13–F24 with no modifiers, else null. */
export function slotOfAction(bytes: number[] | undefined): number | null {
  if (!bytes || bytes[0] !== 0x04 || bytes[2] !== 0) return null
  const s = bytes[3] - F13_USAGE
  return s >= 0 && s < TRIGGERS ? s : null
}

export type RapidTarget = { type: 'mouse'; bit: number } | { type: 'key'; usage: number }

/** One press + release; the macro loops it with `gap` between presses. */
export function rapidEvents(target: RapidTarget, hold: number): TimedEvent[] {
  return target.type === 'mouse'
    ? [
        { delay: 0, event: { type: 'mouse', mask: target.bit } },
        { delay: hold, event: { type: 'mouse', mask: 0 } },
      ]
    : [
        { delay: 0, event: { type: 'key', down: true, usage: target.usage } },
        { delay: hold, event: { type: 'key', down: false, usage: target.usage } },
      ]
}

export function rapidTargetOf(m: SoftMacro): RapidTarget | null {
  const e = m.events[0]?.event
  if (!e) return null
  if (e.type === 'mouse') return { type: 'mouse', bit: e.mask }
  if (e.type === 'key') return { type: 'key', usage: e.usage }
  return null
}
