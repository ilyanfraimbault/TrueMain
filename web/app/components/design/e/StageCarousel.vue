<script setup lang="ts">
import type { ChampionOverviewRow } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { formatCount } from '~~/shared/utils/counts'
import { championLoadingUrlFromIcon, formatPercentage, formatPercentageOrDash } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#shared/utils/positions'
import { winRateTone } from '#common/utils/rate-tone'

// The stage on a phone (<768px): the same top champions as pick cards in a
// row the thumb swipes through (native scroll snapping — no script moves it),
// each card opening its build. The dots follow the scroll and jump to a card.
// No auto-advance here: a row that moves under the thumb fights it.
const props = defineProps<{
  rows: ChampionOverviewRow[]
  championsById: Map<number, ChampionStaticListItem>
  pending: boolean
  patch: string | null
}>()

const { pathFor } = useChampionSlugs()
const artOf = useCanonicalLoadingArt()
const fx = useFx()

const slides = computed(() => props.rows.map((row) => {
  const champion = props.championsById.get(row.championId)
  const lane = POSITION_BY_VALUE.get(row.position) ?? null
  return {
    key: `${row.championId}-${row.position}`,
    row,
    name: championDisplayName(row.championId, champion),
    lane,
    laneLabel: lane?.label ?? row.position,
    art: artOf(championLoadingUrlFromIcon(champion?.iconUrl)),
    to: { path: pathFor(row.championId), query: { position: row.position } },
  }
}))

const trackEl = ref<HTMLElement | null>(null)
const active = ref(0)
let frame = 0

function cardEls(): HTMLElement[] {
  return Array.from(trackEl.value?.querySelectorAll<HTMLElement>('[data-e-slide]') ?? [])
}

function measure() {
  frame = 0
  const track = trackEl.value
  const first = cardEls()[0]
  if (!track || !first) return
  const pitch = first.offsetWidth + Number.parseFloat(getComputedStyle(track).columnGap || '0')
  active.value = Math.max(0, Math.min(slides.value.length - 1, Math.round(track.scrollLeft / pitch)))
}

function onScroll() {
  if (!frame) frame = requestAnimationFrame(measure)
}

function goTo(i: number) {
  const track = trackEl.value
  const card = cardEls()[i]
  if (!track || !card) return
  track.scrollTo({ left: card.offsetLeft - track.offsetLeft - (track.clientWidth - card.offsetWidth) / 2, behavior: fx.value.reducedMotion ? 'auto' : 'smooth' })
}

onBeforeUnmount(() => cancelAnimationFrame(frame))

const pct = (value: number) => formatPercentage(value)
</script>

<template>
  <section
    class="e-carousel"
    aria-roledescription="carousel"
    aria-label="Most played and banned champions"
  >
    <header class="e-carousel__head">
      <p class="e-stage__eyebrow">
        <span
          v-if="patch"
          class="e-chip"
        >Patch {{ patch }}</span>
        <span>Most played and banned</span>
      </p>
    </header>

    <div
      v-if="pending"
      class="e-carousel__track"
      aria-hidden="true"
    >
      <span
        v-for="i in 2"
        :key="i"
        class="e-slide e-slide--skeleton"
      />
    </div>

    <template v-else-if="slides.length">
      <ul
        ref="trackEl"
        class="e-carousel__track"
        @scroll.passive="onScroll"
      >
        <li
          v-for="(slide, i) in slides"
          :key="slide.key"
          class="e-carousel__item"
          data-e-slide
          aria-roledescription="slide"
          :aria-label="`${i + 1} of ${slides.length}`"
        >
          <NuxtLink
            :to="slide.to"
            class="e-slide"
          >
            <img
              v-if="slide.art"
              :src="slide.art"
              alt=""
              width="308"
              height="560"
              loading="lazy"
              decoding="async"
              class="e-slide__art img-skeleton"
            >
            <span
              class="e-slide__shade"
              aria-hidden="true"
            />
            <span class="e-slide__body">
              <span class="e-slide__meta">
                <img
                  v-if="slide.lane?.iconUrl"
                  :src="slide.lane.iconUrl"
                  alt=""
                  width="14"
                  height="14"
                >
                {{ slide.laneLabel }}
                <span
                  class="e-stage__sep"
                  aria-hidden="true"
                />
                <TierBadge :tier="slide.row.tier" />
                <span>tier</span>
              </span>
              <span class="e-slide__name">{{ slide.name }}</span>
              <span class="e-slide__stats">
                <span>
                  <span
                    class="e-slide__value"
                    :class="winRateTone(slide.row.winRate)"
                  >{{ pct(slide.row.winRate) }}</span>
                  <span class="e-slide__label">Win</span>
                </span>
                <span>
                  <span class="e-slide__value">{{ pct(slide.row.pickRate) }}</span>
                  <span class="e-slide__label">Pick</span>
                </span>
                <span>
                  <span class="e-slide__value">{{ formatPercentageOrDash(slide.row.banRate) }}</span>
                  <span class="e-slide__label">Ban</span>
                </span>
              </span>
              <span class="e-slide__foot">
                <span>{{ formatCount(slide.row.games) }} games</span>
                <span class="e-slide__open">Open build
                  <UIcon
                    name="i-lucide-arrow-right"
                    class="size-4"
                  />
                </span>
              </span>
            </span>
          </NuxtLink>
        </li>
      </ul>

      <div class="e-carousel__dots">
        <button
          v-for="(slide, i) in slides"
          :key="slide.key"
          type="button"
          class="e-dot"
          :class="{ 'is-active': i === active }"
          :aria-label="`Show ${slide.name}`"
          :aria-current="i === active ? 'true' : undefined"
          @click="goTo(i)"
        />
      </div>
    </template>

    <p
      v-else
      class="e-carousel__empty"
    >
      No champion stats for this patch yet.
    </p>
  </section>
</template>
