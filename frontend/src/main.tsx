import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './i18n'
import './index.css'
import { App } from './App'
import { OverlayApp } from './overlay/OverlayApp'
import { isOverlayPage } from './overlay/overlayPage'

// /overlay is the in-game overlay of the Windows program: only the popup layer on a transparent page (public/theme-init.js marks
// <html data-pv-overlay> before the first paint); everything else is the app.
createRoot(document.getElementById('root')!).render(
  <StrictMode>{isOverlayPage(window.location.pathname) ? <OverlayApp /> : <App />}</StrictMode>
)
