// Modules that the Vite config generates (see languageFlags() in vite.config.ts)
declare module 'virtual:language-flags' {
  import type { ComponentType, SVGProps } from 'react'
  // Flag components by ISO 3166-1 alpha-2 code (upper case) - exactly the flags that languages.json uses
  const flags: Record<string, ComponentType<SVGProps<SVGSVGElement>>>
  export default flags
}
