import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useState } from 'react'
import { AppWindow, RotateCw, X } from 'lucide-react'
import { send, useMessage } from '../bridge'
import { t } from '../i18n'

export interface RunningApp { exe: string; title: string; icon: string | null; thumb: string | null }

/** Pick an open app by its window, like Discord's screen-share picker. */
export function AppPicker({ open, onClose, onPick }: { open: boolean; onClose: () => void; onPick: (app: RunningApp) => void }) {
  const [apps, setApps] = useState<RunningApp[] | null>(null)
  useMessage('apps', (list: RunningApp[]) => setApps(list))
  const refresh = () => {
    setApps(null)
    send('runningApps')
  }
  useEffect(() => {
    if (open) refresh()
  }, [open])

  return (
    <AnimatePresence>
      {open && (
        <motion.div className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-8 backdrop-blur-sm" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} onClick={onClose}>
          <motion.div
            className="flex max-h-full w-full max-w-[880px] flex-col overflow-hidden rounded-2xl bg-surface shadow-[0_24px_80px_rgb(0_0_0/.6)] ring-1 ring-inset ring-white/10"
            initial={{ scale: 0.97, y: 8 }}
            animate={{ scale: 1, y: 0 }}
            exit={{ scale: 0.97, y: 8 }}
            transition={{ duration: 0.22, ease: [0.2, 0, 0, 1] }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-center justify-between border-b border-line px-6 py-4">
              <div>
                <div className="text-[16px] font-semibold">{t.pickApp}</div>
                <div className="mt-0.5 text-[12.5px] text-fg-faint">{t.pickAppHint}</div>
              </div>
              <div className="flex items-center gap-1">
                <button onClick={refresh} title={t.refresh} className="grid size-9 place-items-center rounded-lg text-fg-muted hover:bg-white/[0.06] hover:text-fg">
                  <RotateCw size={15} />
                </button>
                <button onClick={onClose} className="grid size-9 place-items-center rounded-lg text-fg-muted hover:bg-white/[0.06] hover:text-fg">
                  <X size={16} />
                </button>
              </div>
            </div>
            <div className="grid min-h-[260px] grid-cols-3 gap-4 overflow-y-auto p-6">
              {apps == null &&
                Array.from({ length: 6 }, (_, i) => <div key={i} className="aspect-[16/11] animate-pulse rounded-xl bg-white/[0.04]" />)}
              {apps?.length === 0 && <p className="col-span-3 py-10 text-center text-[13px] text-fg-faint">{t.noApps}</p>}
              {apps?.map((a) => (
                <button key={a.exe + a.title} onClick={() => onPick(a)} className="group flex flex-col overflow-hidden rounded-xl bg-white/[0.03] text-left ring-1 ring-inset ring-line transition-all hover:-translate-y-0.5 hover:bg-white/[0.06] hover:ring-accent/50">
                  <div className="relative grid aspect-[16/10] place-items-center overflow-hidden bg-black/40">
                    {a.thumb ? <img src={a.thumb} alt="" className="size-full object-cover object-top" /> : a.icon ? <img src={a.icon} alt="" className="size-12 opacity-80" /> : <AppWindow size={34} className="text-fg-faint" />}
                  </div>
                  <div className="flex items-center gap-2.5 px-3 py-2.5">
                    {a.icon ? <img src={a.icon} alt="" className="size-5 shrink-0" /> : <AppWindow size={18} className="shrink-0 text-fg-faint" />}
                    <div className="min-w-0">
                      <div className="truncate text-[13px] font-medium">{a.title}</div>
                      <div className="truncate text-[11px] text-fg-faint">{a.exe}</div>
                    </div>
                  </div>
                </button>
              ))}
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  )
}
