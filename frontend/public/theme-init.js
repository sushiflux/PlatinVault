// The in-game overlay of the Windows program (route /overlay, src/overlay/overlayPage.ts): marked before the first paint, so the
// page is transparent from the very first frame (index.css, html[data-pv-overlay]) - never a full-screen opaque flash over a game.
;(function () {
  if (location.pathname.replace(/\/+$/, '') === '/overlay') document.documentElement.dataset.pvOverlay = 'true'
})()

// Synchronously initializes the document color scheme before first paint to prevent FOUC.
// Only two schemes exist: light and dark. A first visit (nothing stored) starts from the OS preference once, and a value stored by
// an older version ("system", or anything else) becomes the scheme it resolves to right now; the choice is stored either way.
;(function () {
  var saved = 'light'
  try {
    saved = localStorage.getItem('companion_vault_theme_v2')
    if (saved !== 'light' && saved !== 'dark') {
      saved = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
      localStorage.setItem('companion_vault_theme_v2', saved)
    }
  } catch (e) {
    // Ignore storage errors in restricted contexts (the OS preference still decides below)
    if (saved !== 'light' && saved !== 'dark') {
      saved = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
    }
  }
  var meta = document.getElementById('color-scheme-meta')
  if (meta) meta.setAttribute('content', saved)
  document.documentElement.dataset.theme = saved
})()

// UI size ("Kleiner" / "Größer" in the header): applied before the first paint like the theme, so the page never loads at
// 100 % and then jumps. The root font size is calc(100% * var(--pv-ui-scale)) in index.css; the valid steps and the
// snapping live in src/utils/uiScale.ts (same storage key), this only accepts a sane percentage.
;(function () {
  try {
    var percent = parseInt(localStorage.getItem('pv_ui_scale_v1') || '', 10)
    if (percent >= 50 && percent <= 200) {
      var root = document.documentElement
      root.style.setProperty('--pv-ui-scale', String(percent / 100))
      root.dataset.uiScale = String(percent)
    }
  } catch (e) {
    // Ignore storage errors in restricted contexts
  }
})()

// App language: <html lang> is set before the first paint from the copy of the chosen language this browser keeps (same key as
// src/i18n/index.ts), so hyphenation, screen readers and the browser's translate offer start with the right language. A plain
// script cannot read languages.json (the list of languages), so it takes any stored language code; src/i18n/index.ts checks it
// against the list when the app starts and corrects it. A first visit keeps the lang of index.html until then.
;(function () {
  try {
    var stored = localStorage.getItem('pv_ui_language_v1')
    if (stored && /^[a-z]{2,3}$/.test(stored)) document.documentElement.lang = stored
  } catch (e) {
    // Ignore storage errors in restricted contexts (index.html's default stays)
  }
})()
