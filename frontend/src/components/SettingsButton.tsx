import { IconSettings } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { IconButton } from './ui/IconButton'

// Gear in the top-right corner of every page header (next to "?"): opens the settings from anywhere.
export function SettingsButton({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation('shell')
  return (
    <IconButton data-hint="settings" label={t('header.settings.label')} description={t('header.settings.description')} onClick={onClick}>
      <IconSettings size={18} />
    </IconButton>
  )
}
