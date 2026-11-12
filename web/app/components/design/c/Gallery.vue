<script setup lang="ts">
import { championDisplayName } from '~~/shared/utils/champion-name'
import type { ChampionOverviewRowReadModel as ChampionOverviewRow } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championSplashUrlFromIcon, formatPercentage, formatPercentageOrDash } from '~~/shared/utils/ddragon'
import { formatCount } from '~~/shared/utils/counts'
import { POSITION_BY_VALUE } from '#shared/utils/positions'
import { clamp01, documentTop, useScrollScene, writeVar } from './scroll-scenes'

/**
 * Design exploration, variant C — chapter 01, "This patch": the patch's most
 * present champions as a strip of film frames. The stage pins and the strip
 * travels sideways as you scroll down; each frame's letterbox opens as it comes
 * in, its splash settles from a slight zoom, and its outlined rank numeral
 * drifts at its own depth. The art is graded down until the cursor (or focus)
 * floods it with colour from where the pointer enters.
 *
 * Under `md`, under reduced motion and before hydration it is a plain
 * horizontally swipeable row with every frame open.
 */
const props = defineProps<{
  topRows: ChampionOverviewRow[]
  championsById: Map<number, ChampionStaticListItem>
  pending: boolean
  patchVersion: string | null
}>()

const { pathFor } = useChampionSlugs()
const splashOf = useCanonicalSplash()

const frames = computed(() => props.topRows.map((row, index) => {
  const champ = props.championsById.get(row.championId)
  const position = POSITION_BY_VALUE.get(row.position)
  return {
    key: `${row.championId}-${row.position}`,
    rank: String(index + 1).padStart(2, '0'),
    name: championDisplayName(row.championId, champ),
    splash: splashOf(championSplashUrlFromIcon(champ?.iconUrl)),
    lane: position?.label ?? row.position,
    laneIcon: position?.iconUrl ?? null,
    tier: row.tier,
    winRate: formatPercentage(row.winRate),
    pickRate: formatPercentage(row.pickRate),
    banRate: formatPercentageOrDash(row.banRate),
    games: formatCount(row.games),
    to: { path: pathFor(row.championId), query: { position: row.position } },
  }
}))

const remaining = computed(() => Math.max(0, props.championsById.size - frames.value.length))

// ─── Pinned travel ────────────────────────────────────────────────────────
const root = ref<HTMLElement | null>(null)
const stage = ref<HTMLElement | null>(null)
const rail = ref<HTMLElement | null>(null)
const pinned = ref(false)
const current = ref(1)
const cache: Record<string, number> = {}
let top = 0
let travel = 0
let stageWidth = 0
let frameCenters: number[] = []
// Sideways pixels travelled per pixel scrolled: a touch faster than 1:1, so
// eight frames don't cost six screens of scrolling.
const SPEED = 1.3

function frameEls(): HTMLElement[] {
  return rail.value ? Array.from(rail.value.querySelectorAll<HTMLElement>('.c-frame')) : []
}

const { remeasure } = useScrollScene({
  measure({ vh, vw }) {
    if (!root.value || !rail.value || !stage.value) return
    const wanted = vw >= 768 && !window.matchMedia('(prefers-reduced-motion: reduce)').matches
    if (wanted !== pinned.value) {
      pinned.value = wanted
      // The layout switches on the next render; measure again after it.
      nextTick(remeasure)
      return
    }
    if (!pinned.value) {
      root.value.style.height = ''
      return
    }
    stageWidth = stage.value.clientWidth
    travel = Math.max(0, rail.value.scrollWidth - stageWidth)
    // The run the strip needs at SPEED, plus a short dwell at each end.
    const height = Math.round(vh + travel / SPEED + vh * 0.25)
    if (root.value.style.height !== `${height}px`) root.value.style.height = `${height}px`
    top = documentTop(root.value)
    writeVar(stage.value, '--sw', stageWidth, cache, 1)
    // Where each frame starts opening: when its left edge reaches the right
    // edge of the stage — or, for the frames already in view when the stage
    // pins, staggered across the approach from below.
    // Every read first, then every write, so the loop forces no layout.
    const els = frameEls()
    const boxes = els.map(el => ({ left: el.offsetLeft, width: el.offsetWidth }))
    frameCenters = boxes.map(({ left, width }) => left + width / 2)
    els.forEach((el, i) => {
      const { left, width } = boxes[i]!
      const enter = Math.max(left - stageWidth * 0.98, -vh * 0.85 + i * vh * 0.14)
      el.style.setProperty('--enter', String(Math.round(enter)))
      el.style.setProperty('--span', String(Math.round(Math.min(width * 0.9, stageWidth * 0.45))))
      el.style.setProperty('--cx', String(Math.round(frameCenters[i]!)))
    })
  },
  update({ y, vh }) {
    if (!pinned.value || !stage.value) return
    // Unclamped below so the frames can open during the approach.
    const raw = (y - top - vh * 0.1) * SPEED
    const x = Math.min(travel, Math.max(-vh, raw))
    writeVar(stage.value, '--x', Math.max(0, x), cache, 1)
    writeVar(stage.value, '--xr', x, cache, 1)
    writeVar(stage.value, '--gp', travel > 0 ? clamp01(x / travel) : 0, cache)
    if (frameCenters.length) {
      const center = Math.max(0, x) + stageWidth / 2
      let nearest = 0
      let best = Infinity
      frameCenters.forEach((c, i) => {
        const d = Math.abs(c - center)
        if (d < best) {
          best = d
          nearest = i
        }
      })
      if (current.value !== nearest + 1) current.value = nearest + 1
    }
  },
})

watch(() => [props.pending, frames.value.length], () => nextTick(remeasure))
</script>

<template>
  <section
    ref="root"
    class="c-gallery"
    :class="{ 'is-pinned': pinned }"
    aria-labelledby="c-gallery-title"
  >
    <div
      ref="stage"
      class="c-gallery__stage"
    >
      <div
        ref="rail"
        class="c-gallery__rail"
      >
        <header class="c-gallery__intro">
          <p class="c-mono c-gallery__kicker">
            <b>01</b><i aria-hidden="true" />This patch
            <template v-if="patchVersion">
              <span
                class="c-gallery__dot"
                aria-hidden="true"
              /> Patch {{ patchVersion }}
            </template>
          </p>
          <h2
            id="c-gallery-title"
            v-reveal.stagger="{ effect: 'c-mask', step: 110 }"
            class="c-gallery__title"
          >
            <span class="c-mask"><span>Most played,</span></span>{{ ' ' }}
            <span class="c-mask"><span>most banned.</span></span>
          </h2>
          <p
            v-reveal="{ delay: 240, effect: 'blur' }"
            class="c-gallery__lede"
          >
            The champions with the biggest presence this patch, by pick and ban rate. Open one for the build its mains are running.
          </p>
          <NuxtLink
            to="/champions/tierlist"
            class="c-textlink"
          >
            <span
              class="c-roll"
              data-text="Full tier list"
            ><span>Full tier list</span></span>
            <UIcon
              name="i-lucide-arrow-right"
              class="c-textlink__icon"
            />
          </NuxtLink>
        </header>

        <ol
          class="c-gallery__track"
          :aria-busy="pending"
        >
          <template v-if="pending">
            <li
              v-for="i in 6"
              :key="`s${i}`"
              class="c-frame is-skeleton"
              aria-hidden="true"
            >
              <span class="c-frame__link">
                <span class="c-frame__media"><USkeleton class="size-full rounded-none" /></span>
              </span>
            </li>
          </template>
          <li
            v-for="(frame, i) in frames"
            v-else
            :key="frame.key"
            class="c-frame"
            :class="{ 'is-alt': i % 2 === 1 }"
          >
            <NuxtLink
              v-spotlight
              :to="frame.to"
              class="c-frame__link"
              data-c-cursor="View build"
            >
              <span
                class="c-frame__num"
                aria-hidden="true"
              >{{ frame.rank }}</span>
              <span class="c-frame__media">
                <img
                  v-if="frame.splash"
                  :src="frame.splash"
                  alt=""
                  class="c-frame__img c-frame__img--graded"
                  :loading="i < 3 ? 'eager' : 'lazy'"
                  decoding="async"
                >
                <img
                  v-if="frame.splash"
                  :src="frame.splash"
                  alt=""
                  class="c-frame__img c-frame__img--color"
                  loading="lazy"
                  decoding="async"
                >
                <span class="c-frame__shade" />
                <span class="c-frame__edge" />
              </span>
              <span class="c-frame__top">
                <span class="c-frame__lane">
                  <img
                    v-if="frame.laneIcon"
                    :src="frame.laneIcon"
                    alt=""
                    width="14"
                    height="14"
                  >
                  <span class="c-mono">{{ frame.lane }}</span>
                </span>
                <span class="c-frame__tier"><TierBadge :tier="frame.tier" /><span
                  class="c-mono"
                  aria-hidden="true"
                >Tier</span></span>
              </span>
              <span class="c-frame__body">
                <span class="c-frame__name">{{ frame.name }}</span>
                <span class="c-frame__stats">
                  <span class="c-frame__stat c-frame__stat--lead"><b>{{ frame.winRate }}</b><span class="c-mono">Win rate</span></span>
                  <span class="c-frame__stat"><b>{{ frame.pickRate }}</b><span class="c-mono">Pick</span></span>
                  <span class="c-frame__stat"><b>{{ frame.banRate }}</b><span class="c-mono">Ban</span></span>
                  <span class="c-frame__stat"><b>{{ frame.games }}</b><span class="c-mono">Games</span></span>
                </span>
              </span>
            </NuxtLink>
          </li>
        </ol>

        <NuxtLink
          v-if="!pending && remaining > 0"
          to="/champions"
          class="c-gallery__outro"
          data-c-cursor="Browse"
        >
          <span class="c-mono">And the rest of the roster</span>
          <span class="c-gallery__more">+{{ remaining }}</span>
          <span class="c-gallery__more-label">
            <span
              class="c-roll"
              data-text="All champions"
            ><span>All champions</span></span>
            <UIcon
              name="i-lucide-arrow-right"
              class="c-textlink__icon"
            />
          </span>
        </NuxtLink>
      </div>

      <div
        class="c-gallery__hud"
        aria-hidden="true"
      >
        <span class="c-mono">Frame <b>{{ String(current).padStart(2, '0') }}</b> / {{ String(Math.max(frames.length, 1)).padStart(2, '0') }}</span>
        <span class="c-gallery__progress"><span /></span>
        <span class="c-mono">Scroll</span>
      </div>
    </div>
  </section>
</template>
