import { NavInfo } from './NavInfo'
import './gamenav.css'

interface NavFootProps {
  // Phone drawer: the card of the symbol opens above it
  drawer: boolean
  // Accepted for callers that still pass it; the foot is always the compact symbol row
  collapsed?: boolean
  // An overlay of the foot (card, version popover, what's new, licence viewer) opened or closed
  onOpenChange?: (open: boolean) => void
}

/**
 * The foot of the side navigation, under a fine rule: always one slim row with the calm info symbol (`NavInfo`) that opens the
 * floating card (logo, name, version button with "Version & Updates" and "What's new", "Made with <heart>", copyright, licences)
 * - in the expanded panel, the collapsed panel and the phone drawer alike, so the chapter list gets the full height. The foot
 * never scrolls with the chapters.
 */
export function NavFoot({ drawer, onOpenChange }: NavFootProps) {
  return (
    <div className="pv-gnav-foot">
      <NavInfo drawer={drawer} onOpenChange={onOpenChange} />
    </div>
  )
}
