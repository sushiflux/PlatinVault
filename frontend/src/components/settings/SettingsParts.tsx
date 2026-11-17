import type { ReactNode } from 'react'
import { Switch } from '../ui/Switch'
import '../../styles/settings.css'

/** The one light card of a settings tab: hairline rows inside (see styles/settings.css). */
export function SettingsCard({ children, className = '', ...rest }: { children: ReactNode; className?: string } & React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div {...rest} className={`pv-surface pv-set-card ${className}`.trim()}>
      {children}
    </div>
  )
}

/** A quiet small-caps heading that starts a topic inside a card. */
export function SettingsTopic({ children, id }: { children: ReactNode; id?: string }) {
  return (
    <h3 id={id} className="pv-eyebrow pv-set-topic">
      {children}
    </h3>
  )
}

interface SettingsRowProps {
  /** Name of the setting (also the accessible label of the control when `labelFor` is set). */
  title: ReactNode
  /** One quiet line below the name. */
  help?: ReactNode
  /** The control; it sits at the right (below the text on a narrow window or with `wide`). */
  children?: ReactNode
  /** The control takes the whole row. */
  wide?: boolean
  /** Id of the control: the title becomes its <label>. */
  labelFor?: string
  /** Id of the title element (for aria-labelledby of a group). */
  titleId?: string
  className?: string
  'data-testid'?: string
}

/** A row of a settings card: name + help on the left, the control on the right, a hairline below. */
export function SettingsRow({ title, help, children, wide = false, labelFor, titleId, className = '', ...rest }: SettingsRowProps) {
  const Title = labelFor ? 'label' : 'div'
  return (
    <div className={`pv-set-row ${wide ? 'pv-set-row--wide' : ''} ${className}`.trim()} data-testid={rest['data-testid']}>
      <div className="min-w-0">
        <Title id={titleId} {...(labelFor ? { htmlFor: labelFor } : {})} className="pv-set-row__title block">
          {title}
        </Title>
        {help && <div className="pv-set-row__help">{help}</div>}
      </div>
      {children !== undefined && children !== null && <div className="pv-set-row__ctl">{children}</div>}
    </div>
  )
}

interface SettingsToggleProps {
  checked: boolean
  onChange: (next: boolean) => void
  title: ReactNode
  help?: ReactNode
  disabled?: boolean
  /** Extra content below the help (a reserved status line, for example). */
  children?: ReactNode
}

/** An on/off row: the shared Switch (red off, green on) is the whole row, the name is its accessible name, the hint sits below it. */
export function SettingsToggle({ checked, onChange, title, help, disabled, children }: SettingsToggleProps) {
  return (
    <div className="pv-set-row pv-set-row--toggle">
      <Switch checked={checked} onChange={onChange} disabled={disabled} className="pv-set-toggle">
        {title}
      </Switch>
      {help && <div className="pv-set-row__help">{help}</div>}
      {children}
    </div>
  )
}

/** A hairline panel with a thin crystal line on the left: "where do I get the key" instructions. */
export function SettingsGuide({ title, children }: { title?: ReactNode; children: ReactNode }) {
  return (
    <div className="pv-set-guide">
      {title && <h4>{title}</h4>}
      {children}
    </div>
  )
}
