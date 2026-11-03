/// <reference types="vitest/config" />
import { readFileSync } from 'node:fs'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import type { Plugin } from 'vite'

// Fixed ports of the dev stack: Vite 5230 -> backend 5231.
const FRONTEND_PORT = 5230
const BACKEND = 'http://localhost:5231'

// The product name lives in ../branding.json only; index.html gets it from there.
const branding = JSON.parse(readFileSync(new URL('../branding.json', import.meta.url), 'utf8')) as { productName: string }

// The languages of the app live in ../languages.json only. The flags of exactly those languages are bundled (virtual module
// 'virtual:language-flags'): one inline SVG component each from country-flag-icons, so nothing is fetched at runtime.
function languageFlags(): Plugin {
  const id = 'virtual:language-flags'
  const resolved = '\0' + id
  return {
    name: 'language-flags',
    resolveId: (source) => (source === id ? resolved : null),
    load(loadedId) {
      if (loadedId !== resolved) return null
      const registry = JSON.parse(readFileSync(new URL('../languages.json', import.meta.url), 'utf8')) as { languages: { flag: string }[] }
      const codes = [...new Set(registry.languages.map((language) => language.flag.toUpperCase()))]
      const imports = codes.map((code, index) => `import flag${index} from 'country-flag-icons/react/3x2/${code}'`)
      const entries = codes.map((code, index) => `  ${JSON.stringify(code)}: flag${index},`)
      return [...imports, 'export default {', ...entries, '}'].join('\n')
    },
  }
}

function brandingInHtml(): Plugin {
  return {
    name: 'branding-in-html',
    transformIndexHtml: { order: 'pre', handler: (html) => html.replaceAll('%PRODUCT_NAME%', branding.productName) },
  }
}

export default defineConfig({
  plugins: [react(), tailwindcss(), brandingInHtml(), languageFlags()],
  server: {
    host: '127.0.0.1',
    port: FRONTEND_PORT,
    strictPort: true,
    allowedHosts: ['localhost', 'host.docker.internal'],
    fs: { allow: ['..'] },
    proxy: {
      '/api': { target: BACKEND, changeOrigin: true },
      '/hubs': { target: BACKEND, changeOrigin: true, ws: true },
      '/health': { target: BACKEND, changeOrigin: true },
    },
  },
  build: {
    assetsInlineLimit: 0,
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['src/test/setup.ts'],
  },
})
