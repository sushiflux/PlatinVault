import { useEffect, useRef } from 'react'
import { AppCopyright, AppVersion, LicensesLink } from './FooterParts'
import { MadeWith } from './MadeWith'
import { VersionCheck } from './updates/VersionCheck'
import { ScrollTopButton } from './ScrollTopButton'

interface AppFooterProps {
  // Dashboard: always visible at the bottom of the window, the game grid scrolls underneath
  sticky?: boolean
}

// The footer of the dashboard: version left, credit line centred, copyright right. The game page has no page footer: the same
// pieces (FooterParts.tsx, MadeWith.tsx) are in the info card at the bottom of its side navigation (gamenav/NavInfo.tsx).
// It publishes its height as --pv-footer-h (resize only, never while scrolling): the "back to top" button keeps its distance to it.
export function AppFooter({ sticky = false }: AppFooterProps) {
  const footerRef = useRef<HTMLElement>(null)

  useEffect(() => {
    const footer = footerRef.current
    if (!footer) return
    const root = document.documentElement
    const update = () => root.style.setProperty('--pv-footer-h', `${Math.ceil(footer.getBoundingClientRect().height)}px`)
    update()
    const observer = new ResizeObserver(update)
    observer.observe(footer)
    return () => {
      observer.disconnect()
      root.style.removeProperty('--pv-footer-h')
    }
  }, [])

  return (
    <>
      <footer
        ref={footerRef}
        data-app-footer
        className={`mt-auto px-4 py-4 sm:px-6 no-print ${sticky ? 'sticky bottom-0 z-30 backdrop-blur-md' : ''}`}
        style={{
          background: sticky ? 'var(--bg-glass)' : 'var(--bg-surface)',
          borderTop: '1px solid var(--border-subtle)',
        }}
      >
        <div
          className="mx-auto grid max-w-[var(--pv-page-max)] grid-cols-1 items-center gap-2 text-center text-xs font-medium sm:grid-cols-3"
          style={{ color: 'var(--text-muted)' }}
        >
          <span className="sm:justify-self-start">
            <VersionCheck>
              <AppVersion className="text-[color:var(--text-secondary)]" />
            </VersionCheck>
          </span>
          <span className="sm:justify-self-center">
            <MadeWith />
          </span>
          <span className="inline-flex flex-wrap items-center justify-center gap-x-2 sm:justify-self-end">
            <AppCopyright />
            <span aria-hidden="true">·</span>
            {/* Usage licence and third-party notices open in the app's own viewer */}
            <LicensesLink
              className="font-semibold text-[color:var(--text-secondary)] underline decoration-transparent underline-offset-2 transition-colors hover:decoration-current focus-visible:decoration-current"
            />
          </span>
        </div>
      </footer>
      <ScrollTopButton />
    </>
  )
}
