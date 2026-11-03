import 'i18next'
import type { DEFAULT_NAMESPACE, de } from './resources'

// Types the key of every t() call (and the {{variables}} it needs) from the German source files.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: typeof DEFAULT_NAMESPACE
    resources: typeof de
  }
}
