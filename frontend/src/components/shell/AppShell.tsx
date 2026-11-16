import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Outlet, useLocation, useMatch, useNavigate } from 'react-router'
import { useTranslation } from 'react-i18next'
import { OnboardingTour } from '../onboarding/OnboardingTour'
import { START_TOUR_EVENT, useOnboardingTour } from '../onboarding/useOnboardingTour'
import { useMediaQuery } from '../../utils/useMediaQuery'
import { useStickyHeaderHeight } from '../../utils/useStickyHeaderHeight'
import { OverlayScrollbars } from './OverlayScrollbars'
import { ShellContext, requestPageHints, settingsHref, type ShellApi } from './ShellContext'
import { ShellHeader } from './ShellHeader'
import { ShellNav } from './ShellNav'
import { DRAWER_QUERY, useRailState } from './useShellNav'
import '../../styles/shell.css'

const NAV_ID = 'pv-shell-nav'

interface AppShellProps {
  theme: string
  onToggleTheme: () => void
}

/**
 * The frame around every page of the app (variant "Kristall"): the header on top, the tree navigation on the left (an icon rail when
 * collapsed, a drawer on phones), the page in the content area. The page scrolls the document; header and navigation stick.
 * It owns the one onboarding tour of the app and the overlay scrollbars.
 */
export function AppShell({ theme, onToggleTheme }: AppShellProps) {
  const { t } = useTranslation('shell')
  const location = useLocation()
  const navigate = useNavigate()
  const gameMatch = useMatch('/games/:slug')
  const currentSlug = gameMatch?.params.slug ?? null
  const headerRef = useStickyHeaderHeight<HTMLElement>()
  const drawer = useMediaQuery(DRAWER_QUERY)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [rail, toggleRail] = useRailState()
  const menuReturnRef = useRef<HTMLElement | null>(null)
  const navRef = useRef<HTMLDivElement>(null)

  const onSettings = location.pathname === '/settings'

  // One introduction for the whole app. A tour step that opens the settings ends the tour (it did its job, the page is the destination);
  // "Restart the introduction" on the settings page goes to the start page first, so the tour has a page behind it.
  const tour = useOnboardingTour()
  useEffect(() => {
    // "Restart the introduction" on the settings page: the tour needs a page behind it
    const onStart = () => {
      if (window.location.pathname === '/settings') void navigate('/')
    }
    window.addEventListener(START_TOUR_EVENT, onStart)
    return () => window.removeEventListener(START_TOUR_EVENT, onStart)
  }, [navigate])

  // Back from "Sign in with Steam": the server sends the browser to /?steamLink=ok|error&reason=...; the result belongs on the Steam tab
  useEffect(() => {
    const params = new URLSearchParams(location.search)
    if (!params.has('steamLink') || onSettings) return
    const next = new URLSearchParams({ tab: 'steam', steamLink: params.get('steamLink') ?? '' })
    const reason = params.get('reason')
    if (reason) next.set('reason', reason)
    void navigate(`/settings?${next.toString()}`, { replace: true })
  }, [location.search, onSettings, navigate])

  // The drawer closes on every navigation (a tree link was followed) and when the window gets wide again
  useEffect(() => {
    setDrawerOpen(false)
  }, [location.pathname, location.search])
  useEffect(() => {
    if (!drawer) setDrawerOpen(false)
  }, [drawer])

  const openDrawer = useCallback(() => {
    menuReturnRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    setDrawerOpen(true)
  }, [])
  const closeDrawer = useCallback(() => {
    setDrawerOpen(false)
    menuReturnRef.current?.focus()
  }, [])

  // Drawer open: focus into it, Esc closes it, Tab stays inside
  useEffect(() => {
    if (!drawer || !drawerOpen) return
    const panel = navRef.current
    const focusables = () =>
      [...(panel?.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), input:not([disabled])') ?? [])].filter((el) => el.offsetParent !== null)
    focusables()[0]?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        closeDrawer()
        return
      }
      if (event.key !== 'Tab') return
      const list = focusables()
      const first = list[0]
      const last = list[list.length - 1]
      if (!first || !last) return
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [drawer, drawerOpen, closeDrawer])

  // The page behind an open drawer does not scroll (scrollbars overlay, so nothing shifts)
  useEffect(() => {
    if (!drawer || !drawerOpen) return
    const root = document.documentElement
    const before = root.style.overflow
    root.style.overflow = 'hidden'
    return () => {
      root.style.overflow = before
    }
  }, [drawer, drawerOpen])

  const shell: ShellApi = useMemo(() => ({ tourOpen: tour.open, startTour: tour.start }), [tour.open, tour.start])

  return (
    <ShellContext.Provider value={shell}>
      <div className="pv-shell" data-rail={rail && !drawer ? 'true' : undefined} data-drawer={drawer ? 'true' : undefined}>
        <a href="#pv-main" className="pv-skip-link">
          {t('nav.skip')}
        </a>
        <ShellHeader
          ref={headerRef}
          theme={theme}
          onToggleTheme={onToggleTheme}
          onOpenSettings={(tab) => void navigate(settingsHref(tab))}
          onHelp={() => {
            if (!requestPageHints()) tour.start()
          }}
          drawer={drawer}
          drawerOpen={drawerOpen}
          onOpenDrawer={openDrawer}
          navId={NAV_ID}
        />
        <div className="pv-shell-body">
          <div
            ref={navRef}
            className="pv-shell-navwrap no-print"
            data-open={drawer ? (drawerOpen ? 'true' : 'false') : undefined}
            role={drawer && drawerOpen ? 'dialog' : undefined}
            aria-modal={drawer && drawerOpen ? true : undefined}
            aria-label={drawer && drawerOpen ? t('nav.drawerLabel') : undefined}
          >
            <ShellNav id={NAV_ID} rail={rail} onToggleRail={toggleRail} drawer={drawer} onCloseDrawer={closeDrawer} currentSlug={currentSlug} />
          </div>
          {drawer && drawerOpen && <div className="pv-shell-drawer-backdrop no-print" aria-hidden="true" onClick={closeDrawer} />}
          <main id="pv-main" className="pv-shell-main" tabIndex={-1}>
            <Outlet />
          </main>
        </div>
      </div>
      <OnboardingTour
        open={tour.open}
        onClose={tour.finish}
        onOpenSettings={(tab) => {
          tour.finish()
          void navigate(settingsHref(tab))
        }}
      />
      <OverlayScrollbars />
    </ShellContext.Provider>
  )
}
