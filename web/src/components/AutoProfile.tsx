import { useState } from 'react'
import { AppWindow, Plus, X } from 'lucide-react'
import { send, type Device } from '../bridge'
import { AppPicker, type RunningApp } from './AppPicker'
import { t } from '../i18n'
import { Segmented, Toggle } from './ui'

export interface AutoRule { exe: string; title?: string; profile: number; label?: string; icon?: string | null }
export interface AutoProfileState { enabled: boolean; fallback: number; rules: AutoRule[] }

const EMPTY: AutoProfileState = { enabled: false, fallback: 1, rules: [] }

/** "Minecraft → profile 2, everything else → profile 1": rules that switch the onboard profile by foreground app. */
export function AutoProfile({ device, foreground }: { device: Device; foreground: { exe: string; title: string } }) {
  const state: AutoProfileState = device.autoProfile ?? EMPTY
  const save = (s: AutoProfileState) => send('autoProfile', { key: device.key, json: JSON.stringify(s) })
  const [picking, setPicking] = useState(false)

  const matching = state.enabled ? state.rules.find((r) => r.exe.toLowerCase() === foreground.exe.toLowerCase() && (!r.title || foreground.title.toLowerCase().includes(r.title.toLowerCase()))) : undefined
  const addRule = (a: RunningApp) => {
    // Apps that share a launcher exe (Minecraft Java is javaw.exe, like every Java app) are told
    // apart by their window title; everything else matches on the exe alone.
    const shared = /^(javaw?|python|pythonw|node|electron)\.exe$/i.test(a.exe)
    save({ ...state, enabled: true, rules: [...state.rules, { exe: a.exe, title: shared ? a.title.split(/[\s:-]/)[0] : '', label: a.title, icon: a.icon, profile: 2 }] })
    setPicking(false)
  }

  return (
    <section className="mt-6">
      <div className="mb-3 flex items-center justify-between">
        <div className="label">{t.autoProfile}</div>
        <Toggle checked={state.enabled} onChange={(v) => save({ ...state, enabled: v })} />
      </div>
      <p className="mb-4 text-[12.5px] leading-[18px] text-fg-faint">{t.autoProfileHint}</p>

      <div className={`overflow-hidden rounded-xl bg-white/[0.025] ring-1 ring-inset ring-line transition-opacity ${state.enabled ? '' : 'pointer-events-none opacity-40'}`}>
        {state.rules.map((r, i) => (
          <div key={i} className={`flex items-center gap-3 border-b border-line px-4 py-2.5 ${matching === r ? 'bg-accent/[0.06]' : ''}`}>
            {r.icon ? <img src={r.icon} alt="" className="size-5" /> : <AppWindow size={15} className={matching === r ? 'text-accent' : 'text-fg-faint'} />}
            <div className="min-w-0 flex-1">
              <div className="truncate text-[13.5px] font-medium">{r.label || r.exe}</div>
              <div className="flex items-center gap-1.5 text-[11.5px] text-fg-faint">
                <span>{r.exe}</span>
                <span>·</span>
                <input
                  value={r.title ?? ''}
                  placeholder={t.titleContains}
                  onChange={(e) => save({ ...state, rules: state.rules.map((x, j) => (j === i ? { ...x, title: e.target.value } : x)) })}
                  className="w-32 bg-transparent text-fg-muted outline-none placeholder:text-fg-faint/70"
                />
              </div>
            </div>
            <Segmented id={`ap-${device.key}-${i}`} value={r.profile} onChange={(v) => save({ ...state, rules: state.rules.map((x, j) => (j === i ? { ...x, profile: v } : x)) })} options={[1, 2, 3].map((p) => ({ value: p, label: String(p) }))} />
            <button onClick={() => save({ ...state, rules: state.rules.filter((_, j) => j !== i) })} className="grid size-7 place-items-center rounded text-fg-faint hover:text-bad">
              <X size={14} />
            </button>
          </div>
        ))}

        <div className="flex items-center gap-3 border-b border-line px-4 py-2.5">
          <span className="flex-1 text-[13px] text-fg-muted">{t.otherwise}</span>
          <Segmented
            id={`ap-fallback-${device.key}`}
            value={state.fallback}
            onChange={(v) => save({ ...state, fallback: v })}
            options={[{ value: 0, label: t.leave }, ...[1, 2, 3].map((p) => ({ value: p, label: String(p) }))]}
          />
          <span className="w-7" />
        </div>

        <div className="relative px-4 py-2.5">
          <button
            onClick={() => setPicking(true)}
            className="inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-[13px] font-medium text-accent hover:bg-accent/10"
          >
            <Plus size={14} /> {t.addApp}
          </button>
          <span className="ml-3 text-[11.5px] text-fg-faint">
            {t.nowActive}: <span className="text-fg-muted">{foreground.exe || '—'}</span>
            {matching && <span className="text-accent"> → {t.profile} {matching.profile}</span>}
          </span>
          <AppPicker open={picking} onClose={() => setPicking(false)} onPick={addRule} />
        </div>
      </div>
    </section>
  )
}
