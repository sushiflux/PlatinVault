import { useId } from 'react'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconBulb, IconEye, IconEyeOff, IconLock, IconMapPin, IconPuzzle, IconRoute } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { GuideSectionDto } from '../../api/types'
import { StackedLabel } from '../ui/StackedLabel'
import { usePuzzleReveal } from './puzzleState'
import './guide.css'

interface PuzzleBlockProps {
  section: GuideSectionDto
  checkedSet: Set<string>
  onToggleCheck: (itemKey: string, checked: boolean) => Promise<void>
  // Where the puzzle sits in the guide (tab id + position): the key of its revealed state when it has no item id. Never the title - two puzzles can
  // share a name and a translation changes it.
  stateKey?: string
}

/**
 * A puzzle of the guide. What it is, where it is and what it needs are always visible; the way to solve it stays hidden until the reader asks:
 * "Show hint" reveals one hint at a time (vague to concrete), "Show solution" the complete steps, which can be hidden again. The buttons sit
 * above the revealed area and keep their size (all labels are drawn on top of each other), so only the block itself grows - smoothly - and nothing
 * else moves. A puzzle without steps says "solution not documented" instead of offering a solution button.
 */
export function PuzzleBlock({ section, checkedSet, onToggleCheck, stateKey }: PuzzleBlockProps) {
  const { t } = useTranslation('game')
  const reduceMotion = useReducedMotion()
  const baseId = useId()
  const puzzle = section.puzzle
  const hints = puzzle?.hints ?? []
  const steps = puzzle?.steps ?? []
  const hasSolution = puzzle?.documented !== false && steps.length > 0
  const name = section.title ?? puzzle?.location ?? ''
  const reveal = usePuzzleReveal(puzzle?.itemId || stateKey || name, hints.length, hasSolution)
  if (!puzzle) return null

  const hintsId = `${baseId}-hints`
  const solutionId = `${baseId}-solution`
  const itemId = puzzle.itemId || null
  const isDone = itemId !== null && checkedSet.has(itemId)
  const hintLabelIndex = reveal.hints === 0 ? 0 : reveal.hints < hints.length ? 1 : 2
  const motionProps = reduceMotion
    ? {}
    : {
        initial: { height: 0, opacity: 0 },
        animate: { height: 'auto', opacity: 1 },
        exit: { height: 0, opacity: 0 },
        transition: { duration: 0.22, ease: 'easeOut' as const },
      }

  const onHintClick = () => {
    if (reveal.hints >= hints.length) reveal.hideHints()
    else reveal.revealNextHint()
  }

  return (
    <article className={`pv-card pv-puzzle ${isDone ? 'pv-puzzle-done' : ''}`} data-puzzle={itemId ?? undefined}>
      <header className="pv-puzzle-head">
        <span className="pv-puzzle-icon" aria-hidden="true">
          <IconPuzzle size={20} />
        </span>
        <div className="pv-puzzle-titles">
          <span className="pv-puzzle-kicker">{t('puzzle.badge')}</span>
          {section.title && <h3 className="pv-puzzle-title">{section.title}</h3>}
        </div>
        <div className="pv-puzzle-head-end">
          {puzzle.missable && (
            <span className="pv-puzzle-missable">
              <IconLock size={13} aria-hidden="true" /> {t('puzzle.missable')}
            </span>
          )}
          {itemId && (
            <label className="pv-puzzle-done-label">
              <input
                type="checkbox"
                checked={isDone}
                onChange={(e) => void onToggleCheck(itemId, e.target.checked)}
                aria-label={t('puzzle.doneFor', { name })}
                className="h-5 w-5 rounded cursor-pointer"
                style={{ accentColor: 'var(--accent-emerald)' }}
              />
              <span>{t('puzzle.done')}</span>
            </label>
          )}
        </div>
      </header>

      {section.htmlContent && <p className="pv-puzzle-task">{section.htmlContent}</p>}

      {(puzzle.location || puzzle.requirements) && (
        <dl className="pv-puzzle-facts">
          {puzzle.location && (
            <div>
              <dt>
                <IconMapPin size={14} aria-hidden="true" /> {t('puzzle.where')}
              </dt>
              <dd>{puzzle.location}</dd>
            </div>
          )}
          {puzzle.requirements && (
            <div>
              <dt>
                <IconRoute size={14} aria-hidden="true" /> {t('puzzle.requirements')}
              </dt>
              <dd>{puzzle.requirements}</dd>
            </div>
          )}
        </dl>
      )}

      {(hasSolution || hints.length > 0) && (
        <>
          <div className="pv-puzzle-actions no-print">
            {hints.length > 0 && (
              <button
                type="button"
                className="pv-btn pv-btn-gold pv-puzzle-btn"
                onClick={onHintClick}
                aria-expanded={reveal.hints > 0}
                aria-controls={hintsId}
              >
                <IconBulb size={16} aria-hidden="true" />
                <StackedLabel active={hintLabelIndex} labels={[t('puzzle.hint.show'), t('puzzle.hint.next'), t('puzzle.hint.hide')]} />
              </button>
            )}
            {hasSolution && (
              <button
                type="button"
                className="pv-btn pv-btn-primary pv-puzzle-btn"
                onClick={reveal.toggleSolution}
                aria-expanded={reveal.solution}
                aria-controls={solutionId}
              >
                {reveal.solution ? <IconEyeOff size={16} aria-hidden="true" /> : <IconEye size={16} aria-hidden="true" />}
                <StackedLabel active={reveal.solution ? 1 : 0} labels={[t('puzzle.solution.show'), t('puzzle.solution.hide')]} />
              </button>
            )}
            <span className="pv-puzzle-self">{t('puzzle.selfHint')}</span>
          </div>

          <div id={hintsId} aria-live="polite">
            <AnimatePresence initial={false}>
              {reveal.hints > 0 && (
                <motion.div key="hints" className="pv-puzzle-reveal" {...motionProps}>
                  <div className="pv-puzzle-panel pv-puzzle-panel-hints" role="region" aria-label={t('puzzle.hint.title', { shown: reveal.hints, total: hints.length })}>
                    {hints.slice(0, reveal.hints).map((hint, i) => (
                      <p key={i} className="pv-puzzle-hint">
                        <span className="pv-puzzle-hint-no">{t('puzzle.hint.title', { shown: i + 1, total: hints.length })}</span>
                        {hint}
                      </p>
                    ))}
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>

          <div id={solutionId}>
            <AnimatePresence initial={false}>
              {hasSolution && reveal.solution && (
                <motion.div key="solution" className="pv-puzzle-reveal" {...motionProps}>
                  <div className="pv-puzzle-panel pv-puzzle-panel-solution" role="region" aria-label={t('puzzle.solution.title')}>
                    <h4 className="pv-puzzle-panel-title">{t('puzzle.solution.title')}</h4>
                    <ol className="pv-steps">
                      {steps.map((step, i) => (
                        <li key={i}>{step}</li>
                      ))}
                    </ol>
                    {puzzle.source && <p className="pv-puzzle-source">{t('puzzle.solution.source', { source: puzzle.source })}</p>}
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </>
      )}

      {!hasSolution && (
        <div className="pv-puzzle-undocumented" role="note">
          <IconLock size={16} aria-hidden="true" />
          <div>
            <strong>{t('puzzle.undocumented.title')}</strong>
            <span>{t('puzzle.undocumented.text')}</span>
          </div>
        </div>
      )}
    </article>
  )
}
