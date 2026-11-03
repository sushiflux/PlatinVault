import { useEffect, useState } from 'react'
import { LicenseViewer } from '../LicenseViewer'
import { InfoBody } from './InfoBody'
import './gamenav.css'

/**
 * The info block at the foot of the full side navigation (and of the phone drawer): the same content as the card of the info symbol
 * (InfoBody), set into the nav instead of floating. "Lizenzen" opens the licence viewer; while it is up the nav is told (the
 * drawer pauses its own Esc and Tab handling).
 */
export function NavInfoBlock({ onOpenChange }: { onOpenChange?: (open: boolean) => void }) {
  const [viewerOpen, setViewerOpen] = useState(false)
  const [checkOpen, setCheckOpen] = useState(false)

  useEffect(() => {
    onOpenChange?.(viewerOpen || checkOpen)
  }, [viewerOpen, checkOpen, onOpenChange])

  // Gone from the nav (collapsed, a short window): the nav is no longer told about an overlay of this block
  useEffect(() => () => onOpenChange?.(false), [onOpenChange])

  return (
    <div className="pv-gnav-info-block">
      <InfoBody onOpenLicenses={() => setViewerOpen(true)} onVersionCheckChange={setCheckOpen} />
      <LicenseViewer open={viewerOpen} onClose={() => setViewerOpen(false)} />
    </div>
  )
}
