import { useCallback, useState } from 'react'
import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import type { AiUsageDto } from '../../api/types'
import { FloatingNotice } from '../ui/FloatingNotice'
import { useRefreshAiStatus } from './useAiStatus'

/**
 * The notice for a change of AI provider - never silent. An action reports what it learned (`report(result.ai)`); the sentence comes from
 * the server in the language of the UI ("Gemini quota used up – continuing with Claude."). Put `element` into the page's
 * <FloatingNoticeStack>. `refresh()` re-reads the provider states and belongs in the `finally` of every AI action.
 */
export function useAiSwitchNotice(): { element: ReactElement | null; report: (usage: AiUsageDto | null | undefined) => void; show: (text: string | null) => void; refresh: () => void } {
  const { t } = useTranslation('ai')
  const [text, setText] = useState<string | null>(null)
  const refresh = useRefreshAiStatus()

  const report = useCallback((usage: AiUsageDto | null | undefined) => {
    const last = usage?.switches[usage.switches.length - 1]
    if (last) setText(last.text)
  }, [])

  const element = text ? <FloatingNotice key="ai-switch" tone="info" title={t('notice.switchedTitle')} message={text} onClose={() => setText(null)} /> : null
  return { element, report, show: setText, refresh }
}
