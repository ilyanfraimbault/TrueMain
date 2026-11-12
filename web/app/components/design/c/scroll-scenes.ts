import { onBeforeUnmount, onMounted } from 'vue'

/**
 * Design exploration, variant C — the one scroll listener every scene shares.
 *
 * A scene registers a `measure` (layout reads, run on mount, on resize and when
 * the document changes height) and an `update` (run once per animation frame
 * while the page scrolls, with no layout reads — only cached numbers and CSS
 * variable writes). One passive `scroll` listener, one rAF, one
 * ResizeObserver, however many scenes are mounted.
 */

export interface ScrollFrame {
  /** `window.scrollY`. */
  y: number
  /** Viewport height (CSS px). */
  vh: number
  /** Viewport width without the scrollbar (CSS px). */
  vw: number
}

export interface ScrollScene {
  measure?: (frame: ScrollFrame) => void
  update: (frame: ScrollFrame) => void
}

const scenes = new Set<ScrollScene>()
const frame: ScrollFrame = { y: 0, vh: 0, vw: 0 }
let raf = 0
let measureQueued = false
let resizeObserver: ResizeObserver | null = null

function tick() {
  if (measureQueued) {
    measureQueued = false
    frame.vh = window.innerHeight
    frame.vw = document.documentElement.clientWidth
    frame.y = window.scrollY
    // Every measure before any update writes a variable.
    for (const scene of scenes) scene.measure?.(frame)
  }
  frame.y = window.scrollY
  for (const scene of scenes) scene.update(frame)
}

function onFrame() {
  raf = 0
  tick()
}

function schedule() {
  if (!raf) raf = requestAnimationFrame(onFrame)
}

// A scene mounting (or switching layout) measures in a microtask rather than
// the next frame: it lands before the first paint — a reload restored mid-page
// paints the right state at once — and does not wait on a frame a background
// tab never gets.
let flushQueued = false
function remeasure() {
  measureQueued = true
  if (flushQueued) return
  flushQueued = true
  queueMicrotask(() => {
    flushQueued = false
    tick()
  })
}

// Resizes measure on the next frame instead: a scene that sets its own height
// (the pinned gallery) would otherwise resize the body inside the
// ResizeObserver's own delivery and trip its loop error.
function remeasureNextFrame() {
  measureQueued = true
  schedule()
}

function start() {
  window.addEventListener('scroll', schedule, { passive: true })
  window.addEventListener('resize', remeasureNextFrame, { passive: true })
  resizeObserver = new ResizeObserver(remeasureNextFrame)
  resizeObserver.observe(document.body)
}

function stop() {
  window.removeEventListener('scroll', schedule)
  window.removeEventListener('resize', remeasureNextFrame)
  resizeObserver?.disconnect()
  resizeObserver = null
  cancelAnimationFrame(raf)
  raf = 0
}

/** Register a scene for the lifetime of the calling component. */
export function useScrollScene(scene: ScrollScene) {
  onMounted(() => {
    if (scenes.size === 0) start()
    scenes.add(scene)
    remeasure()
  })
  onBeforeUnmount(() => {
    scenes.delete(scene)
    if (scenes.size === 0) stop()
  })
  return { remeasure }
}

export function clamp01(value: number): number {
  return value < 0 ? 0 : value > 1 ? 1 : value
}

/** An element's top edge in document coordinates (a layout read — `measure` only). */
export function documentTop(el: HTMLElement): number {
  return el.getBoundingClientRect().top + window.scrollY
}

/**
 * Progress through a pinned section: 0 when its top reaches the viewport top,
 * 1 when its bottom reaches the viewport bottom (the sticky child's whole run).
 */
export function pinProgress(y: number, top: number, height: number, vh: number): number {
  const run = height - vh
  if (run <= 0) return y >= top ? 1 : 0
  return clamp01((y - top) / run)
}

/** Write a numeric CSS variable only when it moved, so idle frames cost nothing. */
export function writeVar(el: HTMLElement, name: string, value: number, cache: Record<string, number>, precision = 4) {
  const rounded = Number(value.toFixed(precision))
  if (cache[name] === rounded) return
  cache[name] = rounded
  el.style.setProperty(name, String(rounded))
}

/** Whether scroll-linked motion should run: wide enough, motion allowed. */
export function prefersStaticScroll(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}
