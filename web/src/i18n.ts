import i18next from 'i18next'
import en from './locales/en.json'
import tr from './locales/tr.json'
import de from './locales/de.json'
import fr from './locales/fr.json'
import es from './locales/es.json'
import pt from './locales/pt-BR.json'
import it from './locales/it.json'
import pl from './locales/pl.json'
import ru from './locales/ru.json'
import ja from './locales/ja.json'
import ko from './locales/ko.json'
import zh from './locales/zh-CN.json'

/**
 * UI text. English (locales/en.json) is the source: every other locale has the same keys, and a
 * missing or empty key falls back to English. To add a language, copy en.json, translate the
 * values, import it here and add it to LANGUAGES (and to src/Core/Strings.cs for the tray).
 */
export type Key = keyof typeof en

export const LANGUAGES: { code: string; name: string }[] = [
  { code: 'en', name: 'English' },
  { code: 'de', name: 'Deutsch' },
  { code: 'es', name: 'Español' },
  { code: 'fr', name: 'Français' },
  { code: 'it', name: 'Italiano' },
  { code: 'pl', name: 'Polski' },
  { code: 'pt-BR', name: 'Português (Brasil)' },
  { code: 'ru', name: 'Русский' },
  { code: 'tr', name: 'Türkçe' },
  { code: 'ja', name: '日本語' },
  { code: 'ko', name: '한국어' },
  { code: 'zh-CN', name: '简体中文' },
]

const resources = { en, tr, de, fr, es, 'pt-BR': pt, it, pl, ru, ja, ko, 'zh-CN': zh }

/** The app passes ?lang=<code> (from Settings, or Windows' language when set to Auto). */
function pick(requested: string | null): string {
  const want = [requested, ...navigator.languages].filter(Boolean) as string[]
  for (const w of want) {
    if (w in resources) return w
    const base = w.split('-')[0]
    if (base === 'pt') return 'pt-BR'
    if (base === 'zh') return 'zh-CN'
    if (base in resources) return base
  }
  return 'en'
}

export const lang = pick(new URLSearchParams(location.search).get('lang'))

i18next.init({
  lng: lang,
  fallbackLng: 'en',
  initAsync: false,
  returnEmptyString: false,
  interpolation: { escapeValue: false }, // React escapes already
  resources: Object.fromEntries(Object.entries(resources).map(([k, v]) => [k, { translation: v }])),
})
document.documentElement.lang = lang

/** Translate with interpolation, e.g. tf('updateAvailable', { version: '0.2.0' }). */
export function tf(key: Key, values?: Record<string, string | number>): string {
  return i18next.t(key, values)
}

/** Property access for plain strings: t.settings. Resolved through i18next, so fallback applies. */
export const t = new Proxy({} as Record<Key, string>, {
  get: (_, key: string) => i18next.t(key),
})

const percentFormat = new Intl.NumberFormat(lang, { style: 'percent', maximumFractionDigits: 0 })
/** "84%" or "%84", whichever the language uses. */
export const pct = (n: number) => percentFormat.format(n / 100)
/** True for languages that write the percent sign first (Turkish). */
export const percentFirst = percentFormat.formatToParts(0.5)[0]?.type === 'percentSign'
