import { useState } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import type { UiDesign } from '../../api/types'
import { UI_DESIGNS, useUiDesign } from '../../utils/uiDesign'
import { errorText } from '../updates/updateResult'
import { FloatingNotice, FloatingNoticeStack } from '../ui/FloatingNotice'
import { ReservedText } from '../ui/ReservedText'
import { SettingsRow, SettingsTopic } from './SettingsParts'

/**
 * Settings > System > Design: "Leicht" (the calm default) or "Klassisch" (the earlier look) for this whole PlatinVault. Like the update
 * channel below it, the choice applies and is saved at once (no "Save" needed; the buttons stay enabled, so keyboard focus never jumps away); a failed save brings the previous design back and
 * says why in a floating notice over the page (a rare technical failure: nothing in the section moves for it, like the language menu). The
 * hint of the selected option has its room reserved, so switching moves nothing inside one design.
 */
export function DesignSection() {
  const { t } = useTranslation('settings')
  const { design, change } = useUiDesign()
  const [error, setError] = useState<string | null>(null)

  const choose = async (next: UiDesign) => {
    if (next === design) return
    setError(null)
    const result = await change(next)
    if (!result.ok) setError(t('design.saveFailed', { reason: errorText(result.error) }))
  }

  return (
    <div data-testid="design-section">
      <SettingsTopic>{t('design.title')}</SettingsTopic>
      <SettingsRow
        title={t('design.label')}
        titleId="ui-design-label"
        help={
          <>
            <ReservedText candidates={UI_DESIGNS.map((id) => t(`design.hints.${id}`))}>{t(`design.hints.${design}`)}</ReservedText>
            <p className="pv-set-design-scope">{t('design.scope')}</p>
          </>
        }
      >
        <div className="pv-segmented" role="group" aria-labelledby="ui-design-label" data-testid="ui-design">
          {UI_DESIGNS.map((id) => (
            <button key={id} type="button" aria-pressed={design === id} onClick={() => void choose(id)}>
              {t(`design.options.${id}`)}
            </button>
          ))}
        </div>
      </SettingsRow>
      {/* In the body: complete, copyable, laid over the content so nothing on the page moves */}
      {createPortal(
        <FloatingNoticeStack>
          {error && <FloatingNotice key="design-error" tone="error" title={t('design.saveFailedTitle')} message={error} onClose={() => setError(null)} />}
        </FloatingNoticeStack>,
        document.body
      )}
    </div>
  )
}
