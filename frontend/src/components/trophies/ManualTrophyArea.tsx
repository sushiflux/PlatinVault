import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconBook2, IconChevronRight, IconListDetails, IconLoader2, IconRefresh, IconSparkles } from '@tabler/icons-react'
import type { GameDetailDto, ManualTrophyListDto } from '../../api/types'
import { useFormat } from '../../i18n/format'
import { platformLabelKey, platformOf, trophyKind } from '../../utils/platforms'
import { AiProgressLines } from '../ai/AiProgress'
import { AiRunButton } from '../ai/AiRunButton'
import { rememberedHow, type AiHow } from '../ai/aiProviders'
import type { AiProgressView } from '../ai/useAiProgress'
import { ErrorBlock } from '../ui/ErrorBlock'
import { ALL_SORTS, TrophyListView, type TrophySort } from './TrophyListView'
import { hasRarityShares, tierFiltersOf, trophyTipsOf } from './trophyList'
import type { ManualTrophies } from './useManualTrophies'
import '../../styles/live.css'

// The sorts of a list that states no rarity: only the order of the game, what was ticked last, and the alphabet
const SORTS_WITHOUT_SHARES: readonly TrophySort[] = ['default', 'unlocked_recent', 'name_asc']

interface ManualTrophyAreaProps {
  game: GameDetailDto
  trophies: ManualTrophies
  // The AI research of the list runs (kind "trophies"); its progress is shown here while there is no list yet
  researching: boolean
  progress: AiProgressView | null
  // The research button was pressed (oneOff may be 'manual': the player's own AI subscription)
  onResearch: (oneOff?: AiHow) => void
  // "Zum Guide": the second step after the list
  onOpenGuide: () => void
}

// Only addresses a browser may open: a source of the researched list is data, never trusted to be a link
const isWebAddress = (url: string) => /^https?:\/\//i.test(url.trim())

/**
 * The trophy tab of a game added by hand: no list yet = the research ("Trophäenliste recherchieren", with the AI choice and the way with the
 * player's own subscription) and the recommended order (list first, guide second); a list = where it comes from (date, sources, the hint that it may
 * be wrong), the quiet "neu recherchieren", and the same filters and four views as the live list of a Steam game - with the check that ticks a
 * trophy by hand. The facts of a game without any list (Nintendo) are not here: that platform has no trophy tab.
 */
export function ManualTrophyArea({ game, trophies, researching, progress, onResearch, onOpenGuide }: ManualTrophyAreaProps) {
  const { t } = useTranslation(['game', 'live', 'common'])
  const fmt = useFormat()
  const { list, loading, error } = trophies
  // The AI picked for this one research ("Mit Claude"); the setting itself stays as it is. Starts on "Mein KI-Abo" when that was the way taken last time
  const [oneOff, setOneOff] = useState<AiHow | undefined>(rememberedHow)
  const platform = platformOf(game)
  const kind = trophyKind(platform)

  const tips = useMemo(() => trophyTipsOf(list), [list])

  if (loading && !list) return <div className="pv-surface pv-live-empty">{t('live:tracker.loading')}</div>
  if (!list) {
    return error ? <ErrorBlock title={t('game:manualTrophies.loadFailed')} message={error instanceof Error ? error.message : String(error)} defaultOpen /> : null
  }

  const researched = list.researchedAt !== null && list.achievements.length > 0
  if (!researched) {
    const platformName = t(`common:platforms.${platformLabelKey(platform, game.platformVariant)}`)
    const runningTitle = (running: boolean) => (running ? t(`game:manualTrophies.running.${kind}`) : t(`game:manualTrophies.empty.title.${kind}`, { title: game.title }))
    const emptyText = t(`game:manualTrophies.empty.text.${kind}`, { platform: platformName })
    return (
      <section className="pv-surface pv-tstate" data-state={researching ? 'running' : 'empty'} aria-busy={researching || undefined}>
        <span className="pv-tstate-ic" aria-hidden="true">
          {researching ? <IconLoader2 size={22} className="motion-safe:animate-spin" /> : <IconListDetails size={22} />}
        </span>
        {/* The card is the same height empty and while the research runs: heading and body lay both of their texts on top of each other (the one
            that is not shown as an invisible copy), so starting the research moves nothing below the card */}
        <div className="pv-reserve pv-tstate-swap">
          <h2>{runningTitle(researching)}</h2>
          <span className="pv-reserve-copy pv-tstate-h" aria-hidden="true" data-text={runningTitle(!researching)} />
        </div>
        <div className="pv-reserve pv-tstate-swap pv-tstate-body">
          {researching ? (
            <>
              <div className="pv-tstate-progress" role="status">
                <AiProgressLines view={progress} />
              </div>
              <span className="pv-reserve-copy pv-tstate-p" aria-hidden="true" data-text={emptyText} />
            </>
          ) : (
            <>
              <p>{emptyText}</p>
              <div className="pv-reserve-copy pv-tstate-progress" aria-hidden="true">
                <AiProgressLines view={null} ghost />
              </div>
            </>
          )}
        </div>
        <AiRunButton
          manual
          tone="pv-btn-primary"
          busy={researching}
          oneOff={oneOff}
          onOneOffChange={setOneOff}
          onClick={() => onResearch(oneOff)}
          icon={<IconSparkles size={16} />}
          label={<span>{t(`game:manualTrophies.research.${kind}`)}</span>}
        />
        <p className="pv-tstate-small">{t('game:manualTrophies.backgroundHint')}</p>
        <ol className="pv-tsteps">
          <li data-now="">
            <span className="pv-tsteps-n" aria-hidden="true">
              1
            </span>
            <span className="pv-tsteps-t">
              <b>{t(`game:manualTrophies.steps.listTitle.${kind}`)}</b>
              <span>{t('game:manualTrophies.steps.listText', { platform: platformName })}</span>
            </span>
            <span className="pv-badge pv-badge-a">{t('game:manualTrophies.steps.now')}</span>
          </li>
          <li>
            <span className="pv-tsteps-n" aria-hidden="true">
              2
            </span>
            <span className="pv-tsteps-t">
              <b>{t('game:manualTrophies.steps.guideTitle')}</b>
              <span>{t('game:manualTrophies.steps.guideText')}</span>
            </span>
            <button type="button" className="pv-btn-text" onClick={onOpenGuide}>
              <IconBook2 size={16} aria-hidden="true" />
              {t('game:manualTrophies.steps.toGuide')}
              <IconChevronRight size={16} aria-hidden="true" />
            </button>
          </li>
        </ol>
      </section>
    )
  }

  return (
    <TrophyListView
      data={list}
      loading={false}
      tips={tips}
      notesScope={{ gameId: list.gameId }}
      check={trophies.checkFor}
      tierFilters={tierFiltersOf(list)}
      sorts={hasRarityShares(list) ? ALL_SORTS : SORTS_WITHOUT_SHARES}
      notice={
        <div className="pv-tsrc pv-accent-left" data-testid="trophy-source">
          <div className="pv-tsrc-t">
            <b>{t('game:manualTrophies.source.by')}</b> {t('game:manualTrophies.source.on', { date: fmt.date(list.researchedAt!) })}{' '}
            <Sources list={list} />
            <small>{t(`game:manualTrophies.source.mayErr.${kind}`)}</small>
            {list.translation?.state === 'missing' && list.language !== fmt.language && (
              <small data-testid="trophy-language-hint">{t('game:manualTrophies.source.language', { language: t(`game:contentLanguage.${list.language === 'en' ? 'en' : 'de'}`) })}</small>
            )}
          </div>
          <AiRunButton
            manual
            quiet
            busy={researching}
            oneOff={oneOff}
            onOneOffChange={setOneOff}
            onClick={() => onResearch(oneOff)}
            icon={<IconRefresh size={16} />}
            label={<span>{t(`game:manualTrophies.redo.${kind}`)}</span>}
          />
        </div>
      }
    />
  )
}

// "Quellen: A, B, C." - or that the research named none
function Sources({ list }: { list: ManualTrophyListDto }) {
  const { t } = useTranslation('game')
  const links = list.sources.filter((source) => isWebAddress(source.url))
  if (links.length === 0) return <span>{t('manualTrophies.source.none')}</span>
  return (
    <span>
      {t('manualTrophies.source.sources')}{' '}
      {links.map((source, index) => (
        <span key={source.url}>
          <a href={source.url} target="_blank" rel="noopener noreferrer">
            {source.title.trim() || source.url}
          </a>
          {index < links.length - 1 ? ', ' : '.'}
        </span>
      ))}
    </span>
  )
}
