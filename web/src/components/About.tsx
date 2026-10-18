import { ClipboardCopy, Download, FolderOpen, ExternalLink } from 'lucide-react'
import { send } from '../bridge'
import { t, tf } from '../i18n'
import { Button } from './ui'
import { Logo } from './Logo'

export function About({ version, update }: { version: string; update?: { version: string; url: string } | null }) {
  return (
    <div className="mx-auto flex min-h-full w-full max-w-[560px] flex-col items-center justify-center px-8 py-10 text-center">
      <div className="flex flex-col items-center gap-4">
        <Logo size={56} />
        <div>
          <div className="text-[28px] font-semibold leading-8 tracking-[-0.02em]">BetterGlorious</div>
          <div className="text-[13px] text-fg-faint tabular">v{version}</div>
        </div>
        {update && (
          <button
            onClick={() => send('openUrl', { url: update.url })}
            className="inline-flex h-9 items-center gap-2 rounded-lg bg-accent px-4 text-[13px] font-semibold text-[#06121a] transition-opacity hover:opacity-90"
          >
            <Download size={15} /> {tf('updateAvailable', { version: update.version })}
          </button>
        )}
      </div>
      <p className="mt-6 max-w-[460px] text-[14px] leading-6 text-fg-muted">{t.aboutBody}</p>
      <div className="mt-7 flex flex-wrap justify-center gap-2">
        <Button icon={<ClipboardCopy size={15} strokeWidth={1.75} />} onClick={() => send('copyDiagnostics')}>{t.copyDiagnostics}</Button>
        <Button icon={<FolderOpen size={15} strokeWidth={1.75} />} onClick={() => send('openLogs')}>{t.openLogs}</Button>
        <Button icon={<ExternalLink size={15} strokeWidth={1.75} />} onClick={() => send('openUrl', { url: 'https://github.com/skytexdev/BetterGlorious' })}>{t.sourceCode}</Button>
      </div>
      <p className="mt-10 border-t border-line pt-5 text-[12px] leading-5 text-fg-faint">{t.disclaimer}</p>
    </div>
  )
}
