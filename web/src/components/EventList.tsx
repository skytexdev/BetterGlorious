import { AnimatePresence, motion } from 'motion/react'
import { ArrowDown, ArrowUp, X } from 'lucide-react'
import { t } from '../i18n'
import { describeEvent, type TimedEvent } from '../hid'

/** Editable step list shared by the macro editor. */
export function EventList({ events, onChange }: { events: TimedEvent[]; onChange: (e: TimedEvent[]) => void }) {
  let mask = 0
  const total = events.reduce((s, e) => s + e.delay, 0)
  return (
    <div className="mt-4 rounded-xl bg-white/[0.025] ring-1 ring-inset ring-line">
      <div className="flex items-center justify-between border-b border-line px-4 py-2.5">
        <span className="label">{t.events} · {events.length}</span>
        <span className="text-[11.5px] text-fg-faint tabular">{total} ms</span>
      </div>
      <div className="max-h-[260px] overflow-y-auto py-1">
        {events.length === 0 && <p className="px-4 py-6 text-center text-[13px] text-fg-faint">{t.emptyMacro}</p>}
        <AnimatePresence initial={false}>
          {events.map((ev, i) => {
            const d = describeEvent(ev.event, mask)
            if (ev.event.type === 'mouse') mask = ev.event.mask
            return (
              <motion.div key={i} layout initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} className="group flex items-center gap-3 px-4 py-1.5 hover:bg-white/[0.03]">
                {i > 0 ? (
                  <label className="flex w-[84px] items-center gap-1 text-[12px] text-fg-faint">
                    <input
                      type="number"
                      min={0}
                      max={65535}
                      value={ev.delay}
                      onChange={(e) => onChange(events.map((x, j) => (j === i ? { ...x, delay: Math.max(0, Number(e.target.value)) } : x)))}
                      className="h-7 w-14 rounded-md bg-transparent px-1.5 text-right tabular text-fg-muted outline-none ring-1 ring-inset ring-transparent hover:ring-white/10 focus:ring-accent/50"
                    />
                    ms
                  </label>
                ) : (
                  <span className="w-[84px]" />
                )}
                <span className={`grid size-5 place-items-center rounded ${d.down ? 'bg-accent/15 text-accent' : 'bg-white/[0.06] text-fg-faint'}`}>
                  {d.down ? <ArrowDown size={12} /> : <ArrowUp size={12} />}
                </span>
                <span className="flex-1 text-[13px] font-medium">{d.label}</span>
                <button onClick={() => onChange(events.filter((_, j) => j !== i))} className="grid size-6 place-items-center rounded text-fg-faint opacity-0 hover:text-bad group-hover:opacity-100">
                  <X size={13} />
                </button>
              </motion.div>
            )
          })}
        </AnimatePresence>
      </div>
    </div>
  )
}
