<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import type { ChampionOverviewRow } from '~~/shared/types/champions'
import type { WallChampion } from './champion-filter'
import { startWallLens } from './wall-lens'
import type { WallLens } from './wall-lens'

// Variant F ("Dock") — every champion on one wall, Launchpad-style. The field
// above filters it (the parent passes the matching ids, best first); the arrow
// keys move a highlight through it from the field or from a tile. Hovering a
// tile swells it and its neighbours (`wall-lens.ts`); one card glides from tile
// to tile with the name, and — for the patch's most played and banned
// champions, which wear a rose ring and a flame — their tier and win rate.
const props = defineProps<{
  champions: WallChampion[]
  /** The ids on show, in display order. */
  matches: number[]
  filtering: boolean
  hot: Map<number, ChampionOverviewRow>
  pending: boolean
  /** The field has the focus: the highlight's card stays up while it does. */
  fieldFocused: boolean
}>()

const highlight = defineModel<number | null>('highlight', { default: null })
const emit = defineEmits<{ escape: [] }>()

const { pathFor } = useChampionSlugs()
const icon = useCanonicalIcon()

const SKELETON_TILES = 168
const WAVE_STEP_MS = 15

const stageEl = ref<HTMLElement | null>(null)
const gridEl = ref<HTMLElement | null>(null)
const cols = ref(16)

const orderOf = computed(() => new Map(props.matches.map((id, index) => [id, index])))
const byId = computed(() => new Map(props.champions.map(champion => [champion.id, champion])))

// ─── Entrance: a diagonal wave, once, when the real tiles first render ──────
const entering = ref(false)
let enterTimer: ReturnType<typeof setTimeout> | undefined
// The columns the wave is drawn for. The server can only guess them; a
// hydrating wall keeps that guess (re-timing a wave already playing would make
// tiles blink), a wall mounted by a client navigation takes the real count.
const waveCols = ref(16)
const nuxtApp = useNuxtApp()

function waveDelay(index: number): number {
  const c = Math.max(1, waveCols.value)
  return (index % c) + Math.floor(index / c)
}

watch(() => props.pending || props.champions.length === 0, (waiting) => {
  if (waiting) return
  entering.value = true
  if (import.meta.server) return
  clearTimeout(enterTimer)
  const rows = Math.ceil(props.champions.length / 6)
  // Long enough for the slowest layout (six columns) to finish its wave.
  enterTimer = setTimeout(() => (entering.value = false), (6 + rows) * WAVE_STEP_MS + 900)
}, { immediate: true })

// ─── Phones: the wall opens on its first rows, the rest one tap away ────────
// (CSS only folds it under 768px; on a desktop the whole wall fits a screen.)
const expanded = ref(false)
const collapsible = computed(() => !expanded.value && !props.filtering)

// ─── Roving focus ───────────────────────────────────────────────────────────
const tabTarget = computed(() => highlight.value ?? props.matches[0] ?? null)

function tileEl(id: number): HTMLElement | null {
  return document.getElementById(`f-tile-${id}`)
}

/** Move the highlight like a grid: ←→ by one, ↑↓ by a row. False when it cannot move. */
function move(key: string): boolean {
  const list = props.matches
  if (list.length === 0) return false
  const at = highlight.value === null ? -1 : list.indexOf(highlight.value)
  let next: number
  if (at === -1) next = key === 'ArrowUp' || key === 'ArrowLeft' ? -1 : 0
  else if (key === 'ArrowRight') next = at + 1
  else if (key === 'ArrowLeft') next = at - 1
  else if (key === 'ArrowDown') next = at + cols.value
  else if (key === 'ArrowUp') next = at - cols.value
  else if (key === 'Home') next = 0
  else if (key === 'End') next = list.length - 1
  else return false
  if (next < 0 || next >= list.length) {
    // Down from the last row lands on the last tile; anything else stays put.
    if (key === 'ArrowDown' && at < list.length - 1 && at !== -1) next = list.length - 1
    else return false
  }
  const id = list[next]!
  highlight.value = id
  const el = tileEl(id)
  if (stageEl.value?.contains(document.activeElement)) el?.focus()
  else el?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  return true
}

function onTileKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('escape')
    return
  }
  if (move(event.key)) event.preventDefault()
}

defineExpose({ move })

// ─── The card ───────────────────────────────────────────────────────────────
const hoveredId = ref<number | null>(null)
const gridFocused = ref(false)
const cardId = computed(() => hoveredId.value
  ?? ((props.fieldFocused || gridFocused.value) ? highlight.value : null))
const card = reactive({ x: 0, y: 0, shown: false, instant: true })
// The champion on the card outlives the hover by its fade-out.
const shownId = ref<number | null>(null)
const cardChampion = computed(() => (shownId.value === null ? null : byId.value.get(shownId.value) ?? null))
const cardHot = computed(() => (shownId.value === null ? null : props.hot.get(shownId.value) ?? null))
const cardLane = computed(() => (cardHot.value ? POSITION_BY_VALUE.get(cardHot.value.position) ?? null : null))
const cardKeyboard = computed(() => hoveredId.value === null && cardId.value !== null)
let settleFrame = 0

function placeCard() {
  const id = cardId.value
  const li = id === null ? null : tileEl(id)?.parentElement ?? null
  if (!li || li.offsetParent === null) {
    card.shown = false
    return
  }
  const wasHidden = !card.shown
  shownId.value = id
  card.instant = wasHidden
  // Kept clear of the wall's edges, so a card over the first or last column
  // never hangs off the page.
  const width = stageEl.value?.offsetWidth ?? 0
  const centre = li.offsetLeft + li.offsetWidth / 2
  card.x = width > 200 ? Math.min(Math.max(centre, 84), width - 84) : centre
  // Above the tile at its full swell.
  card.y = li.offsetTop + li.offsetHeight / 2 - (li.offsetHeight * 1.4) / 2 - 12
  card.shown = true
  if (wasHidden) {
    // Appear in place first, glide only between tiles.
    cancelAnimationFrame(settleFrame)
    settleFrame = requestAnimationFrame(() => {
      settleFrame = requestAnimationFrame(() => (card.instant = false))
    })
  }
}

watch(cardId, () => nextTick(placeCard))

function onFocusIn(event: FocusEvent) {
  gridFocused.value = true
  expanded.value = true
  const id = Number((event.target as HTMLElement | null)?.closest<HTMLElement>('[data-tile]')?.dataset.id)
  if (Number.isFinite(id)) highlight.value = id
}

function onFocusOut(event: FocusEvent) {
  if (!stageEl.value?.contains(event.relatedTarget as Node | null)) gridFocused.value = false
}

// ─── Lens + layout tracking ─────────────────────────────────────────────────
let lens: WallLens | null = null
let resize: ResizeObserver | null = null

function readCols() {
  const grid = gridEl.value
  if (!grid) return
  const count = getComputedStyle(grid).gridTemplateColumns.split(' ').filter(Boolean).length
  if (count > 0) cols.value = count
}

function startLens() {
  lens?.stop()
  lens = null
  const fine = window.matchMedia('(hover: hover) and (pointer: fine)').matches
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  if (!stageEl.value || !fine || reduced) return
  lens = startWallLens({ stage: stageEl.value, onHover: id => (hoveredId.value = id) })
}

watch(() => props.matches, () => nextTick(() => {
  lens?.refresh()
  if (card.shown) placeCard()
}))

watch(gridEl, (grid, previous) => {
  if (previous) resize?.unobserve(previous)
  if (!grid) return
  resize?.observe(grid)
  readCols()
  if (entering.value) waveCols.value = cols.value
  startLens()
})

onMounted(() => {
  resize = new ResizeObserver(() => {
    readCols()
    lens?.refresh()
    if (card.shown) placeCard()
  })
  if (gridEl.value) {
    resize.observe(gridEl.value)
    readCols()
  }
  if (!nuxtApp.isHydrating) waveCols.value = cols.value
  startLens()
})

onBeforeUnmount(() => {
  clearTimeout(enterTimer)
  cancelAnimationFrame(settleFrame)
  resize?.disconnect()
  lens?.stop()
})

function tileLabel(champion: WallChampion): string {
  const row = props.hot.get(champion.id)
  if (!row) return champion.name
  const lane = POSITION_BY_VALUE.get(row.position)?.label
  return `${champion.name} — hot this patch: tier ${row.tier}${lane ? `, ${lane}` : ''}, ${formatPercentage(row.winRate)} win rate`
}
</script>

<template>
  <div
    ref="stageEl"
    class="f-wall"
    :class="{ 'is-entering': entering, 'is-filtering': filtering, 'is-collapsible': collapsible }"
    @focusin="onFocusIn"
    @focusout="onFocusOut"
  >
    <ul
      v-if="!pending && champions.length > 0"
      id="f-wall"
      ref="gridEl"
      class="f-wall__grid"
      aria-label="Champions"
    >
      <li
        v-for="(champion, index) in champions"
        v-show="orderOf.has(champion.id)"
        :key="champion.id"
        :data-id="champion.id"
        data-tile
        :style="{ 'order': filtering ? orderOf.get(champion.id) : undefined, '--f-d': waveDelay(index) }"
      >
        <NuxtLink
          :id="`f-tile-${champion.id}`"
          :to="pathFor(champion.id)"
          class="f-tile"
          :class="{ 'is-hot': hot.has(champion.id), 'is-hl': highlight === champion.id }"
          :tabindex="tabTarget === champion.id ? 0 : -1"
          :aria-label="tileLabel(champion)"
          @keydown="onTileKeydown"
        >
          <span class="f-tile__face">
            <img
              :src="icon(champion.iconUrl)"
              alt=""
              width="64"
              height="64"
              loading="lazy"
              decoding="async"
              draggable="false"
              class="f-tile__img img-skeleton"
            >
          </span>
          <span
            v-if="hot.has(champion.id)"
            class="f-tile__hot"
            aria-hidden="true"
          >
            <UIcon name="i-lucide-flame" />
          </span>
        </NuxtLink>
      </li>
    </ul>

    <ul
      v-else
      class="f-wall__grid"
      aria-hidden="true"
    >
      <li
        v-for="i in SKELETON_TILES"
        :key="i"
      >
        <span class="f-tile__skel" />
      </li>
    </ul>

    <button
      v-if="collapsible && !pending && champions.length > 0"
      type="button"
      class="f-wall__more"
      @click="expanded = true"
    >
      Show all {{ champions.length }} champions
      <UIcon name="i-lucide-chevron-down" />
    </button>

    <slot
      v-if="filtering && matches.length === 0 && !pending"
      name="empty"
    />

    <div
      v-if="cardChampion"
      class="f-card"
      :class="{ 'is-shown': card.shown, 'is-instant': card.instant }"
      :style="{ transform: `translate3d(${card.x}px, ${card.y}px, 0)` }"
      aria-hidden="true"
    >
      <div class="f-card__body">
        <p class="f-card__name">
          {{ cardChampion.name }}
        </p>
        <p
          v-if="cardHot"
          class="f-card__stats"
        >
          <TierBadge :tier="cardHot.tier" />
          <span
            v-if="cardLane"
            class="f-card__lane"
          >
            <img
              :src="icon(cardLane.iconUrl)"
              alt=""
              width="14"
              height="14"
            >
            {{ cardLane.shortLabel }}
          </span>
          <span class="f-card__wr">{{ formatPercentage(cardHot.winRate) }} <span>WR</span></span>
        </p>
        <p
          v-if="cardKeyboard"
          class="f-card__hint"
        >
          <kbd class="f-key">↵</kbd> open
        </p>
      </div>
    </div>
  </div>
</template>
