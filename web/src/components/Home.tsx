import { motion } from 'motion/react'
import { useState } from 'react'
import { Plug } from 'lucide-react'
import type { Device } from '../bridge'
import { activeLink, levelColor, levelOf } from '../devices'
import { percentFirst, t } from '../i18n'
import { Render } from './Render'
import { AnimatedNumber, BatteryCell, Chip, LinkIcon, linkLabel } from './ui'

export function Home({ devices, lowBattery, onOpen }: { devices: Device[]; lowBattery: number; onOpen: (key: string) => void }) {
  if (devices.length === 0) return <Empty />
  const size = devices.length === 1 ? 380 : devices.length === 2 ? 320 : 260
  // Battery devices first, then by name, so the interesting ones lead.
  const sorted = [...devices].sort((a, b) => Number(b.hasBattery) - Number(a.hasBattery) || a.name.localeCompare(b.name))
  return (
    <div className="flex min-h-full flex-wrap content-center items-start justify-center gap-x-6 gap-y-10 px-8 py-10">
      {sorted.map((d, i) => (
        <Stage key={d.key} device={d} size={size} lowBattery={lowBattery} index={i} onOpen={() => onOpen(d.key)} />
      ))}
    </div>
  )
}

function Stage({ device: d, size, lowBattery, index, onOpen }: { device: Device; size: number; lowBattery: number; index: number; onOpen: () => void }) {
  const [hover, setHover] = useState(false)
  const level = levelOf(d, lowBattery)
  const color = levelColor[level]
  const asleep = d.state === 'asleep'

  return (
    <motion.button
      onClick={onOpen}
      onHoverStart={() => setHover(true)}
      onHoverEnd={() => setHover(false)}
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.32, delay: index * 0.06, ease: [0.2, 0, 0, 1] }}
      className="group flex flex-col items-center text-left outline-none"
      style={{ width: size }}
    >
      <Render device={d} size={size} lift={hover} dim={asleep && false} />

      <div className="mt-1 flex w-full flex-col items-center gap-3">
        <div className="flex flex-col items-center gap-1">
          <div className="text-[15px] font-semibold tracking-[-0.01em] text-fg">{d.name}</div>
          <div className="flex items-center gap-3 text-[12px] text-fg-faint">
            {activeLink(d) && (
              <span className="inline-flex items-center gap-1.5">
                <LinkIcon link={activeLink(d)!} size={13} />
                {linkLabel(activeLink(d)!)}
              </span>
            )}
            {!d.verified && d.hasBattery && <Chip tone="warn">{t.experimental}</Chip>}
          </div>
        </div>

        {d.hasBattery ? (
          <div className="flex items-center gap-4">
            <div className="flex items-baseline" style={{ color }}>
              {percentFirst && d.percent >= 0 && <span className="mr-0.5 text-[18px] font-medium opacity-60">%</span>}
              {d.percent >= 0 ? (
                <AnimatedNumber value={d.percent} className="text-[34px] font-semibold leading-none tracking-[-0.03em] tabular" />
              ) : (
                <span className="text-[34px] font-semibold leading-none">–</span>
              )}
              {!percentFirst && d.percent >= 0 && <span className="ml-0.5 text-[18px] font-medium opacity-60">%</span>}
            </div>
            <div className="flex flex-col gap-1.5">
              <BatteryCell percent={Math.max(0, d.percent)} color={color} charging={d.charge === 'charging'} width={96} height={6} />
              <span className="text-[11.5px] font-medium" style={{ color: level === 'charging' || level === 'full' ? color : 'var(--color-fg-faint)' }}>
                {status(d)}
              </span>
            </div>
          </div>
        ) : (
          <span className="text-[12px] text-fg-faint">
            {t.wired} · {t.noBattery}
          </span>
        )}
      </div>
    </motion.button>
  )
}

export function status(d: Device) {
  if (d.state === 'asleep') return d.percent >= 0 ? `${t.asleep} · ${t.lastKnown}` : t.asleep
  if (d.percent < 0) return t.waiting
  if (d.charge === 'charging') return t.charging
  if (d.charge === 'full') return t.full
  return `${t.updated} ${d.time}`
}

function Empty() {
  return (
    <div className="grid h-full place-items-center">
      <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.32 }} className="flex max-w-sm flex-col items-center text-center">
        <div className="relative mb-6 grid size-24 place-items-center">
          <div className="absolute inset-0 rounded-full bg-accent/10 blur-2xl" />
          <div className="relative grid size-16 place-items-center rounded-2xl bg-surface ring-1 ring-inset ring-white/[0.08]">
            <Plug size={26} strokeWidth={1.5} className="text-fg-muted" />
          </div>
        </div>
        <div className="text-[20px] font-semibold tracking-[-0.01em]">{t.noDevicesTitle}</div>
        <div className="mt-2 text-[14px] leading-5 text-fg-muted">{t.noDevicesBody}</div>
      </motion.div>
    </div>
  )
}
