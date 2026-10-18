// Checks UI flows that don't need --dump-config: live language switch, tabs, button map screenshot,
// building a macro and assigning it to a button (then restoring).
import puppeteer from 'puppeteer-core'
const out = process.argv[2] ?? '.'
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const browser = await puppeteer.connect({ browserURL: 'http://127.0.0.1:9222', defaultViewport: null })
let page = (await browser.pages()).find((p) => p.url().includes('app.betterglorious'))
page.on('dialog', (d) => d.accept())
const texts = () => page.evaluate(() => [...document.querySelectorAll('button')].map((b) => b.textContent.trim()))
const click = async (t, exact = true) => {
  const ok = await page.evaluate((t, exact) => {
    const el = [...document.querySelectorAll('button')].find((e) => (exact ? e.textContent.trim() === t : e.textContent.includes(t)))
    if (!el) return false
    el.click(); return true
  }, t, exact)
  if (!ok) throw new Error(`no button "${t}"`)
}
const log = (ok, name) => console.log(ok ? 'PASS' : 'FAIL', name)

// language
await click('Ayarlar'); await sleep(1000)
await page.select('select', 'en'); await sleep(2500)
page = (await browser.pages()).find((p) => p.url().includes('app.betterglorious'))
log(page.url().includes('lang=en') && page.url().endsWith('#settings') && (await texts()).includes('Settings'), 'language → English live')
await page.select('select', 'tr'); await sleep(2500)
page = (await browser.pages()).find((p) => p.url().includes('app.betterglorious'))
log(page.url().includes('lang=tr') && (await texts()).includes('Ayarlar'), 'language → Türkçe live')

// tabs
await page.goto(page.url().split('#')[0] + '#device=ModelOWireless/buttons'); await page.reload(); await sleep(3000)
const tabs = await texts()
log(!tabs.includes('Makrolar') && tabs.some((t) => t.includes('Tuşlar')), 'no separate Macros tab')
await page.screenshot({ path: `${out}/ui-buttons.png` })

// macro → Forward button
const picked = await page.evaluate(() => {
  const g = [...document.querySelectorAll('svg g')].find((e) => e.querySelector('text')?.textContent.trim() === 'İleri')
  g?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  return !!g
})
await sleep(800)
const heading = await page.evaluate(() => [...document.querySelectorAll('div')].some((d) => d.children.length === 0 && d.textContent.trim().startsWith('İleri') && d.className.includes('text-[20px]')))
if (!picked || !heading) { console.log('FAIL could not select Forward; stopping before any write'); process.exit(1) }
await click('Makro'); await sleep(600)
await click('Yeni makro', false); await sleep(400)
await click('Sol tık', false); await sleep(300)
await page.screenshot({ path: `${out}/ui-macro.png` })
await click('Kaydet ve bu tuşa ata', false); await sleep(3000)
const assigned = await page.evaluate(() => {
  const h = [...document.querySelectorAll('div')].find((d) => d.className.includes('text-[20px]'))
  return h.textContent + ' | ' + h.parentElement.parentElement.querySelector('span').textContent
})
log(/^İleri.*\| .*Makro/.test(assigned), 'macro saved and assigned to Forward: ' + assigned)
await page.screenshot({ path: `${out}/ui-assigned.png` })
// put everything back: delete the test macro and restore the mouse to how it was when the app opened
await click('Sil', false); await sleep(800)
await click('Başlangıca dön'); await sleep(4000)
await browser.disconnect()
