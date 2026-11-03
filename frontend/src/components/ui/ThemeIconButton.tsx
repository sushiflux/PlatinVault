import { IconMoon, IconSun } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { IconButton } from './IconButton'

// Theme switch for the page headers: the icon shows the active scheme, the tooltip names it and the other one.
// Two schemes only: the button flips between them
export function ThemeIconButton({ theme, onToggle }: { theme: string; onToggle: () => void }) {
  const { t } = useTranslation('shell')
  const key = theme === 'dark' ? 'dark' : 'light'
  const next = key === 'dark' ? 'light' : 'dark'
  const Icon = key === 'dark' ? IconMoon : IconSun
  return (
    <IconButton
      label={t('header.theme.label', { current: t(`header.theme.${key}`) })}
      description={t('header.theme.description', { next: t(`header.theme.${next}`) })}
      onClick={onToggle}
    >
      <Icon size={18} />
    </IconButton>
  )
}
