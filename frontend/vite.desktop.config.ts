import { mergeConfig } from 'vite'
import base from './vite.config.ts'

// Second Vite dev server for developing the native Windows program (start-windows.ps1). The window of PlatinVault.exe loads it
// (hot reload) and its /api and /hubs go to the SERVER OF THE WINDOWS PROGRAM, never to the browser stack's backend on 5231
// (that would bypass the program's own server and show the wrong host). The ports are fixed in start-windows.ps1 (5236 / 5234)
// and passed in by the script - there is deliberately no default here, so a
// missing value can never fall back to the browser stack's 5230/5231.

function requiredEnv(name: string): string {
  const value = process.env[name]
  if (!value) throw new Error(`${name} is not set - start this server with start-windows.ps1 (it sets the fixed ports).`)
  return value
}

const port = Number(requiredEnv('PV_DESKTOP_VITE_PORT'))
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error(`PV_DESKTOP_VITE_PORT is not a valid port: ${process.env.PV_DESKTOP_VITE_PORT}`)
const backend = requiredEnv('PV_DESKTOP_BACKEND_URL')

export default mergeConfig(base, {
  // Own optimizer cache: two Vite servers on one node_modules/.vite would overwrite each other's pre-bundled dependencies
  cacheDir: 'node_modules/.vite-desktop',
  server: {
    port,
    strictPort: true,
    proxy: {
      '/api': { target: backend, changeOrigin: true },
      '/hubs': { target: backend, changeOrigin: true, ws: true },
      '/health': { target: backend, changeOrigin: true },
    },
  },
})
