import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useState, type ReactNode } from 'react'
import { ChevronDown, FolderOpen, Keyboard, RotateCcw } from 'lucide-react'
import { send, sendCfg, useMessage, type Device, type MouseConfig } from '../bridge'
import { t } from '../i18n'
import { decodeAction, describeAction, dpiLabels, encodeAction, keyLabel, mediaLabels, modifierBit, mouseLabels, usageOf, type Action } from '../hid'
import { freeTrigger, rapidTargetOf, saveAll, slotOfAction, triggerAction, type SoftMacro } from '../softmacros'
import { MacroWorkbench } from './SoftMacros'

// Physical buttons, in the order you'd point at them. Defaults restore the factory mapping.
const PHYSICAL: { id: number; name: keyof typeof t; def: Action }[] = [
  { id: 0x01, name: 'btnLeft', def: { kind: 'mouse', code: 1 } },
  { id: 0x02, name: 'btnRight', def: { kind: 'mouse', code: 2 } },
  { id: 0x03, name: 'btnMiddle', def: { kind: 'mouse', code: 3 } },
  { id: 0x05, name: 'btnForward', def: { kind: 'mouse', code: 5 } },
  { id: 0x04, name: 'btnBack', def: { kind: 'mouse', code: 4 } },
  { id: 0x14, name: 'btnDpi', def: { kind: 'dpi', code: 6 } },
  { id: 0x10, name: 'btnWheelUp', def: { kind: 'mouse', code: 0x10 } },
  { id: 0x11, name: 'btnWheelDown', def: { kind: 'mouse', code: 0x11 } },
]

type Category = 'mouse' | 'key' | 'macro' | 'media' | 'dpi' | 'other'

export function Buttons({ device, config, softMacros, open: openProp, onOpen, single }: { device: Device; config: MouseConfig; softMacros: SoftMacro[]; open?: number | null; onOpen?: (id: number | null) => void; single?: boolean }) {
  const [openState, setOpenState] = useState<number | null>(null)
  const open = openProp !== undefined ? openProp : openState
  const setOpen = onOpen ?? setOpenState
  const owned = (id: number) => softMacros.find((m) => m.owner?.device === device.key && m.owner.button === id)
  // Any onboard action frees the rapid-fire macro this button owned.
  const assign = (id: number, a: Action) => {
    if (owned(id)) saveAll(softMacros.filter((m) => !(m.owner?.device === device.key && m.owner.button === id)))
    sendCfg(device.key, 'button', { id, action: encodeAction(a, id) })
  }
  const assignRaw = (id: number, bytes: number[]) => sendCfg(device.key, 'button', { id, action: bytes })
  const softLabel = (bytes: number[]) => softLabelOf(bytes, softMacros)
  const macroName = (id: number) => `${t.macro} ${id}`

  if (single) {
    const p = PHYSICAL.find((x) => x.id === open && config.buttons[x.id]) ?? PHYSICAL.find((x) => config.buttons[x.id])
    if (!p) return null
    const action = decodeAction(config.buttons[p.id])
    const isDefault = JSON.stringify(encodeAction(action, p.id)) === JSON.stringify(encodeAction(p.def, p.id))
    return (
      <div key={p.id} className="flex flex-col gap-4">
        <div className="flex items-end justify-between gap-4">
          <div>
            <div className="label">{t.selectedButton}</div>
            <div className="mt-1 text-[20px] font-semibold tracking-[-0.01em]">{t[p.name] as string}</div>
          </div>
          <span className={`mb-0.5 rounded-lg px-2.5 py-1 text-[12.5px] font-medium ${isDefault ? 'bg-white/[0.05] text-fg-muted' : 'bg-accent/10 text-accent'}`}>{buttonLabel(p.id, config, device, softMacros)}</span>
        </div>
        <div className="overflow-hidden rounded-xl bg-white/[0.025] ring-1 ring-inset ring-line">
          <Editor
            device={device}
            config={config}
            buttonId={p.id}
            current={action}
            softMacros={softMacros}
            owned={owned(p.id)}
            isSoft={slotOfAction(config.buttons[p.id]) != null}
            onAssignRaw={(bytes) => assignRaw(p.id, bytes)}
            onAssign={(a) => assign(p.id, a)}
            onReset={isDefault ? undefined : () => assign(p.id, p.def)}
          />
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-2">
      <p className="mb-3 text-[12.5px] text-fg-faint">{t.buttonsHint}</p>
      {PHYSICAL.filter((p) => config.buttons[p.id]).map((p) => {
        const action = decodeAction(config.buttons[p.id])
        const isOpen = open === p.id
        const isDefault = JSON.stringify(encodeAction(action, p.id)) === JSON.stringify(encodeAction(p.def, p.id))
        return (
          <div key={p.id} className={`overflow-hidden rounded-xl ring-1 ring-inset transition-colors ${isOpen ? 'bg-white/[0.05] ring-white/12' : 'bg-white/[0.025] ring-line hover:bg-white/[0.04]'}`}>
            <button onClick={() => setOpen(isOpen ? null : p.id)} className="flex w-full items-center justify-between gap-4 px-4 py-3 text-left">
              <span className="text-[14px] font-medium">{t[p.name] as string}</span>
              <span className="flex items-center gap-3">
                <span className={`rounded-lg px-2.5 py-1 text-[12.5px] font-medium ${isDefault ? 'text-fg-muted' : 'bg-accent/10 text-accent'}`}>
                  {softLabel(config.buttons[p.id]) ?? (action.kind === 'launch' && device.launch[String(p.id)] ? fileName(device.launch[String(p.id)]) : describeAction(action, macroName))}
                </span>
                <ChevronDown size={15} className={`text-fg-faint transition-transform ${isOpen ? 'rotate-180' : ''}`} />
              </span>
            </button>
            <AnimatePresence initial={false}>
              {isOpen && (
                <motion.div initial={{ height: 0, opacity: 0 }} animate={{ height: 'auto', opacity: 1 }} exit={{ height: 0, opacity: 0 }} transition={{ duration: 0.2, ease: [0.2, 0, 0, 1] }}>
                  <Editor
                    device={device}
                    config={config}
                    buttonId={p.id}
                    current={action}
                    softMacros={softMacros}
                    owned={owned(p.id)}
                    isSoft={slotOfAction(config.buttons[p.id]) != null}
                    onAssignRaw={(bytes) => assignRaw(p.id, bytes)}
                    onAssign={(a) => assign(p.id, a)}
                    onReset={isDefault ? undefined : () => assign(p.id, p.def)}
                  />
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        )
      })}
    </div>
  )
}

/** Short description of what a button does, for the list and the button map. */
export function buttonLabel(id: number, config: MouseConfig, device: Device, softMacros: SoftMacro[]): string {
  const bytes = config.buttons[id]
  if (!bytes) return ''
  const soft = softLabelOf(bytes, softMacros)
  if (soft) return soft
  const action = decodeAction(bytes)
  if (action.kind === 'launch' && device.launch[String(id)]) return fileName(device.launch[String(id)])
  return describeAction(action, (m) => `${t.macro} ${m}`)
}

export function buttonChanged(id: number, config: MouseConfig): boolean {
  const p = PHYSICAL.find((x) => x.id === id)
  const bytes = config.buttons[id]
  if (!p || !bytes) return false
  return JSON.stringify(encodeAction(decodeAction(bytes), id)) !== JSON.stringify(encodeAction(p.def, id))
}

function softLabelOf(bytes: number[], softMacros: SoftMacro[]): string | null {
  const slot = slotOfAction(bytes)
  const m = slot == null ? undefined : softMacros.find((x) => x.trigger === slot)
  if (!m) return null
  if (m.kind === 'rapid') {
    const tg = rapidTargetOf(m)
    const what = tg?.type === 'mouse' ? (tg.bit === 2 ? t.rightClick : t.leftClick) : tg ? keyLabel(tg.usage) : ''
    return `${t.rapidFire} · ${what} · ${m.gap} ms`
  }
  return `${t.softMacro} · ${m.name}`
}

function Editor({ device, config, buttonId, current, softMacros, owned, isSoft, onAssign, onAssignRaw, onReset }: { device: Device; config: MouseConfig; buttonId: number; current: Action; softMacros: SoftMacro[]; owned?: SoftMacro; isSoft: boolean; onAssign: (a: Action) => void; onAssignRaw: (bytes: number[]) => void; onReset?: () => void }) {
  const initial: Category = owned || isSoft || current.kind === 'macro' ? 'macro' : current.kind === 'launch' ? 'other' : current.kind === 'sniper' ? 'dpi' : current.kind === 'profile' || current.kind === 'battery' || current.kind === 'disabled' || current.kind === 'unknown' ? 'other' : current.kind
  const [cat, setCat] = useState<Category>(initial)
  const cats: { id: Category; label: string }[] = [
    { id: 'mouse', label: t.catMouse },
    { id: 'key', label: t.catKey },
    { id: 'macro', label: t.macro },
    { id: 'media', label: t.catMedia },
    { id: 'dpi', label: 'DPI' },
    { id: 'other', label: t.catOther },
  ]
  const same = (a: Action) => JSON.stringify(encodeAction(a, buttonId)) === JSON.stringify(encodeAction(current, buttonId))

  return (
    <div className="border-t border-line px-4 pb-4 pt-3 first:border-t-0">
      <div className="mb-3 flex flex-wrap gap-1">
        {cats.map((c) => (
          <button key={c.id} onClick={() => setCat(c.id)} className={`h-7 rounded-lg px-2.5 text-[12px] font-medium transition-colors ${cat === c.id ? 'bg-white/[0.1] text-fg' : 'text-fg-muted hover:text-fg'}`}>
            {c.label}
          </button>
        ))}
        {onReset && (
          <button onClick={onReset} className="ml-auto inline-flex h-7 items-center gap-1.5 rounded-lg px-2.5 text-[12px] font-medium text-fg-faint hover:text-fg">
            <RotateCcw size={12} /> {t.resetDefault}
          </button>
        )}
      </div>

      {cat === 'mouse' && <Options items={Object.entries(mouseLabels).map(([code, label]) => ({ label, action: { kind: 'mouse', code: Number(code) } as Action }))} same={same} onPick={onAssign} />}
      {cat === 'dpi' && (
        <>
          <Options items={Object.entries(dpiLabels).map(([code, label]) => ({ label, action: { kind: 'dpi', code: Number(code) } as Action }))} same={same} onPick={onAssign} />
          <SniperRow current={current} onAssign={onAssign} />
        </>
      )}
      {cat === 'key' && (
        <>
          <KeyCapture current={current} onAssign={onAssign} />
          <p className="mt-2.5 text-[11.5px] leading-[17px] text-fg-faint">{t.keyHoldHint}</p>
        </>
      )}
      {cat === 'media' && <Options items={Object.entries(mediaLabels).map(([u, label]) => ({ label, action: { kind: 'media', usage: Number(u) } as Action }))} same={same} onPick={onAssign} />}
      {cat === 'macro' && <ButtonMacro softMacros={softMacros} buttonBytes={config.buttons[buttonId]} onAssignRaw={onAssignRaw} />}
      {cat === 'other' && (
        <div className="flex flex-col gap-4">
          <Options
            items={[
              { label: t.profileNext, action: { kind: 'profile', up: true } },
              { label: t.profilePrev, action: { kind: 'profile', up: false } },
              { label: t.batteryIndicator, action: { kind: 'battery' } },
              { label: t.disabled, action: { kind: 'disabled' } },
            ]}
            same={same}
            onPick={onAssign}
          />
          <div>
            <div className="label mb-2">{t.catLaunch}</div>
            <LaunchRow device={device} buttonId={buttonId} />
          </div>
        </div>
      )}
    </div>
  )
}

function Options({ items, same, onPick }: { items: { label: string; action: Action }[]; same: (a: Action) => boolean; onPick: (a: Action) => void }) {
  return (
    <div className="grid grid-cols-2 gap-1.5">
      {items.map((i) => (
        <Pill key={i.label} active={same(i.action)} onClick={() => onPick(i.action)}>
          {i.label}
        </Pill>
      ))}
    </div>
  )
}

function Pill({ active, onClick, children }: { active?: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button onClick={onClick} className={`h-9 rounded-lg px-3 text-left text-[13px] font-medium transition-colors ${active ? 'bg-accent/15 text-accent ring-1 ring-inset ring-accent/40' : 'bg-white/[0.04] text-fg-muted hover:bg-white/[0.07] hover:text-fg'}`}>
      {children}
    </button>
  )
}

function SniperRow({ current, onAssign }: { current: Action; onAssign: (a: Action) => void }) {
  const [dpi, setDpi] = useState(current.kind === 'sniper' ? current.dpi : 400)
  return (
    <div className="mt-3 flex items-center gap-3 rounded-lg bg-white/[0.03] px-3 py-2">
      <span className="text-[13px] text-fg-muted">{t.sniper}</span>
      <input type="number" min={100} max={19000} step={50} value={dpi} onChange={(e) => setDpi(Number(e.target.value))} className="h-8 w-24 rounded-md bg-white/[0.06] px-2 text-[13px] tabular text-fg outline-none ring-1 ring-inset ring-white/10 focus:ring-accent/60" />
      <span className="text-[12px] text-fg-faint">DPI</span>
      <button onClick={() => onAssign({ kind: 'sniper', dpi: Math.max(100, Math.min(19000, dpi)) })} className="ml-auto h-8 rounded-md bg-accent/15 px-3 text-[12.5px] font-medium text-accent hover:bg-accent/25">
        {t.assign}
      </button>
    </div>
  )
}

/** Press a key combination; modifiers held at the time are included. */
function KeyCapture({ current, onAssign }: { current: Action; onAssign: (a: Action) => void }) {
  const [listening, setListening] = useState(false)
  const [preview, setPreview] = useState<string>(current.kind === 'key' ? describeAction(current) : '')
  useEffect(() => {
    if (!listening) return
    let mods = 0
    const down = (e: KeyboardEvent) => {
      e.preventDefault()
      const bit = modifierBit(e.code)
      if (bit) {
        mods |= bit
        setPreview(describeAction({ kind: 'key', mods, usage: 0 }).replace(/ \+ 0x0$/, ' + …'))
        return
      }
      const usage = usageOf(e.code)
      if (usage === undefined) return
      // Actions only distinguish left/right-agnostic Ctrl/Shift/Alt/Win (low nibble).
      const m = ((mods >> 4) | mods) & 0x0f
      const a: Action = { kind: 'key', mods: m, usage }
      setPreview(describeAction(a))
      setListening(false)
      onAssign(a)
    }
    const up = (e: KeyboardEvent) => {
      const bit = modifierBit(e.code)
      if (bit) mods &= ~bit
    }
    window.addEventListener('keydown', down, true)
    window.addEventListener('keyup', up, true)
    return () => {
      window.removeEventListener('keydown', down, true)
      window.removeEventListener('keyup', up, true)
    }
  }, [listening, onAssign])
  return (
    <button
      onClick={() => setListening(true)}
      className={`flex h-14 w-full items-center justify-center gap-3 rounded-xl text-[14px] font-medium transition-colors ${listening ? 'bg-accent/10 text-accent ring-1 ring-inset ring-accent/50' : 'bg-white/[0.04] text-fg-muted ring-1 ring-inset ring-line hover:text-fg'}`}
    >
      <Keyboard size={17} strokeWidth={1.75} />
      {listening ? t.pressKeys : preview || t.clickToRecordKey}
    </button>
  )
}

function LaunchRow({ device, buttonId }: { device: Device; buttonId: number }) {
  const [target, setTarget] = useState(device.launch[String(buttonId)] ?? '')
  useMessage('picked', (d: { button: number; path: string }) => {
    if (d.button === buttonId) setTarget(d.path)
  })
  return (
    <div className="flex flex-col gap-2">
      <div className="flex gap-2">
        <input
          value={target}
          onChange={(e) => setTarget(e.target.value)}
          placeholder={t.launchPlaceholder}
          className="h-9 flex-1 rounded-lg bg-white/[0.05] px-3 text-[13px] text-fg outline-none ring-1 ring-inset ring-white/10 placeholder:text-fg-faint focus:ring-accent/60"
        />
        <button onClick={() => send('pickProgram', { button: buttonId })} title={t.browse} className="grid size-9 place-items-center rounded-lg bg-white/[0.05] text-fg-muted ring-1 ring-inset ring-white/10 hover:text-fg">
          <FolderOpen size={15} />
        </button>
        <button onClick={() => send('launch', { key: device.key, button: buttonId, target })} disabled={!target} className="h-9 rounded-lg bg-accent/15 px-3 text-[12.5px] font-medium text-accent hover:bg-accent/25 disabled:opacity-40">
          {t.assign}
        </button>
      </div>
      <p className="text-[12px] text-fg-faint">{t.launchHint}</p>
    </div>
  )
}

function fileName(p: string) {
  return p.split(/[\\/]/).pop() ?? p
}

/** A button's Macro category: pick, record or edit a macro right here, then save and assign it. */
function ButtonMacro({ softMacros, buttonBytes, onAssignRaw }: { softMacros: SoftMacro[]; buttonBytes: number[]; onAssignRaw: (bytes: number[]) => void }) {
  const slot = slotOfAction(buttonBytes)
  const assigned = slot == null ? undefined : softMacros.find((m) => m.trigger === slot)
  const [selected, setSelected] = useState<number | null>(assigned?.id ?? softMacros[0]?.id ?? null)
  const onSave = (m: SoftMacro) => {
    // This button keeps its trigger key; otherwise reuse the macro's, otherwise take a free one.
    const others = softMacros.filter((x) => x.id !== m.id)
    const target = slot ?? m.trigger ?? freeTrigger(others, null)
    if (target == null) return
    const list = [...others.map((x) => (x.trigger === target ? { ...x, trigger: null } : x)), { ...m, trigger: target }]
    saveAll(list)
    onAssignRaw(triggerAction(target))
  }
  return <MacroWorkbench macros={softMacros} selected={selected} onSelect={setSelected} onSave={onSave} assignedId={assigned?.id ?? null} />
}
