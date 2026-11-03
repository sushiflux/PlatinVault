import { IconHelp } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { IconButton } from '../ui/IconButton'

// "?" in the page headers: shows the hints of THIS page again (the full intro is a link inside the hint card).
export function HelpButton({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation('shell')
  return (
    <IconButton label={t('header.help.label')} description={t('header.help.description')} onClick={onClick}>
      <IconHelp size={18} />
    </IconButton>
  )
}
