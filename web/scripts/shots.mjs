// Screenshots of the UI with mock data, for design review: node scripts/shots.mjs <outDir> [lang]
import puppeteer from 'puppeteer-core'
const [outDir, lang = 'tr'] = process.argv.slice(2)
const browser = await puppeteer.launch({
  executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  defaultViewport: { width: 1080, height: 700, deviceScaleFactor: 1 },
})
const page = await browser.newPage()
for (const [name, hash] of [['home', ''], ['detail', 'device=ModelOWireless'], ['settings', 'settings'], ['about', 'about'], ['perf', 'device=ModelOWireless/performance'], ['light', 'device=ModelOWireless/lighting'], ['buttons', 'device=ModelOWireless/buttons']]) {
  await page.goto(`http://localhost:4173/index.html?lang=${lang}&v=${name}#${hash}`, { waitUntil: 'networkidle0' })
  await new Promise((r) => setTimeout(r, 1200))
  await page.screenshot({ path: `${outDir}/p-${name}.png` })
  console.log('shot', name)
}
await browser.close()
