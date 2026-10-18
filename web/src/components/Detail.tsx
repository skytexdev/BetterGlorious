import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import { ArrowLeft, Check, FlaskConical, Gauge, Lightbulb, MousePointerClick, LayoutGrid, Undo2, History } from 'lucide-react'
import { send, sendCfg, type Device } from '../bridge'
import { activeLink, colorsFor, layoutFor, levelColor, levelOf, mapImageFor } from '../devices'
import { ButtonMap } from './ButtonMap'
import { buttonChanged, buttonLabel } from './Buttons'
import { percentFirst, t } from '../i18n'
import { Render } from './Render'
import { Performance } from './Performance'
import { Lighting } from './Lighting'
import { Buttons } from './Buttons'
import type { SoftMacro } from '../softmacros'
import { AutoProfile } from './AutoProfile'
import { status } from './Home'
import { AnimatedNumber, BatteryCell, Chip, KindIcon, LinkIcon, linkLabel, Segmented, Toggle } from './ui'

type Tab = 'overview' | 'performance' | 'lighting' | 'buttons'

export function Detail({ device: d, lowBattery, softMacros, foreground, onBack }: { device: Device; lowBattery: number; softMacros: SoftMacro[]; foreground: { exe: string; title: string }; onBack: () => void }) {
  const [tab, setTab] = useState<Tab>(((h) => (['overview', 'performance', 'lighting', 'buttons'].includes(h) ? h : 'overview') as Tab)(location.hash.split('/')[1]))
  const [button, setButton] = useState<number | null>(0x01)
  const layout = layoutFor(d)
  const mapImage = mapImageFor(d)
  const showMap = tab === 'buttons' && !!layout && !!mapImage && !!d.config
  const level = levelOf(d, lowBattery)
  const color = levelColor[level]
  const tabs: { id: Tab; label: string; icon: typeof Gauge; ready: boolean }[] = [
    { id: 'overview', label: t.overview, icon: LayoutGrid, ready: true },
    { id: 'performance', label: t.performance, icon: Gauge, ready: d.configurable },
    { id: 'lighting', label: t.lighting, icon: Lightbulb, ready: d.configurable },
    { id: 'buttons', label: t.buttons, icon: MousePointerClick, ready: d.configurable && (d.config?.features.buttons ?? false) },
  ]

  return (
    // Always split down the middle; the device picture scales to fit its half.
    <div className="grid h-full grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      {/* Left: the device, pinned. */}
      <div className="relative flex min-w-0 flex-col px-8 pt-2">
        <button onClick={onBack} className="inline-flex w-fit items-center gap-2 rounded-lg px-2 py-1.5 text-[13px] font-medium text-fg-muted transition-colors hover:bg-white/[0.05] hover:text-fg">
          <ArrowLeft size={15} strokeWidth={2} />
          {t.back}
        </button>
        <div className="mt-3 px-2">
            <div className="flex items-center gap-2 text-fg-faint">
              <KindIcon kind={d.kind} size={14} />
              <span className="label">{d.family}</span>
            </div>
            <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-2">
              <h1 className="text-[24px] font-semibold leading-8 tracking-[-0.02em]">{d.name}</h1>
            </div>

            {d.config && (
              <div className="mt-4 flex items-center gap-3">
                <span className="label">{t.profile}</span>
                <Segmented id={`profile-${d.key}`} value={d.config.profile} onChange={(v) => sendCfg(d.key, 'profile', v)} options={[1, 2, 3].map((p) => ({ value: p, label: String(p) }))} />
                <div className="ml-auto flex items-center gap-1">
                  <button disabled={!d.canUndo} onClick={() => send('undo', { key: d.key })} className="inline-flex h-8 items-center gap-1.5 whitespace-nowrap rounded-lg px-2.5 text-[12.5px] font-medium text-fg-muted transition-colors hover:bg-white/[0.05] hover:text-fg disabled:pointer-events-none disabled:opacity-30">
                    <Undo2 size={14} /> {t.undo}
                  </button>
                  <button disabled={!d.canRestore} onClick={() => confirm(t.restoreConfirm) && send('restore', { key: d.key })} className="inline-flex h-8 items-center gap-1.5 whitespace-nowrap rounded-lg px-2.5 text-[12.5px] font-medium text-fg-muted transition-colors hover:bg-white/[0.05] hover:text-fg disabled:pointer-events-none disabled:opacity-30">
                    <History size={14} /> {t.restore}
                  </button>
                </div>
              </div>
            )}

        </div>
        <div className="grid flex-1 place-items-center pb-8">
          {showMap ? (
            <motion.div key="map" initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: 0.3 }} className="w-full max-w-[760px] justify-self-stretch">
              <ButtonMap
                layout={layout!}
                image={mapImage!}
                selected={button}
                onSelect={setButton}
                actionLabel={(id) => buttonLabel(id, d.config!, d, softMacros)}
                changed={(id) => buttonChanged(id, d.config!)}
              />
              {/* Buttons the map can't point at (scroll up/down) */}
              <div className="mt-2 flex justify-center gap-2">
                {[0x10, 0x11].filter((id) => d.config!.buttons[id]).map((id) => (
                  <button
                    key={id}
                    onClick={() => setButton(id)}
                    className={`flex flex-col items-start rounded-lg px-3 py-1.5 text-left ring-1 ring-inset transition-colors ${button === id ? 'bg-accent/[0.14] ring-accent' : 'bg-white/[0.03] ring-white/10 hover:bg-white/[0.06]'}`}
                  >
                    <span className="text-[12px] font-semibold">{id === 0x10 ? t.btnWheelUp : t.btnWheelDown}</span>
                    <span className={`text-[10.5px] ${buttonChanged(id, d.config!) ? 'text-accent' : 'text-fg-faint'}`}>{buttonLabel(id, d.config!, d, softMacros)}</span>
                  </button>
                ))}
              </div>
            </motion.div>
          ) : (
            <motion.div key="render" className="flex flex-col items-center" initial={{ opacity: 0, scale: 0.97 }} animate={{ opacity: 1, scale: 1 }} transition={{ duration: 0.32, ease: [0.2, 0, 0, 1] }}>
              <Render device={d} size={320} />
              {d.hasBattery && (
                // Centered under the device: the percentage (shimmering while charging), then the cells and status.
                <div className="-mt-3 flex flex-col items-center gap-1.5">
                  <div className={`flex items-baseline tabular ${d.charge === 'charging' ? 'charging-text' : ''}`} style={{ color }}>
                    {percentFirst && d.percent >= 0 && <span className="mr-0.5 text-[16px] font-medium opacity-60">%</span>}
                    {d.percent >= 0 ? <AnimatedNumber value={d.percent} className="text-[30px] font-semibold leading-none tracking-[-0.03em]" /> : <span className="text-[30px] font-semibold leading-none">–</span>}
                    {!percentFirst && d.percent >= 0 && <span className="ml-0.5 text-[16px] font-medium opacity-60">%</span>}
                  </div>
                  <BatteryCell percent={Math.max(0, d.percent)} color={color} charging={d.charge === 'charging'} cells={10} height={6} width={120} />
                  <div className="text-center text-[12px] leading-4">
                    <span className="font-medium" style={{ color: level === 'charging' || level === 'full' ? color : 'var(--color-fg-muted)' }}>{status(d)}</span>
                    {d.time && <span className="text-fg-faint tabular"> · {t.updated} {d.time}</span>}
                  </div>
                </div>
              )}
            </motion.div>
          )}
        </div>
      </div>

      {/* Right: header, tabs, content. */}
      <div className="flex min-h-0 flex-col border-l border-line bg-surface/40">
        <div className="px-8 pt-1">
          <div className="flex gap-1 overflow-x-auto border-b border-line [scrollbar-width:none]">
            {tabs.map((x) => {
              const active = tab === x.id
              return (
                <button
                  key={x.id}
                  disabled={!x.ready}
                  title={x.ready ? undefined : t.soon}
                  onClick={() => setTab(x.id)}
                  className={`relative flex h-10 shrink-0 items-center gap-1.5 whitespace-nowrap px-2.5 text-[13px] font-medium transition-colors ${active ? 'text-fg' : x.ready ? 'text-fg-muted hover:text-fg' : 'cursor-default text-fg-faint'}`}
                >
                  <x.icon size={15} strokeWidth={1.75} />
                  {x.label}
                  {active && <motion.span layoutId="tab-underline" className="absolute inset-x-2 -bottom-px h-[2px] rounded-full bg-accent" />}
                </button>
              )
            })}
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-8 py-5 [scrollbar-gutter:stable]">
          <AnimatePresence mode="wait">
            <motion.div key={tab} initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }} transition={{ duration: 0.2, ease: [0.2, 0, 0, 1] }}>
              {tab !== 'overview' && (d.config ? (tab === 'performance' ? <Performance device={d} config={d.config} /> : tab === 'lighting' ? <Lighting device={d} config={d.config} /> : <Buttons device={d} config={d.config} softMacros={softMacros} open={button} onOpen={setButton} single={showMap} />) : <p className="text-[13px] text-fg-faint">{t.notReadable}</p>)}
              {tab === 'overview' && <>
              {(
                <section>
                  <div className="label">{t.general}</div>
                  <div className="mt-3 divide-y divide-line overflow-hidden rounded-xl bg-white/[0.03] ring-1 ring-inset ring-line">
                    {activeLink(d) && (
                      <div className="flex items-center justify-between px-4 py-3">
                        <span className="text-[14px]">{t.connection}</span>
                        <span className="inline-flex items-center gap-2 text-[13px] text-fg-muted">
                          <LinkIcon link={activeLink(d)!} size={14} />
                          {linkLabel(activeLink(d)!)}
                        </span>
                      </div>
                    )}
                    <div className="flex items-center justify-between px-4 py-3">
                      <span className="text-[14px]">{t.batteryReading}</span>
                      {d.verified ? (
                        <Chip tone="ok">
                          <Check size={12} strokeWidth={2.5} />
                          {t.verified}
                        </Chip>
                      ) : (
                        <Chip tone="warn" title={t.experimentalBody}>
                          <FlaskConical size={12} strokeWidth={2} />
                          {t.experimental}
                        </Chip>
                      )}
                    </div>
                    <div className="flex items-center justify-between px-4 py-3">
                      <span className="text-[14px]">{t.showInTray}</span>
                      <Toggle checked={d.inTray} onChange={(v) => send('tray', { key: d.key, value: v })} />
                    </div>
                    {colorsFor(d).length > 1 && (
                      <div className="flex items-center justify-between px-4 py-3">
                        <span className="text-[14px]">{t.color}</span>
                        <div className="flex gap-2">
                          {colorsFor(d).map((c) => (
                            <button
                              key={c.id}
                              title={c.label}
                              onClick={() => send('color', { key: d.key, value: c.id })}
                              className={`size-7 rounded-full transition-shadow duration-150 ${(d.color === c.id || (!colorsFor(d).some((x) => x.id === d.color) && c === colorsFor(d)[0])) ? 'ring-2 ring-accent ring-offset-2 ring-offset-[#141518]' : 'ring-1 ring-white/15 hover:ring-white/30'}`}
                              style={{ background: `linear-gradient(135deg, ${c.hex}, color-mix(in srgb, ${c.hex} 70%, black))` }}
                            />
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                </section>
              )}

              {d.configurable && d.config && <AutoProfile device={d} foreground={foreground} />}
              </>}
            </motion.div>
          </AnimatePresence>
        </div>
      </div>
    </div>
  )
}
