/**
 * The shape a translation namespace has in another language: the same keys as the German source file, any text.
 * Used with `satisfies` in src/i18n/en/*.ts, so a missing or an extra key is a TypeScript error (and a vitest case).
 */
export type Translation<T> = { [K in keyof T]: T[K] extends string ? string : Translation<T[K]> }
