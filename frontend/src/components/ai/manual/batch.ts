import type { ManualBatchChapterItem, ManualBatchItemCheckDto, ManualChapterSeed } from '../../../api/types'

// Pure helpers and the sessionStorage record of the batch mode ("all chapters at once"): stable chapter keys, the item list the server
// takes, the status of a row, and what survives closing the dialog.

/** From this many chapters to research the batch mode is the default; fewer: one by one. */
export const BATCH_DEFAULT_MIN = 3

export type ManualMode = 'batch' | 'single'

/** A chapter of the second step: `key` identifies it in the dialog, `batchKey` is the id ("K01") the prompt and the answers use. */
export interface BatchRow {
  key: string
  batchKey: string
  seed: ManualChapterSeed
}

export type RowStatus = 'pending' | 'ok' | 'warning' | 'error' | 'notApplicable' | 'saved'

export const ROW_STATUSES: RowStatus[] = ['pending', 'ok', 'warning', 'error', 'notApplicable', 'saved']

/** "K01", "K02", ... (two digits are enough for the 24 chapters the largest scope allows; more simply get longer). */
export const formatBatchKey = (n: number) => `K${String(n).padStart(2, '0')}`

/**
 * Keys for the given chapters: a chapter that had a key keeps it (so pasted answers stay valid when the player goes back and changes the
 * selection), a new one gets the next free number.
 */
export function assignBatchKeys(rowKeys: string[], previous: Record<string, string>): Record<string, string> {
  const used = Object.values(previous)
  let next = used.reduce((max, key) => Math.max(max, Number.parseInt(key.slice(1), 10) || 0), 0) + 1
  const result: Record<string, string> = { ...previous }
  for (const key of rowKeys) {
    if (!result[key]) result[key] = formatBatchKey(next++)
  }
  return result
}

export function toBatchItem(row: BatchRow): ManualBatchChapterItem {
  const { seed } = row
  return {
    key: row.batchKey,
    title: seed.title,
    focus: seed.focus,
    topicId: seed.topicId,
    sourceSituation: seed.sourceSituation,
    icon: seed.icon,
    tabId: seed.tabId,
    replace: seed.replace,
  }
}

export function statusOf(row: BatchRow, saved: Set<string>, item: ManualBatchItemCheckDto | undefined): RowStatus {
  if (saved.has(row.key)) return 'saved'
  switch (item?.state) {
    case 'ok':
      return 'ok'
    case 'warning':
      return 'warning'
    case 'error':
      return 'error'
    case 'notApplicable':
      return 'notApplicable'
    default:
      return 'pending'
  }
}

/** Statuses that "Save" takes along. */
export const isSaveable = (status: RowStatus) => status === 'ok' || status === 'warning' || status === 'notApplicable'

// ---- What survives closing the dialog (sessionStorage, best effort) ----

const PREFIX = 'pv_manual_batch_v1'

export interface StoredBatch {
  // The chapters of the second step (only the "plan" scope restores them; a fixed list comes from the page)
  rows: { key: string; seed: ManualChapterSeed }[]
  keys: Record<string, string>
  saved: string[]
  mode: ManualMode
}

// "plan" = chapters chosen from a blueprint answer, "failed" = the failed-chapter list, "chapter:<tabId>" = one chapter handed in on its own.
// Every list has its own scope so opening one never clears the pasted, unsaved text of another.
export type BatchScope = 'plan' | 'failed' | `chapter:${string}`

const recordKey = (slug: string, scope: BatchScope) => `${PREFIX}:${scope}:${slug}`
const textKey = (slug: string, scope: BatchScope) => `${PREFIX}:text:${scope}:${slug}`

function storage(): Storage | null {
  try {
    return window.sessionStorage
  } catch {
    return null
  }
}

export function readStoredBatch(slug: string, scope: BatchScope): StoredBatch | null {
  try {
    const raw = storage()?.getItem(recordKey(slug, scope))
    if (!raw) return null
    const value = JSON.parse(raw) as Partial<StoredBatch>
    if (!Array.isArray(value.rows) || !value.keys || typeof value.keys !== 'object') return null
    return {
      rows: value.rows.filter((row) => typeof row?.key === 'string' && row.seed && typeof row.seed.title === 'string'),
      keys: value.keys,
      saved: Array.isArray(value.saved) ? value.saved.filter((key) => typeof key === 'string') : [],
      mode: value.mode === 'single' ? 'single' : 'batch',
    }
  } catch {
    return null
  }
}

export function writeStoredBatch(slug: string, scope: BatchScope, value: StoredBatch): void {
  try {
    storage()?.setItem(recordKey(slug, scope), JSON.stringify(value))
  } catch {
    // Best effort: a full or blocked storage only means the state is not restored
  }
}

export function readStoredText(slug: string, scope: BatchScope): string {
  try {
    return storage()?.getItem(textKey(slug, scope)) ?? ''
  } catch {
    return ''
  }
}

export function writeStoredText(slug: string, scope: BatchScope, text: string): void {
  try {
    if (text) storage()?.setItem(textKey(slug, scope), text)
    else storage()?.removeItem(textKey(slug, scope))
  } catch {
    // Best effort, see above
  }
}

export function clearStoredBatch(slug: string, scope: BatchScope): void {
  try {
    storage()?.removeItem(recordKey(slug, scope))
    storage()?.removeItem(textKey(slug, scope))
  } catch {
    // Best effort, see above
  }
}

/** The identity of a chapter list: the stored text only belongs to the same list. */
export const rowsSignature = (rows: { key: string }[]) => rows.map((row) => row.key).join('|')
