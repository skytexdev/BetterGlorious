import { useEffect, useRef, useState } from 'react'
import { sendCfg, type Device, type MouseConfig } from '../bridge'
import { t, type Key } from '../i18n'
import { Row, Rows, Segmented, Slider, Toggle } from './ui'

/**
 * Effects in Glorious CORE's order, with the firmware's effect number. Which effects take a color and
 * which have a speed follows CORE (PresetLightingEffectData_Mouse_V1): Breathing and Rave use fixed
 * palettes, Tail has no color, Static has no speed. Speed is CORE's 1-100 slider value; the app
 * converts it per device family (Rave/Wave use a different firmware scale on the Model O family).
 */
type Effect = { id: number; key: Key; preview: string; color?: boolean; speed?: boolean; chromatic?: boolean }
const RAINBOW = 'linear-gradient(90deg,#ff0000,#ffa500,#ffff00,#00ff00,#007fff,#0000ff,#8b00ff)'
const MOUSE_EFFECTS: Effect[] = [
  { id: 0, key: 'fxOff', preview: 'linear-gradient(135deg,#1a1b1f,#121316)' },
  { id: 1, key: 'fxGlorious', preview: RAINBOW, speed: true },
  { id: 2, key: 'fxSeamless', preview: 'radial-gradient(circle at 30% 50%,#ff4d6d,transparent 60%),radial-gradient(circle at 70% 50%,#4dc3ff,transparent 60%),#15161a', speed: true },
  { id: 3, key: 'fxBreathing', preview: 'radial-gradient(circle at 25% 50%,#ff0000,transparent 45%),radial-gradient(circle at 50% 50%,#00ff00,transparent 45%),radial-gradient(circle at 75% 50%,#0000ff,transparent 45%),#15161a', speed: true },
  { id: 4, key: 'fxStatic', preview: 'var(--fx-color)', color: true },
  { id: 5, key: 'fxBreathingSingle', preview: 'radial-gradient(circle,var(--fx-color) 0%,transparent 72%),#15161a', color: true, speed: true },
  { id: 6, key: 'fxTail', preview: 'linear-gradient(90deg,transparent,#ff3b3b,#3bd8ff,transparent)', speed: true },
  { id: 7, key: 'fxRave', preview: 'linear-gradient(90deg,#ff0000 0 50%,#0000ff 50% 100%)', speed: true },
  { id: 8, key: 'fxWave', preview: 'linear-gradient(90deg,#3bd8ff,#6b3bff,#ff3bd2,#3bd8ff)', speed: true },
]

const PRESETS = ['#ff2d55', '#ff9f0a', '#ffd60a', '#30d158', '#4cc2ff', '#5e5ce6', '#bf5af2', '#ffffff']

/** Families that list their own effects (keyboards): each may switch between its own colors and a custom one. */
function effectsFor(config: MouseConfig): Effect[] {
  if (!config.features.effects) return MOUSE_EFFECTS
  return config.features.effects.map((e) => ({
    id: e.id,
    key: e.key as Key,
    chromatic: e.color,
    color: e.color,
    speed: e.id !== 0 && e.id !== 7,
    preview: e.id === 0 ? MOUSE_EFFECTS[0].preview : RAINBOW,
  }))
}

export function Lighting({ device, config }: { device: Device; config: MouseConfig }) {
  const effects = effectsFor(config)
  // Everything comes from the device state (pending changes are already applied by the bridge).
  // Only the color dialog keeps a local draft while it is open, and writes once when it closes.
  const effect = config.effect
  const [draftColor, setDraftColor] = useState<string | null>(null)
  const color = draftColor ?? config.effectColor
  const custom = !!config.effectCustom
  const fx = effects.find((e) => e.id === effect) ?? effects[1]
  const showColor = fx.color && (!fx.chromatic || custom)
  const bright = device.brightness ?? 100
  const picker = useRef<HTMLInputElement>(null)

  const push = (next: { effect?: number; color?: string; speed?: number; custom?: boolean }) => {
    sendCfg(device.key, 'effect', {
      effect: next.effect ?? effect,
      color: next.color ?? color,
      speed: next.speed ?? config.effectSpeed,
      custom: next.custom ?? custom,
    })
  }
  const pushRef = useRef(push)
  pushRef.current = push

  // The native "change" event fires once, when the color dialog closes.
  useEffect(() => {
    const el = picker.current
    if (!el) return
    const done = () => {
      pushRef.current({ color: el.value })
      setDraftColor(null)
    }
    el.addEventListener('change', done)
    return () => el.removeEventListener('change', done)
  }, [showColor])

  return (
    <div className="flex flex-col gap-6" style={{ ['--fx-color' as string]: color }}>
      <section>
        <div className="label mb-2.5">{t.effect}</div>
        <div className="grid grid-cols-3 gap-2">
          {effects.map((e) => {
            const active = e.id === effect
            return (
              <button
                key={e.id}
                onClick={() => {
                  if (!active) push({ effect: e.id })
                }}
                className={`group flex flex-col gap-2 rounded-xl p-2 text-left transition-colors ${active ? 'bg-white/[0.08] ring-1 ring-inset ring-accent/60' : 'bg-white/[0.03] ring-1 ring-inset ring-line hover:bg-white/[0.06]'}`}
              >
                <div className="h-7 w-full rounded-lg ring-1 ring-inset ring-white/[0.06]" style={{ background: e.preview, backgroundSize: e.id === 8 ? '200% 100%' : undefined }} />
                <span className={`truncate px-0.5 text-[12.5px] font-medium ${active ? 'text-fg' : 'text-fg-muted group-hover:text-fg'}`}>{t[e.key]}</span>
              </button>
            )
          })}
        </div>
      </section>

      {effect !== 0 && (
        <Rows title={t.effectSettings}>
          {fx.chromatic && (
            <Row title={t.colorMode}>
              <Segmented id="cmode" value={custom ? 1 : 0} onChange={(v) => push({ custom: v === 1 })} options={[{ value: 0, label: t.colorModeEffect }, { value: 1, label: t.colorModeCustom }]} />
            </Row>
          )}
          {showColor && (
            <Row title={t.color}>
              <div className="flex items-center gap-2">
                {PRESETS.map((c) => (
                  <button
                    key={c}
                    onClick={() => push({ color: c })}
                    className={`size-6 rounded-full transition-transform hover:scale-110 ${color.toLowerCase() === c ? 'ring-2 ring-accent ring-offset-2 ring-offset-[#141518]' : 'ring-1 ring-white/10'}`}
                    style={{ background: c }}
                  />
                ))}
                <label className="relative ml-1 size-6 cursor-pointer overflow-hidden rounded-full ring-1 ring-white/25" style={{ background: 'conic-gradient(red, yellow, lime, cyan, blue, magenta, red)' }} title={color.toUpperCase()}>
                  <input ref={picker} type="color" value={color} onChange={(e) => setDraftColor(e.target.value)} className="absolute inset-0 cursor-pointer opacity-0" />
                </label>
              </div>
            </Row>
          )}
          {fx.speed && (
            <Row title={t.speed}>
              {/* CORE's slider: 1, 5, 10 ... 100 */}
              <Slider value={config.effectSpeed <= 1 ? 0 : config.effectSpeed} min={0} max={100} step={5} onChange={(v) => push({ speed: Math.max(1, v) })} format={(v) => `${Math.max(1, v)}`} />
            </Row>
          )}
          {config.features.separateBrightness ? (
            <>
              <Row title={t.separateWireless}>
                <Toggle checked={device.brightnessWireless != null} onChange={(on) => sendCfg(device.key, 'brightnessWireless', on ? bright : -1)} />
              </Row>
              <Row title={device.brightnessWireless != null ? t.wiredBrightness : t.brightness} hint={device.brightnessWireless != null ? undefined : t.brightnessHint}>
                <Slider value={bright} min={0} max={100} step={5} onChange={(v) => sendCfg(device.key, 'brightness', v)} format={(v) => `${v}%`} />
              </Row>
              {device.brightnessWireless != null && (
                <Row title={t.wirelessBrightness}>
                  <Slider value={device.brightnessWireless} min={0} max={100} step={5} onChange={(v) => sendCfg(device.key, 'brightnessWireless', v)} format={(v) => `${v}%`} />
                </Row>
              )}
            </>
          ) : (
            <Row title={t.brightness} hint={t.brightnessHint}>
              <Slider value={bright} min={0} max={100} step={5} onChange={(v) => sendCfg(device.key, 'brightness', v)} format={(v) => `${v}%`} />
            </Row>
          )}
        </Rows>
      )}
    </div>
  )
}
