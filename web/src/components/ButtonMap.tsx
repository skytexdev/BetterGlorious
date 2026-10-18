import { motion } from 'motion/react'
import { t } from '../i18n'

export interface LayoutItem { id: string; side: 'left' | 'right'; point: [number, number] }
export interface Layout { width: number; height: number; bbox: [number, number, number, number]; items: LayoutItem[] }

// CORE's component names -> the firmware button ids used everywhere else.
export const LAYOUT_BUTTON: Record<string, number> = {
  LeftButton: 0x01, RightButton: 0x02, MiddleButton: 0x03, BackButton: 0x04, ForwardButton: 0x05, DPICycleButton: 0x14,
}

const NAME: Record<string, keyof typeof t> = {
  LeftButton: 'btnLeft', RightButton: 'btnRight', MiddleButton: 'btnMiddle', BackButton: 'btnBackShort', ForwardButton: 'btnForwardShort', DPICycleButton: 'btnDpi',
}

const LABEL_W = 116
const LABEL_H = 36
const GUTTER = 20 // space between the mouse and the labels
const GAP = 10 // minimum space between stacked labels

/**
 * The mouse with a callout per button, Glorious CORE style: the render large in the middle,
 * labels down both sides at the height of their button, a line to the exact spot. Button spots
 * come from CORE's layout data with small corrections (tools/genimages.py). Click to edit.
 */
export function ButtonMap({ layout, image, selected, onSelect, actionLabel, changed }: {
  layout: Layout
  image: string
  selected: number | null
  onSelect: (id: number) => void
  actionLabel: (id: number) => string
  changed: (id: number) => boolean
}) {
  const { width: W, height: H, bbox } = layout
  const [bx0, by0, bx1, by1] = bbox
  const vx0 = bx0 - GUTTER - LABEL_W - 6
  const vx1 = bx1 + GUTTER + LABEL_W + 6
  const vy0 = by0 - 6
  const vy1 = by1 + 14

  // Each side's labels: in button order, spread evenly over the upper part of the mouse so the
  // lines stay short and never cross; the lines show which button each label belongs to.
  const placed = new Map<string, number>()
  for (const side of ['left', 'right'] as const) {
    const items = layout.items.filter((i) => i.side === side && LAYOUT_BUTTON[i.id] != null).sort((a, b) => a.point[1] - b.point[1])
    if (!items.length) continue
    const top = by0 + 4
    const span = Math.max((by1 - by0) * 0.62, items.length * (LABEL_H + GAP))
    const step = items.length > 1 ? (span - LABEL_H) / (items.length - 1) : 0
    items.forEach((it, i) => {
      const even = top + i * step
      // pull toward the button's own height, but keep the order and spacing
      placed.set(it.id, Math.max(even, Math.min(it.point[1] - LABEL_H / 2, even + step / 2)))
    })
  }

  return (
    <svg viewBox={`${vx0} ${vy0} ${vx1 - vx0} ${vy1 - vy0}`} className="h-auto max-h-[calc(100vh-150px)] w-full select-none">
      <defs>
        <radialGradient id="bm-floor" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#000" stopOpacity=".75" />
          <stop offset="100%" stopColor="#000" stopOpacity="0" />
        </radialGradient>
      </defs>
      <ellipse cx={(bx0 + bx1) / 2} cy={by1 + 4} rx={(bx1 - bx0) * 0.42} ry={8} fill="url(#bm-floor)" />
      <image href={image} x={0} y={0} width={W} height={H} />
      {layout.items.map((it) => {
        const id = LAYOUT_BUTTON[it.id]
        const ly = placed.get(it.id)
        if (id == null || ly == null) return null
        const left = it.side === 'left'
        const lx = left ? bx0 - GUTTER - LABEL_W : bx1 + GUTTER
        const [px, py] = it.point
        const sx = left ? lx + LABEL_W : lx
        const sy = ly + LABEL_H / 2
        const elbow = left ? bx0 - 10 : bx1 + 10
        const active = selected === id
        const accent = 'var(--color-accent)'
        const tx = left ? lx + 11 : lx + LABEL_W - 11
        return (
          <g key={it.id} onClick={() => onSelect(id)} style={{ cursor: 'pointer' }} className="group">
            <path d={`M ${sx} ${sy} L ${elbow} ${sy} L ${px} ${py}`} fill="none" stroke={active ? accent : 'rgba(255,255,255,.3)'} strokeWidth={active ? 1.5 : 1} strokeLinejoin="round" />
            {active && <motion.circle cx={px} cy={py} fill="none" stroke={accent} strokeWidth={1.2} initial={{ r: 4, opacity: 0 }} animate={{ r: [4, 12], opacity: [0, 0.85, 0] }} transition={{ duration: 1.6, repeat: Infinity, ease: 'easeOut', times: [0, 0.18, 1] }} />}
            <circle cx={px} cy={py} r={active ? 4.2 : 3.2} fill={active ? accent : '#fff'} stroke="rgba(0,0,0,.6)" strokeWidth={1.2} />
            <rect x={lx} y={ly} width={LABEL_W} height={LABEL_H} rx={9} fill={active ? 'rgba(76,194,255,.14)' : 'rgba(27,29,33,.94)'} stroke={active ? accent : 'rgba(255,255,255,.1)'} strokeWidth={active ? 1.3 : 1} className="transition-colors group-hover:fill-[rgba(40,43,48,.96)]" />
            <text x={tx} y={ly + 15.5} textAnchor={left ? 'start' : 'end'} fontSize={11.5} fontWeight={600} fill="#f2f3f5" fontFamily="Geist, Segoe UI, sans-serif">
              {t[NAME[it.id]] as string}
            </text>
            <text x={tx} y={ly + 28.5} textAnchor={left ? 'start' : 'end'} fontSize={9.5} fontWeight={500} fill={changed(id) ? accent : '#8a909a'} fontFamily="Geist, Segoe UI, sans-serif">
              {truncate(actionLabel(id), 20)}
            </text>
          </g>
        )
      })}
    </svg>
  )
}

function truncate(s: string, n: number) {
  return s.length > n ? s.slice(0, n - 1) + '…' : s
}
