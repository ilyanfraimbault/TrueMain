<script setup lang="ts">
import type { ChampionOverviewRowReadModel as ChampionOverviewRow } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { championLoadingUrlFromIcon, formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#shared/utils/positions'

// Variant A — the patch's most present champions as tall loading-art cards,
// each with a liquid-glass plate refracting its art. Scrolls sideways (snap),
// tilts toward the cursor.
const props = defineProps<{
  rows: ChampionOverviewRow[]
  championsById: Map<number, ChampionStaticListItem>
  pending: boolean
}>()

const { pathFor } = useChampionSlugs()
const loadingArt = useCanonicalLoadingArt()

const cards = computed(() => props.rows.map((row) => {
  const champ = props.championsById.get(row.championId)
  return {
    ...row,
    name: championDisplayName(row.championId, champ),
    art: loadingArt(championLoadingUrlFromIcon(champ?.iconUrl)),
    lane: POSITION_BY_VALUE.get(row.position),
  }
}))

const rail = ref<HTMLElement | null>(null)
const atStart = ref(true)
const atEnd = ref(false)

function syncEdges() {
  const el = rail.value
  if (!el) return
  atStart.value = el.scrollLeft < 8
  atEnd.value = el.scrollLeft + el.clientWidth > el.scrollWidth - 8
}

function page(direction: 1 | -1) {
  const el = rail.value
  if (!el) return
  el.scrollBy({ left: direction * el.clientWidth * 0.75, behavior: 'smooth' })
}

const percent = (value: number) => formatPercentage(value / 100)
</script>

<template>
  <div class="relative">
    <div
      ref="rail"
      class="dsa-rail flex snap-x snap-mandatory gap-5 overflow-x-auto px-6 pb-10 pt-4 md:px-[max(1.5rem,calc((100vw-72rem)/2+1.5rem))]"
      :class="{ 'dsa-rail--start': atStart, 'dsa-rail--end': atEnd }"
      @scroll.passive="syncEdges"
    >
      <template v-if="pending">
        <div
          v-for="n in 6"
          :key="n"
          class="dsa-card-skeleton aspect-[308/500] w-[240px] shrink-0 animate-pulse rounded-[28px] bg-white/[0.04]"
        />
      </template>
      <template v-else>
        <NuxtLink
          v-for="(card, index) in cards"
          :key="`${card.championId}-${card.position}`"
          v-reveal="{ delay: index * 70, effect: 'blur' }"
          :to="{ path: pathFor(card.championId), query: { position: card.position } }"
          class="dsa-card-wrap w-[240px] shrink-0 snap-start focus-visible:outline-none"
        >
          <div
            v-tilt="5"
            class="dsa-card relative aspect-[308/500] overflow-hidden rounded-[28px]"
          >
            <img
              v-if="card.art"
              :src="card.art"
              :alt="card.name"
              loading="lazy"
              decoding="async"
              class="dsa-card-art absolute inset-0 size-full object-cover"
            >
            <div
              aria-hidden="true"
              class="absolute inset-0 bg-gradient-to-b from-black/30 via-transparent to-black/70"
            />
            <div
              aria-hidden="true"
              class="dsa-card-glare absolute inset-0"
            />

            <div class="absolute inset-x-3 top-3 flex items-center justify-between">
              <span class="dsa-rank font-mono text-xs tabular-nums text-white/70">{{ String(index + 1).padStart(2, '0') }}</span>
              <span class="flex items-center gap-1.5">
                <img
                  v-if="card.lane?.iconUrl"
                  :src="card.lane.iconUrl"
                  :alt="card.lane.label"
                  width="16"
                  height="16"
                  class="size-4 opacity-80"
                >
                <TierBadge :tier="card.tier" />
              </span>
            </div>

            <FxLiquidGlass
              :radius="20"
              :bezel="18"
              :depth="22"
              :blur="1.2"
              class="absolute inset-x-2.5 bottom-2.5 px-3.5 pb-3 pt-2.5"
            >
              <p class="truncate text-[15px] font-semibold text-white">
                {{ card.name }}
              </p>
              <dl class="mt-1.5 grid grid-cols-3 gap-1">
                <div>
                  <dt class="text-[9px] font-medium uppercase tracking-[0.14em] text-white/55">
                    Win
                  </dt>
                  <dd class="text-sm font-semibold text-white">
                    <FxCount
                      :value="card.winRate * 100"
                      :format="percent"
                      :duration="1400"
                    />
                  </dd>
                </div>
                <div>
                  <dt class="text-[9px] font-medium uppercase tracking-[0.14em] text-white/55">
                    Pick
                  </dt>
                  <dd class="text-sm font-semibold text-white/90">
                    <FxCount
                      :value="card.pickRate * 100"
                      :format="percent"
                      :duration="1400"
                    />
                  </dd>
                </div>
                <div>
                  <dt class="text-[9px] font-medium uppercase tracking-[0.14em] text-white/55">
                    Ban
                  </dt>
                  <dd class="text-sm font-semibold text-white/90">
                    <FxCount
                      :value="card.banRate * 100"
                      :format="percent"
                      :duration="1400"
                    />
                  </dd>
                </div>
              </dl>
            </FxLiquidGlass>
          </div>
        </NuxtLink>
      </template>
    </div>

    <div class="pointer-events-none absolute inset-x-0 -top-14 mx-auto hidden max-w-6xl justify-end gap-2 px-6 md:flex">
      <button
        type="button"
        class="dsa-round-btn pointer-events-auto"
        :disabled="atStart"
        aria-label="Previous champions"
        @click="page(-1)"
      >
        <UIcon
          name="i-lucide-arrow-left"
          class="size-4"
        />
      </button>
      <button
        type="button"
        class="dsa-round-btn pointer-events-auto"
        :disabled="atEnd"
        aria-label="Next champions"
        @click="page(1)"
      >
        <UIcon
          name="i-lucide-arrow-right"
          class="size-4"
        />
      </button>
    </div>
  </div>
</template>
