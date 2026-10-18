import { motion } from 'motion/react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Plus, X } from 'lucide-react'
import { sendCfg, type Device, type MouseConfig } from '../bridge'
import { t } from '../i18n'
import { Row, Rows, Segmented, Slider, Toggle } from './ui'


export function Performance({ device, config }: { device: Device; config: MouseConfig }) {
  const cfg = (field: string, value: unknown) => sendCfg(device.key, field, value)
  const [selected, setSelected] = useState(config.activeStage)
  const [dpi, setDpi] = useState(config.dpi)
  const dragging = useRef(false)

  // Follow the device unless the user is mid-drag.
  useEffect(() => {
    if (!dragging.current) setDpi(config.dpi)
    setSelected((s) => Math.min(s, config.dpi.length - 1))
  }, [config.dpi])

  const commitDpi = (next: number[]) => {
    dragging.current = false
    cfg('dpi', next)
  }

  const value = dpi[selected] ?? 800
  const SLEEP_OPTIONS = config.features.sleepMinutes.map((m) => m * 60)

  return (
    <div className="flex flex-col gap-6">
      {config.features.dpi !== false && (
      <>
      <Section title={t.dpiStages}>
        <div className="flex flex-wrap items-center gap-2">
          {dpi.map((v, i) => {
            const isSel = i === selected
            const isActive = i === config.activeStage
            return (
              <motion.button
                layout
                key={i}
                onClick={() => setSelected(i)}
                className={`relative flex h-9 min-w-[80px] items-center gap-2 rounded-lg px-3 text-left transition-colors ${isSel ? 'bg-white/[0.09] ring-1 ring-inset ring-white/15' : 'bg-white/[0.035] ring-1 ring-inset ring-line hover:bg-white/[0.06]'}`}
                style={isActive ? { boxShadow: '0 0 0 1.5px var(--color-accent), 0 0 16px rgb(76 194 255 / .25)' } : undefined}
              >
                <span className="size-2.5 rounded-full" style={{ background: config.dpiColors[i] ?? '#fff', boxShadow: `0 0 8px ${config.dpiColors[i] ?? '#fff'}` }} />
                <span className="text-[14px] font-semibold tabular">{v}</span>
              </motion.button>
            )
          })}
          {dpi.length < config.maxStages && (
            <button
              title={t.addStage}
              onClick={() => {
                const next = [...dpi, Math.min(config.maxDpi, (dpi[dpi.length - 1] ?? 800) * 2)]
                setDpi(next)
                setSelected(next.length - 1)
                commitDpi(next)
              }}
              className="grid size-9 place-items-center rounded-lg text-fg-faint ring-1 ring-inset ring-dashed ring-white/15 transition-colors hover:text-fg hover:ring-white/30"
            >
              <Plus size={16} />
            </button>
          )}
        </div>

        <div className="mt-3 rounded-xl bg-white/[0.03] px-4 py-3.5 ring-1 ring-inset ring-line">
          <div className="flex items-start justify-between">
            <div>
              <div className="label">{t.stage} {selected + 1}</div>
              <div className="mt-1 flex items-baseline gap-2">
                <span className="text-[30px] font-semibold leading-none tracking-[-0.03em] tabular">{value}</span>
                <span className="text-[14px] font-medium text-fg-faint">DPI</span>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <label className="relative size-8 cursor-pointer overflow-hidden rounded-full ring-1 ring-white/15" title={t.color} style={{ background: config.dpiColors[selected] }}>
                <input
                  type="color"
                  value={config.dpiColors[selected] ?? '#ffffff'}
                  onChange={(e) => cfg('dpiColor', { index: selected, color: e.target.value })}
                  className="absolute inset-0 cursor-pointer opacity-0"
                />
              </label>
              {dpi.length > 1 && (
                <button
                  title={t.removeStage}
                  onClick={() => {
                    const next = dpi.filter((_, i) => i !== selected)
                    setDpi(next)
                    setSelected(Math.max(0, selected - 1))
                    commitDpi(next)
                  }}
                  className="grid size-8 place-items-center rounded-full text-fg-faint ring-1 ring-inset ring-white/10 transition-colors hover:text-bad hover:ring-bad/40"
                >
                  <X size={14} />
                </button>
              )}
            </div>
          </div>
          <div className="mt-3" onPointerUp={() => dragging.current && commitDpi(dpi)}>
            <WideSlider
              value={value}
              min={config.minDpi}
              max={config.maxDpi}
              step={50}
              onChange={(v) => {
                dragging.current = true
                setDpi(dpi.map((x, i) => (i === selected ? v : x)))
              }}
            />
            <div className="mt-2 flex justify-between text-[11px] text-fg-faint tabular">
              <span>{config.minDpi}</span>
              <span>{config.maxDpi}</span>
            </div>
          </div>
          {selected !== config.activeStage && (
            <button onClick={() => cfg('activeStage', selected)} className="mt-2 text-[13px] font-medium text-accent hover:underline">
              {t.makeActive}
            </button>
          )}
        </div>
      </Section>

      </>
      )}

      <Rows title={t.sensorAndPower}>
        <Row title={`${t.pollingRate} (Hz)`} hint={t.pollingHint}>
          <Segmented id="poll" value={config.pollingHz} onChange={(v) => cfg('polling', v)} options={(config.features.pollingRates ?? [125, 250, 500, 1000]).map((v) => ({ value: v, label: v >= 1000 ? `${v / 1000}K` : `${v}` }))} />
        </Row>
        <Row title={config.features.debounceOptions ? `${t.debounce} (ms)` : t.debounce} hint={t.debounceHint}>
          {config.features.debounceOptions ? (
            <>
              <Segmented id="deb" value={config.debounceMs} onChange={(v) => cfg('debounce', v)} options={config.features.debounceOptions.map((v) => ({ value: v, label: `${v}` }))} />
            </>
          ) : (
            <Slider value={config.debounceMs} min={0} max={16} step={1} onChange={(v) => cfg('debounce', v)} format={(v) => `${v} ms`} />
          )}
        </Row>
        {config.features.liftOff !== false && (
          <Row title={t.liftOff} hint={t.liftOffHint}>
            <Segmented id="lod" value={config.liftOff} onChange={(v) => cfg('liftOff', v)} options={[{ value: 0, label: config.features.liftOffLabels?.[0] ?? t.low }, { value: 1, label: config.features.liftOffLabels?.[1] ?? t.high }]} />
          </Row>
        )}
        {config.features.motionSync && (
          <Row title={t.motionSync} hint={t.motionSyncHint}>
            <Toggle checked={!!config.motionSync} onChange={(v) => cfg('motionSync', v)} />
          </Row>
        )}
        <Row title={t.sleepTimer} hint={t.sleepHint}>
          {config.features.sleepRange ? (
            <div className="flex items-center gap-3">
              {config.features.sleepNever && <Toggle checked={config.sleepSeconds > 0} onChange={(on) => cfg('sleep', on ? 7 * 60 : 0)} />}
              {(!config.features.sleepNever || config.sleepSeconds > 0) && (
                <Slider
                  value={Math.max(config.features.sleepRange[0], Math.round(config.sleepSeconds / 60))}
                  min={config.features.sleepRange[0]}
                  max={config.features.sleepRange[1]}
                  step={1}
                  onChange={(m) => cfg('sleep', m * 60)}
                  format={(m) => `${m} ${t.min}`}
                />
              )}
            </div>
          ) : (
            <Segmented
              id="sleep"
              value={SLEEP_OPTIONS.includes(config.sleepSeconds) || config.sleepSeconds === 0 ? config.sleepSeconds : -1}
              onChange={(v) => cfg('sleep', v)}
              options={[...SLEEP_OPTIONS.map((s) => ({ value: s, label: `${s / 60}` })), ...(config.features.sleepNever ? [{ value: 0, label: t.never }] : [])]}
            />
          )}
        </Row>
      </Rows>

      <p className="text-[12px] text-fg-faint">
        {config.firmware && `${t.firmware} ${config.firmware} · `}
        {t.profile} {config.profile} · {config.features.readback ? t.savedOnDevice : t.rememberedNote}
      </p>
    </div>
  )
}

function Section({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <section>
      <div className="mb-3 flex items-baseline justify-between gap-4">
        <div className="label">{title}</div>
        {hint && <div className="text-[11.5px] text-fg-faint">{hint}</div>}
      </div>
      {children}
    </section>
  )
}

/** Full-width slider with a thicker track, for the main DPI value. */
function WideSlider({ value, min, max, step, onChange }: { value: number; min: number; max: number; step: number; onChange: (v: number) => void }) {
  const p = ((value - min) / (max - min)) * 100
  return (
    <div className="relative h-7">
      <div className="absolute top-1/2 h-1.5 w-full -translate-y-1/2 rounded-full bg-white/[0.08]" />
      <div className="absolute top-1/2 h-1.5 -translate-y-1/2 rounded-full bg-accent" style={{ width: `${p}%` }} />
      <div className="pointer-events-none absolute top-1/2 size-5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-white shadow-[0_0_0_5px_rgb(76_194_255/.18),0_1px_4px_rgb(0_0_0/.5)]" style={{ left: `${p}%` }} />
      <input type="range" min={min} max={max} step={step} value={value} onChange={(e) => onChange(Number(e.target.value))} className="absolute inset-0 w-full cursor-pointer opacity-0" />
    </div>
  )
}
