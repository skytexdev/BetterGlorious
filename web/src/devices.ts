import type { Device } from './bridge'

import images from './deviceImages.json'

export interface ProductColor { id: string; label: string; hex: string; file: string; map?: string; mask?: string }

// Catalog id -> official render and the colorways the product ships in, generated from
// Glorious CORE's device definitions by tools/genimages.py.
const table = images as unknown as Record<string, { image: string; colors: ProductColor[]; layout?: import('./components/ButtonMap').Layout }>

export function layoutFor(d: Device) {
  return table[d.key]?.layout ?? null
}

/** The untrimmed render that lines up with the button layout. */
export function mapImageFor(d: Device): string | null {
  const colors = colorsFor(d)
  const c = colors.find((x) => x.id === d.color) ?? colors[0]
  return c?.map ? `./devices/${c.map}` : null
}

export function colorsFor(d: Device): ProductColor[] {
  return table[d.key]?.colors ?? []
}

export function renderFor(d: Device, color = d.color): string | null {
  const colors = colorsFor(d)
  const c = colors.find((x) => x.id === color) ?? colors[0]
  return c ? `./devices/${c.file}` : null
}

/** Where the LEDs shine through on the render (same size and crop as renderFor), if CORE ships one. */
export function maskFor(d: Device, color = d.color): string | null {
  const colors = colorsFor(d)
  const c = colors.find((x) => x.id === color) ?? colors[0]
  return c?.mask ? `./devices/${c.mask}` : null
}

/** The link in use: the one the last reading came over, else the best one present. */
export function activeLink(d: Device) {
  return d.activeLink ?? d.links[0] ?? null
}

export type Level = 'charging' | 'full' | 'ok' | 'mid' | 'low' | 'critical' | 'stale'

export function levelOf(d: Device, lowThreshold: number): Level {
  if (d.percent < 0 || d.state !== 'ok') return 'stale'
  if (d.charge === 'charging') return 'charging'
  if (d.charge === 'full') return 'full'
  if (d.percent <= Math.min(10, lowThreshold)) return 'critical'
  if (d.percent <= lowThreshold) return 'low'
  if (d.percent <= 50) return 'mid'
  return 'ok'
}

export const levelColor: Record<Level, string> = {
  charging: 'var(--color-accent)',
  full: 'var(--color-ok)',
  ok: 'var(--color-fg)',
  mid: 'var(--color-fg)',
  low: 'var(--color-warn)',
  critical: 'var(--color-bad)',
  stale: 'var(--color-fg-faint)',
}
