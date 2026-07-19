/**
 * First string of a route/query value Vue Router may hand over as
 * `string | string[]` (catch-all and repeated params arrive as arrays).
 * Non-string leftovers (missing param, LocationQueryValue null) resolve to
 * `undefined`.
 */
export function firstParamValue(value: unknown): string | undefined {
  const raw = Array.isArray(value) ? value[0] : value
  return typeof raw === 'string' ? raw : undefined
}

/**
 * `firstParamValue` for path params, where callers want the empty string as
 * the "missing" sentinel (e.g. nameTag parsing feeds it straight into string
 * splits).
 */
export function parseRouteParam(param: string | string[] | undefined): string {
  return firstParamValue(param) ?? ''
}
