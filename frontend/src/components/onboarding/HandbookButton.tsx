import { IconBook2 } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { HandbookViewer } from '../HandbookViewer'
import { IconButton } from '../ui/IconButton'
import { useDialogParam } from '../../utils/useDialogParam'

// The handbook (PDF) inside the app, next to the "?" in every page header; its window lives in the URL (?dialog=handbook), so F5 keeps it open
export function HandbookButton() {
  const { t } = useTranslation('shell')
  const [open, setOpen] = useDialogParam('handbook')
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
