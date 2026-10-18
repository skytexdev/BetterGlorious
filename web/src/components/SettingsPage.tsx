import type { ReactNode } from 'react'
import { send, type AppSettings } from '../bridge'
import { ChevronDown } from 'lucide-react'
import { LANGUAGES, pct, t } from '../i18n'
import { Segmented, Slider, Toggle } from './ui'

export function SettingsPage({ s }: { s: AppSettings }) {
  const set = (key: keyof AppSettings, value: unknown) => send('setting', { key, value })
  return (
    <div className="mx-auto w-full max-w-[680px] px-8 py-10">
      <h1 className="text-[28px] font-semibold tracking-[-0.02em]">{t.settings}</h1>

      <Group title={t.general}>
        <Row title={t.startup} body={t.startupBody}>
          <Toggle checked={s.startup} onChange={(v) => set('startup', v)} />
        </Row>
        <Row title={t.minimizeToTray} body={t.minimizeToTrayBody}>
          <Toggle checked={s.minimizeToTray} onChange={(v) => set('minimizeToTray', v)} />
        </Row>
        <Row title={t.language}>
          <div className="relative">
            <select
              value={s.language}
              onChange={(e) => set('language', e.target.value)}
              className="h-8 appearance-none rounded-[10px] bg-white/[0.05] py-0 pl-3 pr-8 text-[12.5px] font-medium text-fg outline-none ring-1 ring-inset ring-white/[0.08] hover:bg-white/[0.08] focus:ring-accent/60"
            >
              <option value="auto" className="bg-raised">{t.langAuto}</option>
              {LANGUAGES.map((l) => (
                <option key={l.code} value={l.code} className="bg-raised">{l.name}</option>
              ))}
            </select>
            <ChevronDown size={14} className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 text-fg-faint" />
          </div>
        </Row>
      </Group>

      <Group title={t.battery}>
        <Row title={t.notifications} body={t.notificationsBody}>
          <Toggle checked={s.notifications} onChange={(v) => set('notifications', v)} />
        </Row>
        <Row title={t.lowBattery}>
          <Slider value={s.lowBattery} min={5} max={50} step={5} onChange={(v) => set('lowBattery', v)} format={pct} />
        </Row>
        <Row title={t.poll} body={t.pollBody}>
          <Segmented
            id="poll"
            value={s.pollSeconds}
            onChange={(v) => set('pollSeconds', v)}
            options={[30, 60, 120, 300, 600].map((v) => ({ value: v, label: v < 60 ? `${v}${t.sec}` : `${v / 60}${t.min}` }))}
          />
        </Row>
        <Row title={t.trayMode}>
          <Segmented
            id="tray"
            value={s.tray}
            onChange={(v) => set('tray', v)}
            options={[
              { value: 'per_device', label: t.trayPerDevice },
              { value: 'lowest', label: t.trayLowest },
            ]}
          />
        </Row>
      </Group>
    </div>
  )
}

function Group({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mt-9">
      <div className="label mb-3">{title}</div>
      <div className="divide-y divide-line overflow-hidden rounded-xl bg-white/[0.025] ring-1 ring-inset ring-line">{children}</div>
    </section>
  )
}

function Row({ title, body, children }: { title: string; body?: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-6 px-5 py-4">
      <div className="min-w-0">
        <div className="text-[14px] font-medium text-fg">{title}</div>
        {body && <div className="mt-0.5 text-[12.5px] leading-[18px] text-fg-faint">{body}</div>}
      </div>
      {children}
    </div>
  )
}
