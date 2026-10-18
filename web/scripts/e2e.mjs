// End-to-end test against a real Model O/D family mouse, driving the actual app UI.
// Start BetterGlorious with WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222 and
// --page=device=<key>/performance, then: node scripts/e2e.mjs <path to BetterGlorious.exe>
// Every change is checked by reading the mouse independently (BetterGlorious.exe --dump-config);
// the last step restores the starting settings and checks that too.
import puppeteer from 'puppeteer-core'
import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

const exe = process.argv[2]
const dumpFile = join(process.env.APPDATA, 'BetterGlorious', 'config-dump.txt')
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

function dump() {
  execFileSync(exe, ['--dump-config'])
  const text = readFileSync(dumpFile, 'utf8')
  const json = text.split('\n').map((l) => l.match(/read in \d+ ms: (\{.*\})/)).find(Boolean)
  if (!json) throw new Error('mouse did not answer: ' + text)
  return JSON.parse(json[1])
}

const browser = await puppeteer.connect({ browserURL: 'http://127.0.0.1:9222', defaultViewport: null })
const page = (await browser.pages()).find((p) => p.url().includes('app.betterglorious'))
page.on('dialog', (d) => d.accept())

const click = async (text, { exact = true } = {}) => {
  const ok = await page.evaluate(
    (t, exact) => {
      const el = [...document.querySelectorAll('button')].find((e) => (exact ? e.textContent.trim() === t : e.textContent.includes(t)))
      if (!el) return false
      el.click()
      return true
    },
    text,
    exact,
  )
  if (!ok) throw new Error(`no button "${text}"`)
}

// Set a React-controlled range input inside the section whose label is `label`.
const slide = async (label, value, { release = false } = {}) => {
  const ok = await page.evaluate(
    (label, value, release) => {
      const row = [...document.querySelectorAll('section, div')].find((s) => s.firstElementChild?.firstElementChild?.textContent.trim().toLowerCase() === label.toLowerCase() && s.querySelector('input[type=range]'))
      const input = row?.querySelector('input[type=range]')
      if (!input) return false
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, String(value))
      input.dispatchEvent(new Event('input', { bubbles: true }))
      input.dispatchEvent(new Event('change', { bubbles: true }))
      if (release) input.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }))
      return true
    },
    label,
    value,
    release,
  )
  if (!ok) throw new Error(`no slider "${label}"`)
}

const results = []
async function step(name, action, check, wait = 2500) {
  try {
    await action()
    await sleep(wait)
    const cfg = dump()
    const ok = check(cfg)
    results.push([ok ? 'PASS' : 'FAIL', name])
    console.log(ok ? 'PASS' : 'FAIL', name)
    return cfg
  } catch (e) {
    results.push(['FAIL', `${name}: ${e.message}`])
    console.log('FAIL', name, e.message)
  }
}

const base = dump()
console.log('baseline', JSON.stringify({ dpi: base.dpi, active: base.activeStage, poll: base.pollingHz, deb: base.debounceMs, sleep: base.sleepSeconds, fx: base.effect }))
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b)
const alt = (list, cur) => list.find((v) => v !== cur)

await click('Performans')
await sleep(800)
const poll = alt([500, 1000], base.pollingHz)
await step(`polling → ${poll} Hz`, () => click(`${poll}`), (c) => c.pollingHz === poll)
await step('undo → polling back', () => click('Geri al'), (c) => c.pollingHz === base.pollingHz, 4000)
const deb = alt([7, 9], base.debounceMs)
await step(`debounce → ${deb} ms`, () => slide('Debounce', deb), (c) => c.debounceMs === deb)
const target = base.activeStage === 0 ? 1 : 0
await step(`active DPI stage → ${target + 1}`, async () => {
  await click(String(base.dpi[target]))
  await sleep(300)
  await click('Aktif kademe yap')
}, (c) => c.activeStage === target)
const last = base.dpi.length - 1
const newDpi = base.dpi[last] + 50
await step(`stage ${last + 1} DPI → ${newDpi}`, async () => {
  await click(String(base.dpi[last]))
  await sleep(300)
  await page.evaluate((v) => {
    const input = document.querySelector('input[type=range][step="50"]')
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, String(v))
    input.dispatchEvent(new Event('input', { bubbles: true }))
    input.parentElement.parentElement.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }))
  }, newDpi)
}, (c) => c.dpi[last] === newDpi)
const sleepMin = base.sleepSeconds === 300 ? 10 : 5
await step(`sleep → ${sleepMin} min`, () => slide('Uyku süresi', sleepMin), (c) => c.sleepSeconds === sleepMin * 60)

await click('Işık')
await sleep(800)
const fx = base.effect === 4 ? 3 : 4
await step(`lighting effect → ${fx}`, () => click(fx === 4 ? 'Sabit renk' : 'Nefes'), (c) => c.effect === fx)

await click('Tuşlar')
await sleep(800)
await step('forward button → play/pause', async () => {
  // pick the button on the map (SVG label) or, without a map, in the list
  const onMap = await page.evaluate(() => {
    const g = [...document.querySelectorAll('svg g')].find((x) => x.querySelector('text')?.textContent.trim() === 'İleri')
    if (!g) return false
    g.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    return true
  })
  if (!onMap) await click('İleri (öndeki yan tuş)', { exact: false })
  await sleep(400)
  await click('Medya')
  await sleep(300)
  await click('Oynat / Duraklat')
}, (c) => same(c.buttons['5'].slice(0, 4), [5, 2, 0, 0xcd]))

await step('restore → everything back to start', () => click('Başlangıca dön'), (c) => {
  const keys = ['dpi', 'dpiColors', 'activeStage', 'pollingHz', 'debounceMs', 'liftOff', 'sleepSeconds', 'effect', 'effectSpeed', 'effectColor', 'buttons', 'macros']
  const diff = keys.filter((k) => !same(c[k], base[k]))
  if (diff.length) console.log('  still different:', diff.join(', '))
  return diff.length === 0
}, 9000)

console.log('\n' + results.map((r) => r.join('  ')).join('\n'))
await browser.disconnect()
