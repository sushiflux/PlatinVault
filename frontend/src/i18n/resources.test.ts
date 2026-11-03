import { describe, expect, it } from 'vitest'
import { de, en, NAMESPACES } from './resources'

// Flattens a namespace into "path.to.key" -> text
function flatten(node: unknown, prefix = ''): Record<string, string> {
  const result: Record<string, string> = {}
  for (const [key, value] of Object.entries(node as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (typeof value === 'string') result[path] = value
    else Object.assign(result, flatten(value, path))
  }
  return result
}

// {{variables}} and <tags> of a text, sorted: both languages must use the same ones, or one of them breaks at runtime
function variablesOf(text: string): string[] {
  return [...text.matchAll(/\{\{\s*([a-zA-Z0-9_]+)/g)].map((m) => m[1]!).sort()
}
function tagsOf(text: string): string[] {
  return [...text.matchAll(/<\/?\s*([a-zA-Z][a-zA-Z0-9]*)\s*\/?>/g)].map((m) => m[1]!).sort()
}

describe.each(NAMESPACES)('namespace "%s"', (namespace) => {
  const german = flatten(de[namespace])
  const english = flatten(en[namespace])

  it('has the same keys in German and English', () => {
    expect(Object.keys(english).sort()).toEqual(Object.keys(german).sort())
  })

  it('has no empty texts', () => {
    expect(Object.entries(german).filter(([, text]) => text.trim() === '')).toEqual([])
    expect(Object.entries(english).filter(([, text]) => text.trim() === '')).toEqual([])
  })

  it('uses the same {{variables}} and <tags> in both languages', () => {
    const mismatches = Object.keys(german).flatMap((key) => {
      const g = german[key]!
      const e = english[key]
      if (e === undefined) return []
      const problems: string[] = []
      if (variablesOf(g).join() !== variablesOf(e).join()) problems.push(`${key}: variables ${variablesOf(g)} vs ${variablesOf(e)}`)
      if (tagsOf(g).join() !== tagsOf(e).join()) problems.push(`${key}: tags ${tagsOf(g)} vs ${tagsOf(e)}`)
      return problems
    })
    expect(mismatches).toEqual([])
  })

  it('gives every plural key both forms', () => {
    // German and English both have exactly "one" and "other"
    const keys = Object.keys(german)
    const missing = keys.flatMap((key) => {
      const one = key.endsWith('_one') ? key.slice(0, -4) : null
      const other = key.endsWith('_other') ? key.slice(0, -6) : null
      if (one && !keys.includes(`${one}_other`)) return [`${key} has no _other`]
      if (other && !keys.includes(`${other}_one`)) return [`${key} has no _one`]
      return []
    })
    expect(missing).toEqual([])
  })
})
