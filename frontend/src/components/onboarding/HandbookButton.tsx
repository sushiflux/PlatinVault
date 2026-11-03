import { useState } from 'react'
import { IconBook2 } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { HandbookViewer } from '../HandbookViewer'
import { IconButton } from '../ui/IconButton'

// The handbook (PDF) inside the app, next to the "?" in every page header
export function HandbookButton() {
  const { t } = useTranslation('shell')
  const [open, setOpen] = useState(false)
  return (
    <>
      <IconButton
        label={t('header.handbook.label')}
        description={t('header.handbook.description')}
        data-hint="header-handbook"
        aria-haspopup="dialog"
        onClick={() => setOpen(true)}
      >
        <IconBook2 size={18} />
      </IconButton>
      <HandbookViewer open={open} onClose={() => setOpen(false)} />
    </>
  )
}
