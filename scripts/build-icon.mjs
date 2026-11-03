// Renders the app icon (frontend/public/favicon.svg) into branding/platinvault.ico for the Windows bridge exe, its window
// and tray icon. Runs inside the Playwright container via scripts/build-icon.ps1 (Chromium renders the SVG exactly as the
// browser does). The ICO holds PNG-compressed images, which Windows supports since Vista.
import { readFileSync, writeFileSync } from 'node:fs'
import { chromium } from 'playwright'

const [source, target] = process.argv.slice(2)
const svg = readFileSync(source, 'utf8')
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]

const browser = await chromium.launch()
const page = await browser.newPage()
const images = []
for (const size of sizes) {
  await page.setViewportSize({ width: size, height: size })
  await page.setContent(
    `<html><body style="margin:0;background:transparent">${svg.replace('<svg ', `<svg width="${size}" height="${size}" `)}</body></html>`
  )
  images.push(await page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } }))
}
await browser.close()

// ICONDIR (6 bytes) + one ICONDIRENTRY (16 bytes) per image, then the PNG payloads
const header = Buffer.alloc(6 + 16 * images.length)
header.writeUInt16LE(0, 0)
header.writeUInt16LE(1, 2)
header.writeUInt16LE(images.length, 4)
let offset = header.length
images.forEach((png, i) => {
  const entry = 6 + 16 * i
  const size = sizes[i]
  header.writeUInt8(size >= 256 ? 0 : size, entry)
  header.writeUInt8(size >= 256 ? 0 : size, entry + 1)
  header.writeUInt8(0, entry + 2)
  header.writeUInt8(0, entry + 3)
  header.writeUInt16LE(1, entry + 4)
  header.writeUInt16LE(32, entry + 6)
  header.writeUInt32LE(png.length, entry + 8)
  header.writeUInt32LE(offset, entry + 12)
  offset += png.length
})
writeFileSync(target, Buffer.concat([header, ...images]))
console.log(`Icon geschrieben: ${target} (${sizes.join(', ')} px)`)
