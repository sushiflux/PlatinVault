import { useTranslation } from 'react-i18next'
import type { GameDetailDto, ManualTrophyListDto } from '../../api/types'
import { hasTrophies, platformLabelKey, platformOf, trophyKind } from '../../utils/platforms'
import { PlatformBadge } from '../platform/PlatformBadge'
import { TierIcon } from '../ui/TierIcon'
import '../../styles/gameframe.css'

/**
 * The status line under the name of a game added by hand (instead of the Steam status): the platform chip, what the list is called on that
 * platform ("PlayStation 5 trophies" / "Xbox achievements" / "guide only" for the Switch), that the ticks are set by hand, and the platinum
 * once it is reached. One line high from the first frame (rigid layout): the chip and the platform text are always there.
 */
export function ManualHeadStatus({ game, list }: { game: Pick<GameDetailDto, 'platform' | 'platformVariant'>; list?: ManualTrophyListDto }) {
  const { t } = useTranslation(['game', 'common'])
  const platform = platformOf(game)
  const variant = game.platformVariant ?? null
  const name = t(`common:platforms.${platformLabelKey(platform, variant)}`)
  const label = !hasTrophies(platform) ? t('game:manualHead.guideOnly', { platform: name }) : t(`game:manualHead.${trophyKind(platform)}`, { platform: name })
  return (
    <div className="pv-gstatus" data-testid="manual-head-status">
      <PlatformBadge platform={platform} variant={variant} size="md" />
      <span>{label}</span>
      {hasTrophies(platform) && <span>{t('game:manualHead.byHand')}</span>}
      {list?.platinumAt && (
        <span className="pv-gstatus-plat">
          <TierIcon tier="Platinum" size={15} /> {t('game:manualHead.platinum')}
        </span>
      )}
    </div>
  )
}
