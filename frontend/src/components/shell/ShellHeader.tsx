import { forwardRef } from 'react'
import { Link, useLocation } from 'react-router'
import { useTranslation } from 'react-i18next'
import { IconInfoCircle, IconMenu2, IconSettings } from '@tabler/icons-react'
import { useMediaQuery } from '../../utils/useMediaQuery'
import { CabinetButton } from '../cabinet/CabinetButton'
import { useBranding } from '../FooterParts'
import { LanguageMenu } from '../LanguageMenu'
import { HandbookButton } from '../onboarding/HandbookButton'
import { HelpButton } from '../onboarding/HelpButton'
import { UpdateButton } from '../updates/UpdateButton'
import { iconButtonClass, IconButton } from '../ui/IconButton'
import { ThemeIconButton } from '../ui/ThemeIconButton'
import { Tooltip } from '../ui/Tooltip'
import { UiScaleControl } from '../ui/UiScaleControl'
import { HeaderOverflowMenu } from './HeaderOverflowMenu'
import { HeaderUser } from './HeaderUser'

// Below these widths the less frequent entries move into the overflow menu, so every icon that stays keeps its 48 px target and nothing is cut:
// "compact" = display size, handbook and info; "tiny" (phones) = also colour scheme, help and the Steam user. Measured with the 48 px targets and the
// longest names (German), see styles/shell.css.
export const COMPACT_QUERY = '(max-width: 879px)'
export const TINY_QUERY = '(max-width: 639px)'

interface ShellHeaderProps {
  theme: string
  onToggleTheme: () => void
  onOpenSettings: (tab?: 'system') => void
  // The gear: the settings page on the tab the user left it on
  onOpenSettingsPage: () => void
  onHelp: () => void
  // Game pages only: the menu button that opens the game's tree as a drawer on a phone
  menu: { open: boolean; onOpen: () => void; navId: string } | null
}

/**
 * The one header of the app: logo and wordmark on the left (way home), on the right the Steam user and the tools - update (only when there is
 * one, always the first so nothing moves when it appears), trophy cabinet, display size, language, colour scheme, settings, help, handbook and
 * info. The same on every page; narrow windows fold the rarer ones into one overflow menu.
 */
export const ShellHeader = forwardRef<HTMLElement, ShellHeaderProps>(function ShellHeader(
  { theme, onToggleTheme, onOpenSettings, onOpenSettingsPage, onHelp, menu },
  ref
) {
  const { t } = useTranslation('shell')
  const location = useLocation()
  const { data: branding } = useBranding()
  const name = branding?.productName ?? 'PlatinVault'
  const compact = useMediaQuery(COMPACT_QUERY)
  const tiny = useMediaQuery(TINY_QUERY)
  const onSettings = location.pathname === '/settings'
  const onAbout = location.pathname === '/about'
  return (
    <header ref={ref} className="pv-shell-header no-print">
      {menu && (
        <Tooltip label={t('nav.menuOpen')}>
          <button
            type="button"
            className="pv-btn pv-icon-btn pv-shell-menu"
            aria-label={t('nav.menuOpen')}
            aria-expanded={menu.open}
            aria-controls={menu.navId}
            data-hint="game-nav"
            onClick={menu.onOpen}
          >
            <IconMenu2 size={22} />
          </button>
        </Tooltip>
      )}
      <Link to="/" className="pv-shell-brand" aria-label={t('nav.brandHome')}>
        <img src="/favicon.svg" alt="" className="pv-shell-logo" width={38} height={38} />
        <span className="pv-shell-word">{name}</span>
      </Link>
      {/* Left of the tools, so the account appearing (or the update icon) never moves an icon */}
      {!tiny && <HeaderUser avatarOnly={compact} />}
      <div className="pv-shell-utils" role="toolbar" aria-label={t('header.utilsToolbar')}>
        {/* Leftmost and invisible without news: the other icons never move when it appears */}
        <UpdateButton onOpenSettings={() => onOpenSettings('system')} />
        <CabinetButton />
        {!compact && <UiScaleControl />}
        <LanguageMenu />
        {!tiny && <ThemeIconButton theme={theme} onToggle={onToggleTheme} />}
        <IconButton
          data-hint="settings"
          label={t('header.settings.label')}
          description={t('header.settings.description')}
          aria-current={onSettings ? 'page' : undefined}
          onClick={onOpenSettingsPage}
        >
          <IconSettings size={20} />
        </IconButton>
        {!tiny && <HelpButton onClick={onHelp} />}
        {!compact && <HandbookButton />}
        {!compact && (
          <Tooltip label={t('header.info.label')} description={t('header.info.description')}>
            <Link to="/about" className={iconButtonClass()} aria-label={t('header.info.label')} aria-current={onAbout ? 'page' : undefined}>
              <IconInfoCircle size={20} />
            </Link>
          </Tooltip>
        )}
        {compact && <HeaderOverflowMenu tiny={tiny} theme={theme} onToggleTheme={onToggleTheme} onHelp={onHelp} />}
      </div>
    </header>
  )
})
