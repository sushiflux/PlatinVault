import { IconHeartFilled } from '@tabler/icons-react'
import { Trans, useTranslation } from 'react-i18next'

// "Mit ♥ gemacht" for a German UI, "Made with ♥" for every other language; the heart is always green.
// The wording comes from the texts of the active language (shell:madeWith), so it follows the language setting.
export function MadeWith({ size = 13 }: { size?: number }) {
  const { t } = useTranslation('shell')
  return (
    <span className="inline-flex items-center gap-1">
      <Trans
        t={t}
        i18nKey="madeWith.text"
        components={{
          heart: <IconHeartFilled size={size} style={{ color: 'var(--heart)' }} aria-label={t('madeWith.heart')} />,
        }}
      />
    </span>
  )
}
