import { animate, motion, useMotionValue, useTransform } from 'motion/react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Bluetooth, Cable, Keyboard, Mouse, Headphones, Radio, Box } from 'lucide-react'
import type { Kind, Link } from '../bridge'
import { t } from '../i18n'

/** Number that tweens to its new value instead of jumping. */
export function AnimatedNumber({ value, className }: { value: number; className?: string }) {
  const mv = useMotionValue(value)
  const text = useTransform(mv, (v) => Math.round(v).toString())
  useEffect(() => {
    const c = animate(mv, value, { duration: 0.6, ease: [0.2, 0, 0, 1] })
    return () => c.stop()
  }, [value, mv])
  return <motion.span className={className}>{text}</motion.span>
}

/**
 * Battery as a row of cells. The last lit cell fills proportionally.
 * Charging adds a slow sweep across the lit part.
 */
export function BatteryCell({
  percent,
  color,
  charging,
  cells = 5,
  height = 10,
  width = 72,
}: {
  percent: number
  color: string
  charging?: boolean
  cells?: number
  height?: number
  width?: number | string
}) {
  const p = Math.max(0, Math.min(100, percent))
  const per = 100 / cells
  return (
    <div className="flex items-center gap-[3px]" style={{ width, height }}>
      {Array.from({ length: cells }, (_, i) => {
        const fill = Math.max(0, Math.min(1, (p - i * per) / per))
        return (
          <div key={i} className="relative h-full flex-1 overflow-hidden rounded-[3px] bg-white/[0.07]">
            <motion.div
              className="absolute inset-y-0 left-0 rounded-[3px]"
              style={{ background: color }}
              initial={false}
              animate={{ width: `${fill * 100}%` }}
              transition={{ duration: 0.6, ease: [0.2, 0, 0, 1] }}
            />
            {charging && fill > 0 && (
              <div
                className="absolute inset-0"
                style={{
                  background: 'linear-gradient(90deg, transparent, rgb(255 255 255 / .55), transparent)',
                  animation: `shimmer 1.6s ${i * 0.12}s ease-in-out infinite`,
                }}
              />
            )}
          </div>
        )
      })}
    </div>
  )
}

export function KindIcon({ kind, size = 16, className }: { kind: Kind; size?: number; className?: string }) {
  const Icon = kind === 'keyboard' ? Keyboard : kind === 'headset' ? Headphones : kind === 'mouse' ? Mouse : Box
  return <Icon size={size} strokeWidth={1.5} className={className} />
}

export function LinkIcon({ link, size = 14 }: { link: Link; size?: number }) {
  const Icon = link === 'usb' ? Cable : link === 'bluetooth' ? Bluetooth : Radio
  return <Icon size={size} strokeWidth={1.75} />
}

export function linkLabel(link: Link) {
  return link === 'usb' ? t.usb : link === 'bluetooth' ? t.bluetooth : t.receiver
}

export function Chip({ children, tone = 'muted', title }: { children: ReactNode; tone?: 'muted' | 'warn' | 'ok' | 'accent'; title?: string }) {
  const tones = {
    muted: 'text-fg-muted bg-white/[0.05] ring-white/[0.06]',
    warn: 'text-warn bg-warn/10 ring-warn/20',
    ok: 'text-ok bg-ok/10 ring-ok/20',
    accent: 'text-accent bg-accent/10 ring-accent/20',
  }
  return (
    <span title={title} className={`inline-flex h-[22px] items-center gap-1.5 rounded-full px-2.5 text-[11px] font-medium ring-1 ring-inset ${tones[tone]}`}>
      {children}
    </span>
  )
}

export function Toggle({ checked, onChange }: { checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className={`relative h-6 w-11 shrink-0 rounded-full transition-colors duration-150 ${checked ? 'bg-accent' : 'bg-white/[0.12] hover:bg-white/[0.16]'}`}
    >
      <motion.span
        className="absolute top-1 size-4 rounded-full bg-white shadow-[0_1px_3px_rgb(0_0_0/.4)]"
        initial={false}
        animate={{ left: checked ? 24 : 4 }}
        transition={{ type: 'spring', stiffness: 700, damping: 40 }}
      />
    </button>
  )
}

export function Segmented<T extends string | number>({
  value,
  options,
  onChange,
  id,
}: {
  value: T
  options: { value: T; label: string }[]
  onChange: (v: T) => void
  id: string
}) {
  return (
    <div className="flex rounded-[10px] bg-white/[0.05] p-[3px] ring-1 ring-inset ring-white/[0.06]">
      {options.map((o) => {
        const active = o.value === value
        return (
          <button
            key={String(o.value)}
            onClick={() => {
              if (o.value !== value) onChange(o.value)
            }}
            className={`relative h-7 rounded-[7px] px-2.5 text-[12.5px] font-medium transition-colors duration-150 ${active ? 'text-fg' : 'text-fg-muted hover:text-fg'}`}
          >
            {active && (
              <motion.span
                layoutId={`seg-${id}`}
                className="absolute inset-0 rounded-[7px] bg-raised shadow-[0_1px_2px_rgb(0_0_0/.4),inset_0_0_0_1px_rgb(255_255_255/.08)]"
                transition={{ type: 'spring', stiffness: 600, damping: 45 }}
              />
            )}
            <span className="relative tabular">{o.label}</span>
          </button>
        )
      })}
    </div>
  )
}

export function Slider({ value: actual, min, max, step, onChange, format }: { value: number; min: number; max: number; step: number; onChange: (v: number) => void; format: (v: number) => string }) {
  // While dragging, the thumb follows the pointer locally; the device gets one write when it is let go
  // (the native "change" event: pointer up, or a key press when focused).
  const [drag, setDrag] = useState<number | null>(null)
  const input = useRef<HTMLInputElement>(null)
  const onChangeRef = useRef(onChange)
  onChangeRef.current = onChange
  const sent = useRef(actual)
  sent.current = drag == null ? actual : sent.current
  useEffect(() => {
    const el = input.current
    if (!el) return
    const commit = () => {
      const v = Number(el.value)
      setDrag(null)
      if (v !== sent.current) {
        sent.current = v
        onChangeRef.current(v)
      }
    }
    el.addEventListener('change', commit)
    return () => el.removeEventListener('change', commit)
  }, [])
  const value = drag ?? actual
  const p = ((value - min) / (max - min)) * 100
  return (
    <div className="flex items-center gap-4">
      <div className="relative h-6 w-36">
        <div className="absolute top-1/2 h-1 w-full -translate-y-1/2 rounded-full bg-white/[0.1]" />
        <div className="absolute top-1/2 h-1 -translate-y-1/2 rounded-full bg-accent" style={{ width: `${p}%` }} />
        <div
          className="pointer-events-none absolute top-1/2 size-4 -translate-x-1/2 -translate-y-1/2 rounded-full bg-white shadow-[0_0_0_4px_rgb(76_194_255/.18),0_1px_3px_rgb(0_0_0/.5)]"
          style={{ left: `${p}%` }}
        />
        <input
          ref={input}
          type="range"
          min={min}
          max={max}
          step={step}
          value={value}
          onChange={(e) => setDrag(Number(e.target.value))}
          className="absolute inset-0 w-full cursor-pointer opacity-0"
        />
      </div>
      <span className="w-10 text-right text-[13px] font-medium tabular text-fg">{format(value)}</span>
    </div>
  )
}

export function IconButton({ onClick, children, title }: { onClick: () => void; children: ReactNode; title?: string }) {
  return (
    <button
      title={title}
      onClick={onClick}
      className="grid size-9 place-items-center rounded-[10px] text-fg-muted transition-colors duration-150 hover:bg-white/[0.06] hover:text-fg active:scale-95"
    >
      {children}
    </button>
  )
}

export function Button({ onClick, children, icon }: { onClick: () => void; children: ReactNode; icon?: ReactNode }) {
  return (
    <button
      onClick={onClick}
      className="inline-flex h-9 items-center gap-2 rounded-[10px] bg-white/[0.06] px-3.5 text-[13px] font-medium text-fg ring-1 ring-inset ring-white/[0.06] transition-colors duration-150 hover:bg-white/[0.1] active:scale-[.98]"
    >
      {icon}
      {children}
    </button>
  )
}

/** A card of setting rows: name and hint on the left, the control on the right. */
export function Rows({ title, hint, children }: { title?: string; hint?: string; children: ReactNode }) {
  return (
    <section>
      {(title || hint) && (
        <div className="mb-3 flex items-baseline justify-between gap-4">
          {title && <div className="label">{title}</div>}
          {hint && <div className="text-[11.5px] text-fg-faint">{hint}</div>}
        </div>
      )}
      <div className="divide-y divide-line overflow-hidden rounded-xl bg-white/[0.025] ring-1 ring-inset ring-line">{children}</div>
    </section>
  )
}

export function Row({ title, hint, children, stack }: { title: string; hint?: string; children: ReactNode; stack?: boolean }) {
  if (stack)
    return (
      <div className="flex flex-col gap-3 px-5 py-3.5">
        <div>
          <div className="text-[13.5px] font-medium text-fg">{title}</div>
          {hint && <div className="mt-0.5 text-[12px] leading-[17px] text-fg-faint">{hint}</div>}
        </div>
        <div className="flex flex-wrap items-center gap-3">{children}</div>
      </div>
    )
  return (
    // The text keeps at least ~240px; when the controls don't fit next to it they wrap onto their own line.
    <div className="flex min-h-[52px] flex-wrap items-center justify-between gap-x-6 gap-y-2.5 px-5 py-2.5">
      <div className="min-w-0 flex-1 basis-[200px]">
        <div className="text-[13.5px] font-medium text-fg">{title}</div>
        {hint && <div className="mt-0.5 text-[12px] leading-[17px] text-fg-faint">{hint}</div>}
      </div>
      <div className="flex shrink-0 items-center gap-3">{children}</div>
    </div>
  )
}
