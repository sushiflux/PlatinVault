import { useMediaQuery } from '../../utils/useMediaQuery'
import { NavInfo } from './NavInfo'
import { NavInfoBlock } from './NavInfoBlock'
import './gamenav.css'

// Windows lower than this have no room for the info block under a useful number of chapters: the symbol with its card takes over
// (the drawer is the whole window high, the pinned panel is the window minus the bars)
const SHORT_PANEL = '(max-height: 700px)'
const SHORT_DRAWER = '(max-height: 560px)'

interface NavFootProps {
  // Phone drawer: the card of the symbol opens above it
  drawer: boolean
  // The desktop panel is collapsed to numbers: no room for text
  collapsed: boolean
  // An overlay of the foot (card, licence viewer) opened or closed
  onOpenChange?: (open: boolean) => void
}

/**
 * The foot of the side navigation, under a fine rule: the info block (name, version, "Made with <heart>", copyright, licences) in
 * the full panel and in the phone drawer; in the collapsed panel - and in a window too low for the block - one calm info symbol
 * that opens the same content as a floating card. The foot never scrolls with the chapters.
 */
export function NavFoot({ drawer, collapsed, onOpenChange }: NavFootProps) {
  const short = useMediaQuery(drawer ? SHORT_DRAWER : SHORT_PANEL)
  const symbol = collapsed || short
  return (
    <div className={symbol ? 'pv-gnav-foot' : 'pv-gnav-foot pv-gnav-foot--block'}>
      {symbol ? <NavInfo drawer={drawer} onOpenChange={onOpenChange} /> : <NavInfoBlock onOpenChange={onOpenChange} />}
    </div>
  )
}
