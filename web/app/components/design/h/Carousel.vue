<script setup lang="ts">
import type { ChampionOverviewRowReadModel as ChampionOverviewRow } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { championLoadingUrlFromIcon } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#shared/utils/positions'
import { createRingEngine, wrapOffset } from './ring-engine'
import type { RingEngine } from './ring-engine'

// Variant H — the patch's most played and banned champions on a ring of
// loading-art cards (CSS 3D). The card in front is enlarged and lit, and its
// figures sit on a liquid-glass plate that bends the art under it. The ring
// turns on a drag, a horizontal wheel/trackpad swipe, the arrow keys and the
// two glass buttons; it throws, then settles on a card. A click on the front
// card opens the champion at that lane; a click on a side card brings it round.
//
// Each card carries its own `perspective()` transform from a shared origin
// (the stage centre) rather than living in a `preserve-3d` scene: same
// picture, but the stacking is an explicit z-index and the glass plate keeps a
// plain backdrop to refract. On a phone the ring folds into a stacked deck you
// swipe through. Vertical page scrolling is never taken: `touch-action: pan-y`,
// and only horizontal-dominant wheel deltas are claimed.
const props = defineProps<{
  rows: ChampionOverviewRow[]
  championsById: Map<number, ChampionStaticListItem>
  pending: boolean
}>()

const { pathFor } = useChampionSlugs()
const loadingArt = useCanonicalLoadingArt()
const fx = useFx()

const cards = computed(() => props.rows.map((row) => {
  const champion = props.championsById.get(row.championId)
  const lane = POSITION_BY_VALUE.get(row.position)
  const name = championDisplayName(row.championId, champion)
  return {
    key: `${row.championId}-${row.position}`,
    row,
    name,
    lane,
    art: loadingArt(championLoadingUrlFromIcon(champion?.iconUrl)),
    to: { path: pathFor(row.championId), query: { position: row.position } },
    label: `${name}, ${lane?.label ?? row.position}: open the build`,
  }
}))

const active = ref(0)
// Until the ring has placed its cards (and swung in), nothing is "at rest":
// the plate waits, so it never flashes on a card that is about to move.
const moving = ref(true)
// What the live region reads: the front card, once the ring has settled on it.
const announced = ref('')
const activeCard = computed(() => cards.value[active.value] ?? null)

// ─── Geometry ───────────────────────────────────────────────────────────────
const rootEl = ref<HTMLElement | null>(null)
const stageEl = ref<HTMLElement | null>(null)
const cardEls: (HTMLElement | null)[] = []
const layout = { deck: false, w: 260, h: 400 }
const tilt = { x: 0, y: 0 }
let tiltFrame = 0
const STEP = Math.PI / 6

function cardStyle(d: number) {
  const ad = Math.abs(d)
  const front = Math.max(0, 1 - ad)
  let transform: string
  let opacity: number
  let z: number
  let dim: number
  if (layout.deck) {
    if (d < 0) {
      // Swiped away: slides off to the left, turning, and fades.
      transform = `perspective(900px) translate3d(${(d * layout.w * 1.1).toFixed(1)}px, ${(ad * 14).toFixed(1)}px, 0) rotate(${(d * 9).toFixed(2)}deg)`
      opacity = Math.max(0, 1 + d * 1.15)
      z = 1100 - Math.round(ad * 100)
    }
    else {
      transform = `perspective(900px) translate3d(${(d * 18).toFixed(1)}px, ${(-d * 8).toFixed(1)}px, ${(-d * 64).toFixed(1)}px) scale(${(1 + 0.04 * front).toFixed(4)})`
      opacity = d <= 2.2 ? 1 : Math.max(0, 1 - (d - 2.2) / 0.6)
      z = 1000 - Math.round(ad * 100)
    }
    dim = Math.min(0.66, ad * 0.3)
  }
  else {
    const R = layout.w * 2.35
    const a = d * STEP
    const x = R * Math.sin(a)
    const zz = R * (Math.cos(a) - 1)
    let extra = ''
    if (front > 0.5 && (tilt.x !== 0 || tilt.y !== 0)) {
      const k = (front - 0.5) * 2
      extra = ` rotateX(${(tilt.x * k).toFixed(2)}deg) rotateY(${(tilt.y * k).toFixed(2)}deg)`
    }
    transform = `perspective(1500px) translate3d(${x.toFixed(1)}px, 0, ${zz.toFixed(1)}px) rotateY(${a.toFixed(4)}rad) scale(${(1 + 0.1 * front).toFixed(4)})${extra}`
    opacity = ad <= 2 ? 1 : Math.max(0, 1 - (ad - 2) / 0.55)
    z = 1000 - Math.round(ad * 100)
    dim = Math.min(0.72, ad * 0.42)
  }
  return { transform, opacity, z, dim }
}

function render(pos: number) {
  const count = cards.value.length
  for (let i = 0; i < count; i++) {
    const el = cardEls[i]
    if (!el) continue
    const s = cardStyle(wrapOffset(i - pos, count))
    el.style.transform = s.transform
    el.style.zIndex = String(s.z)
    el.style.opacity = s.opacity >= 0.999 ? '' : s.opacity.toFixed(3)
    el.style.visibility = s.opacity <= 0.001 ? 'hidden' : ''
    el.style.setProperty('--h-dim', s.dim.toFixed(3))
  }
}

function measure() {
  const stage = stageEl.value
  const sample = cardEls.find(Boolean)
  if (!stage || !sample) return
  layout.deck = window.matchMedia('(max-width: 639px)').matches
  layout.w = sample.offsetWidth || layout.w
  layout.h = sample.offsetHeight || layout.h
}

// ─── Engine ─────────────────────────────────────────────────────────────────
let ring: RingEngine | null = null

function setSettled(settled: boolean) {
  stageEl.value?.classList.toggle('is-settled', settled)
}

function ensureRing() {
  if (ring) return ring
  ring = createRingEngine({
    count: cards.value.length,
    render,
    onActive: (index) => {
      active.value = index
    },
    onMoving: (value) => {
      moving.value = value
      setSettled(!value)
      if (value) resetTilt()
    },
    reducedMotion: fx.value.reducedMotion,
  })
  return ring
}

function go(delta: number) {
  if (ring && started) ring.go(delta)
}

function goTo(index: number) {
  if (ring && started) ring.goTo(index)
}

// The front card's name for the live region, once the ring rests on it.
watch([active, moving], ([index, isMoving]) => {
  if (isMoving) return
  const card = cards.value[index]
  if (card) announced.value = `${card.name}, ${card.lane?.label ?? card.row.position}. ${index + 1} of ${cards.value.length}.`
})

// Focus follows the ring when it is turned from the keyboard.
watch(active, (index) => {
  const focused = document.activeElement
  if (!focused || !stageEl.value?.contains(focused) || !focused.matches('[data-ring-card]')) return
  nextTick(() => cardEls[index]?.focus({ preventScroll: true }))
})

let resizeObserver: ResizeObserver | null = null
let deckQuery: MediaQueryList | null = null
let started = false
let placedCount = 0
const ready = computed(() => !props.pending && cards.value.length > 0)

function start() {
  if (started || !ready.value || !stageEl.value) return
  measure()
  if (!cardEls.some(Boolean)) return
  started = true
  placedCount = cards.value.length
  const engine = ensureRing()
  engine.setCount(placedCount)
  // The entrance: the ring swings round into place, once, when it is on screen.
  const rect = stageEl.value.getBoundingClientRect()
  if (rect.top < window.innerHeight && rect.bottom > 0) engine.spinFrom(-1.6)
  moving.value = engine.moving
  setSettled(!engine.moving)
  resizeObserver = new ResizeObserver(relayout)
  resizeObserver.observe(stageEl.value)
  // Crossing the phone breakpoint swaps ring and deck even if the stage keeps its size.
  deckQuery = window.matchMedia('(max-width: 639px)')
  deckQuery.addEventListener('change', relayout)
}

function relayout() {
  if (!ring) return
  measure()
  resetTilt()
  render(ring.pos)
}

watch(ready, (value) => {
  if (value) nextTick(start)
})

// New rows (another patch, a refetch): new cards, placed at once.
watch(() => cards.value.map(card => card.key).join(), (keys, previous) => {
  if (!started || keys === previous) return
  nextTick(() => {
    measure()
    const engine = ensureRing()
    if (cards.value.length !== placedCount) {
      placedCount = cards.value.length
      engine.setCount(placedCount)
    }
    else {
      render(engine.pos)
    }
  })
})

onMounted(() => {
  start()
  stageEl.value?.addEventListener('wheel', onWheel, { passive: false })
  stageEl.value?.addEventListener('pointerdown', onPointerDown)
  stageEl.value?.addEventListener('click', onStageClick, true)
  // Arrow keys turn the ring wherever the focus is inside it (cards, buttons).
  rootEl.value?.addEventListener('keydown', onKeydown)
})

onBeforeUnmount(() => {
  ring?.destroy()
  resizeObserver?.disconnect()
  deckQuery?.removeEventListener('change', relayout)
  stageEl.value?.removeEventListener('wheel', onWheel)
  stageEl.value?.removeEventListener('pointerdown', onPointerDown)
  stageEl.value?.removeEventListener('click', onStageClick, true)
  rootEl.value?.removeEventListener('keydown', onKeydown)
  endPointer()
  cancelAnimationFrame(tiltFrame)
})

// ─── Input ──────────────────────────────────────────────────────────────────
function pxPerCard() {
  return layout.deck ? layout.w * 1.05 : layout.w * 1.15
}

let pointerId: number | null = null
let startX = 0
let startY = 0
let dragging = false
let suppressClick = false

function onPointerDown(event: PointerEvent) {
  if (!started || cards.value.length < 2 || pointerId !== null) return
  if (event.pointerType === 'mouse' && event.button !== 0) return
  pointerId = event.pointerId
  startX = event.clientX
  startY = event.clientY
  dragging = false
  window.addEventListener('pointermove', onPointerMove, { passive: true })
  window.addEventListener('pointerup', onPointerUp)
  window.addEventListener('pointercancel', onPointerCancel)
}

function onPointerMove(event: PointerEvent) {
  if (event.pointerId !== pointerId) return
  const dx = event.clientX - startX
  const dy = event.clientY - startY
  if (!dragging) {
    if (Math.abs(dy) > 10 && Math.abs(dy) > Math.abs(dx)) {
      // A vertical gesture: it belongs to the page.
      endPointer()
      return
    }
    if (Math.abs(dx) < 6) return
    dragging = true
    try {
      stageEl.value?.setPointerCapture(event.pointerId)
    }
    catch {
      // The pointer is already gone; the drag still ends on pointerup.
    }
    ensureRing().dragStart()
  }
  ensureRing().dragMove(-dx / pxPerCard(), event.timeStamp || performance.now())
}

function onPointerUp(event: PointerEvent) {
  if (event.pointerId !== pointerId) return
  if (dragging) {
    ensureRing().dragEnd(false)
    // The click that follows a drag is not a click.
    suppressClick = true
    setTimeout(() => (suppressClick = false), 0)
  }
  endPointer()
}

function onPointerCancel(event: PointerEvent) {
  if (event.pointerId !== pointerId) return
  if (dragging) ensureRing().dragEnd(true)
  endPointer()
}

function endPointer() {
  pointerId = null
  dragging = false
  window.removeEventListener('pointermove', onPointerMove)
  window.removeEventListener('pointerup', onPointerUp)
  window.removeEventListener('pointercancel', onPointerCancel)
}

// Capture phase on the stage, ahead of the link's own router handler: a click
// that ends a drag, or lands on a card other than the front one, never
// navigates — the second brings that card round instead.
function onStageClick(event: MouseEvent) {
  const card = (event.target as Element | null)?.closest<HTMLElement>('[data-ring-card]')
  if (!card) return
  const index = Number(card.dataset.index)
  if (!suppressClick && index === active.value && !moving.value) return
  event.preventDefault()
  event.stopPropagation()
  if (!suppressClick) goTo(index)
}

function onWheel(event: WheelEvent) {
  if (!started || cards.value.length < 2) return
  // Vertical intent scrolls the page; only a sideways swipe turns the ring.
  if (Math.abs(event.deltaX) <= Math.abs(event.deltaY)) return
  event.preventDefault()
  const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? window.innerWidth : 1
  ensureRing().wheel((event.deltaX * unit) / (pxPerCard() * 1.6))
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'ArrowRight') go(1)
  else if (event.key === 'ArrowLeft') go(-1)
  else return
  event.preventDefault()
}

// ─── The front card's tilt and glare ────────────────────────────────────────
function onFrontMove(event: PointerEvent, index: number) {
  if (event.pointerType !== 'mouse' || index !== active.value || moving.value || layout.deck || fx.value.reducedMotion) return
  const el = event.currentTarget as HTMLElement
  const rect = el.getBoundingClientRect()
  const u = (event.clientX - rect.left) / rect.width
  const v = (event.clientY - rect.top) / rect.height
  el.style.setProperty('--gx', `${(u * 100).toFixed(1)}%`)
  el.style.setProperty('--gy', `${(v * 100).toFixed(1)}%`)
  tilt.x = (0.5 - v) * 7
  tilt.y = (u - 0.5) * 9
  el.classList.add('is-tilting')
  if (!tiltFrame) {
    tiltFrame = requestAnimationFrame(() => {
      tiltFrame = 0
      if (ring && !moving.value) render(ring.pos)
    })
  }
}

function resetTilt() {
  cancelAnimationFrame(tiltFrame)
  tiltFrame = 0
  tilt.x = 0
  tilt.y = 0
  for (const el of cardEls) el?.classList.remove('is-tilting')
}

function onFrontLeave(index: number) {
  if (index !== active.value) return
  resetTilt()
  if (ring && !moving.value) render(ring.pos)
}
</script>

<template>
  <section
    ref="rootEl"
    class="h-ring"
    aria-roledescription="carousel"
    aria-label="This patch's most played and banned champions"
  >
    <div
      ref="stageEl"
      class="h-ring__stage"
    >
      <div
        class="h-ring__sky"
        aria-hidden="true"
      >
        <slot name="backdrop" />
      </div>
      <span
        class="h-ring__pool"
        aria-hidden="true"
      />

      <template v-if="pending">
        <span
          v-for="d in [-2, -1, 1, 2, 0]"
          :key="d"
          class="h-card h-card--ghost"
          :data-d="d"
          aria-hidden="true"
        />
      </template>

      <template v-else-if="cards.length > 0">
        <div
          v-for="(card, index) in cards"
          :key="card.key"
          role="group"
          aria-roledescription="slide"
          :aria-label="`${index + 1} of ${cards.length}`"
          class="contents"
        >
          <NuxtLink
            :ref="(el: any) => { cardEls[index] = (el?.$el ?? el) as HTMLElement | null }"
            :to="card.to"
            data-ring-card
            :data-index="index"
            data-h-spot
            draggable="false"
            class="h-card"
            :class="{ 'is-front': index === active }"
            :tabindex="index === active ? 0 : -1"
            :aria-label="card.label"
            @pointermove="(event: PointerEvent) => onFrontMove(event, index)"
            @pointerleave="onFrontLeave(index)"
          >
            <span class="h-card__frame">
              <img
                v-if="card.art"
                :src="card.art"
                alt=""
                width="308"
                height="560"
                draggable="false"
                decoding="async"
                :fetchpriority="index === 0 ? 'high' : 'auto'"
                class="h-card__art img-skeleton"
              >
              <span class="h-card__shade" />
              <span class="h-card__dim" />
              <span class="h-card__glare" />

              <span class="h-card__tier">
                <TierBadge
                  :tier="card.row.tier"
                  stacked
                />
              </span>

              <span class="h-card__caption">
                <img
                  v-if="card.lane?.iconUrl"
                  :src="card.lane.iconUrl"
                  alt=""
                  width="14"
                  height="14"
                  class="size-3.5 opacity-80"
                >
                <span class="truncate">{{ card.name }}</span>
              </span>

              <Transition name="h-plate">
                <DesignHChampionPlate
                  v-if="index === active && !moving"
                  :row="card.row"
                  :name="card.name"
                  :lane="card.lane"
                />
              </Transition>
            </span>
          </NuxtLink>
        </div>
      </template>

      <p
        v-else
        class="h-ring__empty"
      >
        No champion stats for this patch yet.
      </p>
    </div>

    <div class="h-ring__bar">
      <div class="h-ring__aside">
        <slot name="start" />
      </div>
      <div
        class="h-ring__controls"
        :class="{ 'is-idle': pending || cards.length < 2 }"
      >
        <button
        type="button"
        class="h-round"
        aria-label="Previous champion"
        @click="go(-1)"
      >
        <UIcon
          name="i-lucide-chevron-left"
          class="size-5"
        />
      </button>

      <div
        class="h-ring__dots"
        aria-hidden="true"
      >
        <button
          v-for="(card, index) in cards"
          :key="card.key"
          type="button"
          tabindex="-1"
          class="h-ring__dot"
          :class="{ 'is-active': index === active }"
          :title="card.name"
          @click="goTo(index)"
        />
      </div>

      <button
        type="button"
        class="h-round"
        aria-label="Next champion"
        @click="go(1)"
      >
        <UIcon
          name="i-lucide-chevron-right"
          class="size-5"
        />
      </button>
      </div>
      <div class="h-ring__aside h-ring__aside--end">
        <slot name="end" />
      </div>
    </div>

    <p
      class="sr-only"
      aria-live="polite"
      aria-atomic="true"
    >
      {{ announced }}
    </p>
    <span
      v-if="activeCard"
      class="sr-only"
    >Use the arrow keys to turn the ring.</span>
  </section>
</template>
