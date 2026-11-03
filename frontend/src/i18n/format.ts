import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { currentLanguage, languageInfo, parseUiLanguage } from './index'
import type { UiLanguage } from './index'

// Dates, times, numbers, percentages and relative times in the language of the app - one module, so no component picks
// its own locale. The Intl locale of each language comes from languages.json: German de-DE, English en-GB (not en-US) because
// it writes the day first and counts the day in 24 hours by default, exactly like the German formats do - the same date reads
// the same order in both languages and nobody has to guess 03/04. Numbers are identical in en-GB and en-US ("1,234.5").

export function localeFor(language: UiLanguage): string {
  return languageInfo(language).locale
}

/** The locale of the active app language. */
export function currentLocale(): string {
  return localeFor(currentLanguage())
}

// Intl formatters are expensive to build; one per locale and option set is enough
const dateFormatters = new Map<string, Intl.DateTimeFormat>()
const numberFormatters = new Map<string, Intl.NumberFormat>()
const relativeFormatters = new Map<string, Intl.RelativeTimeFormat>()

function dateFormatter(locale: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = `${locale}|${JSON.stringify(options)}`
  let formatter = dateFormatters.get(key)
  if (!formatter) {
    formatter = new Intl.DateTimeFormat(locale, options)
    dateFormatters.set(key, formatter)
  }
  return formatter
}

function numberFormatter(locale: string, options: Intl.NumberFormatOptions): Intl.NumberFormat {
  const key = `${locale}|${JSON.stringify(options)}`
  let formatter = numberFormatters.get(key)
  if (!formatter) {
    formatter = new Intl.NumberFormat(locale, options)
    numberFormatters.set(key, formatter)
  }
  return formatter
}

export type DateStyle =
  /** 31.10.2026 / 31/10/2026 */
  | 'date'
  /** 31. Oktober 2026 / 31 October 2026 */
  | 'dateLong'
  /** 31. Okt. / 31 Oct */
  | 'dayMonth'
  /** Okt. 26 / Oct 26 */
  | 'monthYear'
  /** Okt. / Oct */
  | 'month'
  /** Sa., 31. Okt. 2026 / Sat, 31 Oct 2026 */
  | 'dateWithWeekday'
  /** 14:05 */
  | 'time'
  /** 14:05:33 */
  | 'timeSeconds'
  /** 31.10.2026, 14:05 / 31/10/2026, 14:05 */
  | 'dateTime'
  /** 31.10.2026, 14:05:33 / 31/10/2026, 14:05:33 */
  | 'dateTimeSeconds'
  /** 31. Okt. 2026, 14:05 / 31 Oct 2026, 14:05 */
  | 'dateTimeShort'

const DATE_STYLES: Record<DateStyle, Intl.DateTimeFormatOptions> = {
  date: { day: '2-digit', month: '2-digit', year: 'numeric' },
  dateLong: { day: 'numeric', month: 'long', year: 'numeric' },
  dayMonth: { day: 'numeric', month: 'short' },
  monthYear: { month: 'short', year: '2-digit' },
  month: { month: 'short' },
  dateWithWeekday: { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' },
  time: { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' },
  timeSeconds: { hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' },
  dateTime: { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' },
  dateTimeSeconds: {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  },
  dateTimeShort: { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' },
}

export type DateInput = Date | string | number | null | undefined

function toDate(value: DateInput): Date | null {
  if (value === null || value === undefined || value === '') return null
  const date = value instanceof Date ? value : new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

interface FormatContext {
  locale: string
  /** Fixed time zone (tests); default = the viewer's own */
  timeZone?: string
}

/** A date or time in the language of the app. Missing input gives '', unreadable text is returned unchanged. */
export function formatDate(value: DateInput, style: DateStyle = 'date', context: Partial<FormatContext> = {}): string {
  const date = toDate(value)
  if (!date) return typeof value === 'string' ? value : ''
  const options = context.timeZone ? { ...DATE_STYLES[style], timeZone: context.timeZone } : DATE_STYLES[style]
  return dateFormatter(context.locale ?? currentLocale(), options).format(date)
}

/** A whole number or a number with at most `maxDecimals` decimals, grouped for the language (1.234,5 / 1,234.5). */
export function formatNumber(value: number, maxDecimals = 0, context: Partial<FormatContext> = {}): string {
  return numberFormatter(context.locale ?? currentLocale(), { maximumFractionDigits: maxDecimals }).format(value)
}

/** A percentage given as 0-100 (Steam's global percent, a progress value): "45 %" in German, "45%" in English. */
export function formatPercent(value: number, maxDecimals = 0, context: Partial<FormatContext> = {}): string {
  return numberFormatter(context.locale ?? currentLocale(), { style: 'percent', maximumFractionDigits: maxDecimals }).format(value / 100)
}

/**
 * A file size the way Steam writes it in its own dialogs: steps of 1024 with the labels KB / MB / GB / TB ("46,52 GB" is 46.52 x 2^30
 * bytes), so a size shown here matches what the Steam client shows for the same game. Gigabytes with one decimal, megabytes whole.
 * The number and the unit are joined by a no-break space.
 */
export function formatBytes(bytes: number, context: Partial<FormatContext> = {}): string {
  const KB = 1024
  const MB = KB * 1024
  const GB = MB * 1024
  const TB = GB * 1024
  const [value, unit, decimals] =
    bytes >= TB ? [bytes / TB, 'TB', 2] : bytes >= GB ? [bytes / GB, 'GB', 1] : bytes >= MB ? [bytes / MB, 'MB', 0] : [bytes / KB, 'KB', 0]
  return `${formatNumber(value, decimals, context)} ${unit}`
}

const RELATIVE_UNITS: { unit: Intl.RelativeTimeFormatUnit; ms: number }[] = [
  { unit: 'year', ms: 365 * 24 * 3600 * 1000 },
  { unit: 'month', ms: 30 * 24 * 3600 * 1000 },
  { unit: 'week', ms: 7 * 24 * 3600 * 1000 },
  { unit: 'day', ms: 24 * 3600 * 1000 },
  { unit: 'hour', ms: 3600 * 1000 },
  { unit: 'minute', ms: 60 * 1000 },
]

/** "vor 3 Stunden" / "3 hours ago", "gestern" / "yesterday", "in 2 Tagen" / "in 2 days". */
export function formatRelativeTime(value: DateInput, now: Date | number = Date.now(), context: Partial<FormatContext> = {}): string {
  const date = toDate(value)
  if (!date) return typeof value === 'string' ? value : ''
  const locale = context.locale ?? currentLocale()
  let formatter = relativeFormatters.get(locale)
  if (!formatter) {
    formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
    relativeFormatters.set(locale, formatter)
  }
  const diff = date.getTime() - (typeof now === 'number' ? now : now.getTime())
  const unit = RELATIVE_UNITS.find((u) => Math.abs(diff) >= u.ms)
  if (!unit) return formatter.format(0, 'second')
  return formatter.format(Math.round(diff / unit.ms), unit.unit)
}

/**
 * The formatters bound to the active language. Using the hook (instead of the plain functions) makes the component render
 * again when the language is switched, so its dates and numbers change with the texts.
 */
export function useFormat() {
  const { i18n } = useTranslation()
  const language = parseUiLanguage(i18n.resolvedLanguage) ?? currentLanguage()
  return useMemo(() => {
    const context = { locale: localeFor(language) }
    return {
      language: language as UiLanguage,
      locale: context.locale,
      date: (value: DateInput, style: DateStyle = 'date') => formatDate(value, style, context),
      number: (value: number, maxDecimals = 0) => formatNumber(value, maxDecimals, context),
      percent: (value: number, maxDecimals = 0) => formatPercent(value, maxDecimals, context),
      bytes: (value: number) => formatBytes(value, context),
      relative: (value: DateInput, now?: Date | number) => formatRelativeTime(value, now, context),
    }
  }, [language])
}
