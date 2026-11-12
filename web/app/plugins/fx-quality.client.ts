/**
 * Design exploration (prototype): decide how much visual effect this machine
 * gets, and mirror it on `<html data-fx>` so CSS can tone itself down too.
 *
 * `lite` when the visitor asked for less motion or less data, or when the
 * hardware hints at a weak machine (≤ 4 logical cores or ≤ 4 GB). `?fx=full`
 * or `?fx=lite` forces a level and remembers it for the session.
 */

export default defineNuxtPlugin(() => {
  const fx = useFx()

  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  const finePointer = window.matchMedia('(hover: hover) and (pointer: fine)').matches
  const nav = navigator as Navigator & {
    deviceMemory?: number
    connection?: { saveData?: boolean }
  }
  const weak = (nav.hardwareConcurrency ?? 8) <= 4 || (nav.deviceMemory ?? 8) <= 4 || nav.connection?.saveData === true
  const chromium = isChromium()

  let forcedLevel: FxLevel | null = null
  try {
    const fromQuery = new URLSearchParams(window.location.search).get('fx')
    if (fromQuery === 'full' || fromQuery === 'lite') {
      forcedLevel = fromQuery
      sessionStorage.setItem(FX_STORAGE_KEY, fromQuery)
    }
    else {
      const stored = sessionStorage.getItem(FX_STORAGE_KEY)
      if (stored === 'full' || stored === 'lite') forcedLevel = stored
    }
  }
  catch {
    // Storage blocked: detection alone decides.
  }

  const level: FxLevel = forcedLevel ?? (reducedMotion || weak ? 'lite' : 'full')
  fx.value = {
    level,
    forced: forcedLevel !== null,
    refraction: chromium && level === 'full',
    reducedMotion,
    finePointer,
  }

  const html = document.documentElement
  watchEffect(() => {
    html.dataset.fx = fx.value.level
    html.dataset.refraction = fx.value.refraction ? 'on' : 'off'
  })
})
