// Vitest setup: the app's i18n is initialised (German, the source language) before any component test renders.
import { applyLanguage } from '../i18n'

await applyLanguage('de')
