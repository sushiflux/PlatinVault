import { forwardRef } from 'react'
import { Link } from 'react-router'
import { useTranslation } from 'react-i18next'
import { IconMenu2 } from '@tabler/icons-react'
import { useBranding } from '../FooterParts'
import { LanguageMenu } from '../LanguageMenu'
import { HandbookButton } from '../onboarding/HandbookButton'
import { HelpButton } from '../onboarding/HelpButton'
import { UpdateButton } from '../updates/UpdateButton'
import { ThemeIconButton } from '../ui/ThemeIconButton'
import { Tooltip } from '../ui/Tooltip'
import { UiScaleControl } from '../ui/UiScaleControl'

interface ShellHeaderProps {
  theme: string
  onToggleTheme: () => void
  onOpenSettings: (tab?: 'system') => void
  onHelp: () => void
  // Phone: the menu button that opens the tree drawer
  drawer: boolean
  drawerOpen: boolean
  onOpenDrawer: () => void
  navId: string
}

/**
 * The one header of the app: logo and wordmark on the left (way home), the utilities on the right - update (only when there is one),
 * UI size, language, colour scheme, handbook, help. The same on every page; the page's own tools live in its content.
 */
export const ShellHeader = forwardRef<HTMLElement, ShellHeaderProps>(function ShellHeader(
  { theme, onToggleTheme, onOpenSettings, onHelp, drawer, drawerOpen, onOpenDrawer, navId },
  ref
) {
  const { t } = useTranslation('shell')
  const { data: branding } = useBranding()
  const name = branding?.productName ?? 'PlatinVault'
  return (
    <header ref={ref} className="pv-shell-header no-print">
      {drawer && (
        <Tooltip label={t('nav.menuOpen')}>
          <button
            type="button"
            className="pv-btn pv-icon-btn pv-shell-menu"
            aria-label={t('nav.menuOpen')}
            aria-expanded={drawerOpen}
            aria-controls={navId}
            data-hint="game-nav"
            onClick={onOpenDrawer}
          >
            <IconMenu2 size={22} />
          </button>
        </Tooltip>
      )}
      <Link to="/" className="pv-shell-brand" aria-label={t('nav.brandHome')}>
        <img src="/favicon.svg" alt="" className="pv-shell-logo" width={38} height={38} />
        <span className="pv-shell-word">{name}</span>
      </Link>
      <div className="pv-shell-utils" role="toolbar" aria-label={t('header.utilsToolbar')}>
        {/* Leftmost and invisible without news: the other icons never move when it appears */}
        <UpdateButton onOpenSettings={() => onOpenSettings('system')} />
        <UiScaleControl />
        <LanguageMenu />
        <ThemeIconButton theme={theme} onToggle={onToggleTheme} />
        <HandbookButton />
        <HelpButton onClick={onHelp} />
      </div>
    </header>
  )
})
