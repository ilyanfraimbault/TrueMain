/** `T` without its `readonly`s. */
export type Writable<T> = T extends readonly (infer U)[]
  ? Writable<U>[]
  : T extends object ? { -readonly [K in keyof T]: Writable<T[K]> } : T

/** A copy of a shell constant (`as const`) the app may then change. */
export function writable<T>(value: T): Writable<T> {
  return structuredClone(value) as unknown as Writable<T>
}
