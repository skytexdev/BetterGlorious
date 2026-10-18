// Lighting check on a real Model O-family mouse: each effect is written through the UI and read back
// with --dump-config (effect number, raw speed byte, colors). Restores the start state at the end.
import puppeteer from 'puppeteer-core'
import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
const exe = process.argv[2]
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const dump = () => {
  execFileSync(exe, ['--dump-config'])
  const text = readFileSync(join(process.env.APPDATA, 'BetterGlorious', 'config-dump.txt'), 'utf8')
  return JSON.parse(text.split('\n').map((l) => l.match(/read in \d+ ms: (\{.*\})/)).find(Boolean)[1])
}
const browser = await puppeteer.connect({ browserURL: 'http://127.0.0.1:9222', defaultViewport: null })
const page = (await browser.pages()).find((p) => p.url().includes('app.betterglorious'))
page.on('dialog', (d) => d.accept())
const click = (t) => page.evaluate((t) => { const b = [...document.querySelectorAll('button')].find((b) => b.textContent.trim() === t); b?.click(); return !!b }, t)
await click('Işık'); await sleep(800)
const expect = { 'Glorious': [1, 9], 'Kesintisiz nefes': [2, 9], 'Nefes': [3, 9], 'Kuyruk': [6, 9], 'Rave': [7, 90], 'Dalga': [8, 90], 'Tek renk nefes': [5, 9] }
let pass = 0
const baseSpeed = dump().effectSpeed
for (const [name, [id, speed]] of Object.entries(expect)) {
  const found = await click(name); console.log(new Date().toISOString().slice(11, 19), 'click', name, found); await sleep(3000)
  const c = dump()
  const ok = c.effect === id && Math.abs(c.effectSpeed - baseSpeed) <= 5
  if (ok) pass++
  console.log(ok ? 'PASS' : 'FAIL', name, '→ effect', c.effect, 'speed byte', c.effectSpeed, 'colors', (c.effectColors || []).join(' '))
}
await click('Başlangıca dön'); await sleep(6000)
console.log(`${pass}/${Object.keys(expect).length}`)
await browser.disconnect()
