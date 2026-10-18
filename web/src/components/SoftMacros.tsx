import { useEffect, useState } from 'react'
import { Keyboard, MousePointerClick } from 'lucide-react'
import { modifierBit, usageOf } from '../hid'
import { Play, Plus, Trash2, Zap } from 'lucide-react'
import { send } from '../bridge'
import { t } from '../i18n'
import type { TimedEvent } from '../hid'
import { nextId, saveAll, type SoftMacro, type SoftMode } from '../softmacros'
import { EventList } from './EventList'
import { Row, Rows, Segmented } from './ui'

const newMacro = (macros: SoftMacro[]): SoftMacro => ({
  id: nextId(macros), name: `${t.macro} ${nextId(macros)}`, trigger: null, mode: 'once', repeat: 1, minHold: 30, gap: 0, events: [], kind: 'macro',
})

const rapidClick = (macros: SoftMacro[]): SoftMacro => ({
  id: nextId(macros), name: t.rapidClick, trigger: null, mode: 'hold', repeat: 1, minHold: 30, gap: 50, kind: 'macro',
  events: [{ delay: 0, event: { type: 'mouse', mask: 1 } }, { delay: 30, event: { type: 'mouse', mask: 0 } }],
})

/**
 * Macro list + editor. With `onSave` (used inside a button's Macro category) the primary action
 * becomes "Save and assign" and the chosen macro is bound to that button.
 */
export function MacroWorkbench({ macros, selected, onSelect, onSave, assignedId }: {
  macros: SoftMacro[]
  selected: number | null
  onSelect: (id: number | null) => void
  onSave?: (m: SoftMacro) => void
  assignedId?: number | null
}) {
  const current = macros.find((m) => m.id === selected) ?? null
  // New macros live only in the draft until saved, so nothing half-made ends up in the list.
  const [draft, setDraft] = useState<SoftMacro | null>(current)
  useEffect(() => {
    if (selected == null || macros.some((m) => m.id === selected)) setDraft(macros.find((m) => m.id === selected) ?? null)
  }, [selected, macros])

  const isNew = draft != null && !macros.some((m) => m.id === draft.id)
  const dirty = draft != null && (isNew || JSON.stringify(draft) !== JSON.stringify(current))
  const store = (m: SoftMacro) => saveAll(macros.some((x) => x.id === m.id) ? macros.map((x) => (x.id === m.id ? m : x)) : [...macros, m])
  const start = (m: SoftMacro) => {
    setDraft(m)
    onSelect(m.id)
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-1.5">
        {macros.map((m) => (
          <button
            key={m.id}
            onClick={() => onSelect(m.id)}
            className={`flex h-9 items-center gap-2 rounded-lg px-3 text-[13px] font-medium transition-colors ${selected === m.id ? 'bg-white/[0.08] text-fg ring-1 ring-inset ring-white/12' : 'text-fg-muted ring-1 ring-inset ring-line hover:bg-white/[0.04] hover:text-fg'}`}
          >
            <span className="max-w-40 truncate">{m.name || `${t.macro} ${m.id}`}</span>
            {assignedId === m.id && <span className="size-1.5 rounded-full bg-accent" />}
          </button>
        ))}
        <button onClick={() => start(newMacro(macros))} className="flex h-9 items-center gap-2 rounded-lg px-3 text-[13px] font-medium text-fg-faint ring-1 ring-inset ring-dashed ring-white/15 hover:text-fg">
          <Plus size={14} /> {t.newMacro}
        </button>
        <button onClick={() => start(rapidClick(macros))} className="flex h-9 items-center gap-2 rounded-lg px-3 text-[13px] font-medium text-fg-faint ring-1 ring-inset ring-dashed ring-white/15 hover:text-fg">
          <Zap size={14} /> {t.rapidClick}
        </button>
      </div>

      {!draft ? (
        <p className="text-[13px] leading-5 text-fg-faint">{t.softIntro}</p>
      ) : (
        <>
          <Rows>
            <Row title={t.nameLabel}>
              <input
                value={draft.name}
                onChange={(e) => setDraft({ ...draft, name: e.target.value })}
                className="h-8 w-52 rounded-md bg-white/[0.05] px-2.5 text-[13px] font-medium text-fg outline-none ring-1 ring-inset ring-white/10 focus:ring-accent/60"
              />
            </Row>
            <Row stack title={t.modeLabel} hint={draft.mode === 'once' ? t.modeOnceHint : draft.mode === 'hold' ? t.modeHoldHint : draft.mode === 'toggle' ? t.modeToggleHint : t.modeRepeatHint}>
              <Segmented
                id={`mode-${draft.id}`}
                value={draft.mode}
                onChange={(v: SoftMode) => setDraft({ ...draft, mode: v })}
                options={[
                  { value: 'once', label: t.playOnce },
                  { value: 'hold', label: t.whileHeld },
                  { value: 'toggle', label: t.toggle },
                  { value: 'repeat', label: t.repeatN },
                ]}
              />
            </Row>
            {draft.mode === 'repeat' && (
              <Row title={t.repeatCount}>
                <Num value={draft.repeat} min={1} max={999} suffix="×" onChange={(v) => setDraft({ ...draft, repeat: v })} />
              </Row>
            )}
            <Row title={t.minHold} hint={t.minHoldHint}>
              <Num value={draft.minHold} min={0} max={1000} suffix="ms" onChange={(v) => setDraft({ ...draft, minHold: v })} />
            </Row>
            <Row title={t.gapAfter} hint={t.gapAfterHint}>
              <Num value={draft.gap} min={0} max={60000} suffix="ms" onChange={(v) => setDraft({ ...draft, gap: v })} />
            </Row>
            <Row title={t.jitter} hint={t.jitterHint}>
              <Num value={draft.jitter ?? 0} min={0} max={500} suffix="± ms" onChange={(v) => setDraft({ ...draft, jitter: v })} />
            </Row>
          </Rows>
          <StepAdder onAdd={(steps) => setDraft({ ...draft, events: [...draft.events, ...steps.map((e, i) => (i === 0 && draft.events.length > 0 ? { ...e, delay: 50 } : e))] })} hold={draft.minHold || 30} />
          <EventList events={draft.events} onChange={(events) => setDraft({ ...draft, events })} />
          <div className="flex flex-wrap items-center gap-2">
            {onSave ? (
              <button
                onClick={() => onSave(draft)}
                disabled={draft.events.length === 0 || (!dirty && assignedId === draft.id)}
                className="h-9 whitespace-nowrap rounded-lg bg-accent px-4 text-[13px] font-semibold text-[#06121a] transition-opacity hover:opacity-90 disabled:opacity-30"
              >
                {assignedId === draft.id ? t.save : t.saveAndAssign}
              </button>
            ) : (
              <button onClick={() => store(draft)} disabled={!dirty} className="h-9 whitespace-nowrap rounded-lg bg-accent px-4 text-[13px] font-semibold text-[#06121a] transition-opacity hover:opacity-90 disabled:opacity-30">
                {t.save}
              </button>
            )}
            <button onClick={() => send('testMacro', { id: draft.id })} disabled={dirty || draft.events.length === 0} className="inline-flex h-9 items-center gap-1.5 rounded-lg px-3 text-[13px] font-medium text-fg-muted hover:text-fg disabled:opacity-30">
              <Play size={13} /> {t.test}
            </button>
            {!isNew && (
              <button
                onClick={() => {
                  saveAll(macros.filter((x) => x.id !== draft.id))
                  onSelect(null)
                }}
                className="inline-flex h-9 items-center gap-1.5 rounded-lg px-3 text-[13px] font-medium text-fg-faint hover:text-bad"
              >
                <Trash2 size={14} /> {t.delete}
              </button>
            )}
          </div>
          <p className="text-[11.5px] leading-[17px] text-fg-faint">{onSave ? '' : t.softAssignHint + ' '}{t.antiCheat}</p>
        </>
      )}
    </div>
  )
}

export function Num({ label, value, min, max, suffix, onChange }: { label?: string; value: number; min: number; max: number; suffix: string; onChange: (v: number) => void }) {
  return (
    <label className="flex items-center gap-2 text-[12.5px] text-fg-muted">
      {label}
      <input
        type="number"
        min={min}
        max={max}
        value={value}
        onChange={(e) => onChange(Math.max(min, Math.min(max, Number(e.target.value) || 0)))}
        className="h-8 w-20 rounded-md bg-white/[0.05] px-2 text-right text-[13px] tabular text-fg outline-none ring-1 ring-inset ring-white/10 focus:ring-accent/60"
      />
      <span className="w-10 text-fg-faint">{suffix}</span>
    </label>
  )
}

/** Add steps by hand: a click or a key press (down + up). Delays are edited in the list. */
function StepAdder({ onAdd, hold }: { onAdd: (steps: TimedEvent[]) => void; hold: number }) {
  const [listening, setListening] = useState(false)
  useEffect(() => {
    if (!listening) return
    const down = (e: KeyboardEvent) => {
      e.preventDefault()
      const bit = modifierBit(e.code)
      if (bit) onAdd([{ delay: 0, event: { type: 'mod', down: true, bit } }, { delay: hold, event: { type: 'mod', down: false, bit } }])
      else {
        const usage = usageOf(e.code)
        if (usage === undefined) return
        onAdd([{ delay: 0, event: { type: 'key', down: true, usage } }, { delay: hold, event: { type: 'key', down: false, usage } }])
      }
      setListening(false)
    }
    window.addEventListener('keydown', down, true)
    return () => window.removeEventListener('keydown', down, true)
  }, [listening, onAdd, hold])
  const click = (mask: number) => onAdd([{ delay: 0, event: { type: 'mouse', mask } }, { delay: hold, event: { type: 'mouse', mask: 0 } }])
  const cls = 'inline-flex h-9 items-center gap-2 rounded-lg px-3 text-[13px] font-medium ring-1 ring-inset transition-colors'
  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="mr-1 text-[12.5px] text-fg-muted">{t.addStep}</span>
      <button onClick={() => setListening(true)} className={`${cls} ${listening ? 'bg-accent/10 text-accent ring-accent/50' : 'bg-white/[0.04] text-fg ring-white/10 hover:bg-white/[0.08]'}`}>
        <Keyboard size={14} /> {listening ? t.pressKeys : t.keyLabel}
      </button>
      <button onClick={() => click(1)} className={`${cls} bg-white/[0.04] text-fg ring-white/10 hover:bg-white/[0.08]`}>
        <MousePointerClick size={14} /> {t.leftClick}
      </button>
      <button onClick={() => click(2)} className={`${cls} bg-white/[0.04] text-fg ring-white/10 hover:bg-white/[0.08]`}>
        <MousePointerClick size={14} /> {t.rightClick}
      </button>
    </div>
  )
}
