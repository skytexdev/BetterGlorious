import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import { RotateCw } from 'lucide-react'
import { send, useAppState } from './bridge'
import { t } from './i18n'
import { Home } from './components/Home'
import { Detail } from './components/Detail'
import { SettingsPage } from './components/SettingsPage'
import { About } from './components/About'
import { Logo } from './components/Logo'
import { IconButton } from './components/ui'

type Page = 'devices' | 'settings' | 'about'

export default function App() {
  const { state, toast } = useAppState()
  // #settings, #about or #device=<key> open a page directly (handy for previews).
  const hash = location.hash.slice(1)
  const [page, setPage] = useState<Page>(hash === 'settings' || hash === 'about' ? hash : 'devices')
  const [openKey, setOpenKey] = useState<string | null>(hash.startsWith('device=') ? hash.slice(7).split('/')[0] : null)
  const [spin, setSpin] = useState(0)

  const open = openKey ? state?.devices.find((d) => d.key === openKey) : undefined
  const view = page === 'devices' ? (open ? `device:${open.key}` : 'devices') : page

  return (
    <div className="relative flex h-full flex-col">
      {/* Ambient light from the top: gives the black some depth without a gradient "theme". */}
      <div className="pointer-events-none absolute inset-x-0 top-0 h-[420px]" style={{ background: 'radial-gradient(60% 100% at 50% 0%, rgb(76 194 255 / .05), transparent 70%)' }} />

      <header className="relative z-10 grid h-14 shrink-0 grid-cols-[1fr_auto_1fr] items-center px-5">
        <div className="flex items-center gap-2.5">
          <Logo size={24} />
          <span className="text-[15px] font-semibold leading-none tracking-[-0.01em]">BetterGlorious</span>
        </div>

        <nav className="flex items-center gap-1 rounded-[12px] bg-white/[0.04] p-1 ring-1 ring-inset ring-white/[0.05]">
          {(['devices', 'settings', 'about'] as Page[]).map((p) => {
            const active = page === p
            return (
              <button
                key={p}
                onClick={() => {
                  setPage(p)
                  if (p === 'devices') setOpenKey(null)
                }}
                className={`relative h-8 rounded-[9px] px-4 text-[13px] font-medium transition-colors duration-150 ${active ? 'text-fg' : 'text-fg-muted hover:text-fg'}`}
              >
                {active && <motion.span layoutId="nav-pill" className="absolute inset-0 rounded-[9px] bg-white/[0.08] shadow-[inset_0_0_0_1px_rgb(255_255_255/.06)]" transition={{ type: 'spring', stiffness: 600, damping: 45 }} />}
                <span className="relative">{t[p]}</span>
                {p === 'about' && state?.update && <span className="absolute right-1.5 top-1.5 size-1.5 rounded-full bg-accent" />}
              </button>
            )
          })}
        </nav>

        <div className="flex justify-end">
          <IconButton
            title={t.refresh}
            onClick={() => {
              setSpin((s) => s + 1)
              send('refresh')
            }}
          >
            <motion.span animate={{ rotate: spin * 360 }} transition={{ duration: 0.6, ease: [0.2, 0, 0, 1] }} className="grid">
              <RotateCw size={16} strokeWidth={1.75} />
            </motion.span>
          </IconButton>
        </div>
      </header>

      <main className="relative min-h-0 flex-1 overflow-y-auto">
        {state && (
          <AnimatePresence mode="wait">
            <motion.div
              key={view}
              className="h-full"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.2, ease: [0.2, 0, 0, 1] }}
            >
              {view === 'devices' && <Home devices={state.devices} lowBattery={state.settings.lowBattery} onOpen={setOpenKey} />}
              {open && page === 'devices' && <Detail device={open} lowBattery={state.settings.lowBattery} softMacros={state.softMacros} foreground={state.foreground} onBack={() => setOpenKey(null)} />}
              {page === 'settings' && <SettingsPage s={state.settings} />}
              {page === 'about' && <About version={state.version} update={state.update} />}
            </motion.div>
          </AnimatePresence>
        )}
      </main>

      <AnimatePresence>
        {toast && (
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: 8 }}
            className="absolute bottom-6 left-1/2 z-20 -translate-x-1/2 rounded-full bg-raised px-4 py-2 text-[13px] font-medium shadow-[0_8px_32px_rgb(0_0_0/.45),inset_0_0_0_1px_rgb(255_255_255/.08)]"
          >
            {toast}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}
