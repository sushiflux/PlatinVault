import { useTranslation } from 'react-i18next'
import { LanguageMenu } from './LanguageMenu'
import { HandbookButton } from './onboarding/HandbookButton'
import { HelpButton } from './onboarding/HelpButton'
import type { SettingsTab } from './SettingsModal'
import { SettingsButton } from './SettingsButton'
import { UpdateButton } from './updates/UpdateButton'
import { ThemeIconButton } from './ui/ThemeIconButton'

interface HeaderUtilitiesProps {
  theme: string
  onToggleTheme: () => void
  // The tab is only given by the update popover (system); the gear opens the remembered one
  onOpenSettings: (tab?: SettingsTab) => void
  // Shows the hints of the current page again (the "?" button)
  onShowHints: () => void
}

// The five utilities in the top-right corner of every page header - language, colour scheme, settings, help, handbook - the same,
// in the same place, on every page. The names and tooltips of the buttons are texts of the "shell" namespace.
export function HeaderUtilities({ theme, onToggleTheme, onOpenSettings, onShowHints }: HeaderUtilitiesProps) {
  const { t } = useTranslation('shell')
  return (
    <div className="pv-hgroup pv-hutils" role="toolbar" aria-label={t('header.utilsToolbar')}>
      {/* Leftmost of a right-aligned group and invisible without news: the other icons never move when it appears */}
      <UpdateButton onOpenSettings={() => onOpenSettings('system')} />
      <LanguageMenu />
      <ThemeIconButton theme={theme} onToggle={onToggleTheme} />
      <SettingsButton onClick={() => onOpenSettings()} />
      <HelpButton onClick={onShowHints} />
      <HandbookButton />
    </div>
  )
}
