<script setup lang="ts">
import type { Lane } from '~/types/draft'
import { LANES, LANE_LABELS } from '~/types/draft'

/**
 * One champion: its banner (the site's `PageHero`) with its numbers on a lane, and its builds on that lane
 * in the same view the draft opens after a pick. The lanes offered are the
 * ones the site tiers it on, most played first.
 */
const route = useRoute()
const router = useRouter()

const championId = computed(() => Number(route.params.id))
const { nameOf, aliasOf } = useChampionStatics()
const { entries, entryOf } = useTierList()

/** The lanes the champion is tiered on, most played first. */
const lanes = computed<Lane[]>(() => entries.value
  .filter(entry => entry.championId === championId.value)
  .sort((a, b) => b.games - a.games)
  .map(entry => entry.position as Lane)
  .filter(lane => LANES.includes(lane)))

const lane = computed<Lane | null>({
  get: () => {
    const asked = String(route.query.position ?? '').toUpperCase() as Lane
    return LANES.includes(asked) ? asked : lanes.value[0] ?? null
  },
  set: value => void router.replace({ query: { ...route.query, position: value ?? undefined } }),
})

const entry = computed(() => entryOf(championId.value, lane.value))
const percent = (value: number) => `${(value * 100).toFixed(1)}%`
</script>

<template>
  <div class="flex h-full flex-col">
    <!-- The site's banner (`PageHero`): splash, portrait, the lanes it is
         tiered on, and its numbers on the chosen lane. The app reads the tier
         list, which has no patch before this one, so the tiles carry no move. -->
    <div class="shrink-0 px-4 pt-4">
      <PageHero :splash-url="aliasOf(championId) ? splashOfAlias(aliasOf(championId)!) : null" splash-position="60% 20%">
        <template #portrait>
          <ChampionPortrait :champion-id="championId" size="lg" class="size-[72px]! shrink-0 rounded-lg! shadow-lg ring-1 ring-white/10" />
        </template>
        <template #title>
          <h1 class="truncate text-[28px] font-semibold leading-tight text-highlighted">{{ nameOf(championId) }}</h1>
        </template>
        <template #subtitle>
          <RolePicker v-if="lanes.length" v-model:position="lane" hide-all />
          <span v-else class="text-dimmed">Not tiered on any lane this patch</span>
        </template>
        <template v-if="entry" #actions>
          <TierMark :tier="entry.tier" />
        </template>
        <template v-if="entry">
          <KpiTile label="Win rate" :value="percent(entry.winRate)" />
          <KpiTile label="Pick rate" :value="percent(entry.pickRate)" />
          <KpiTile label="Ban rate" :value="percent(entry.banRate)" />
          <KpiTile label="Games" :value="entry.games.toLocaleString('en-US')" />
        </template>
      </PageHero>
    </div>

    <div class="min-h-0 flex-1 p-4">
      <BuildView v-if="lane" :champion-id="championId" :position="lane">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <span class="text-sm font-semibold text-highlighted">{{ LANE_LABELS[lane] }}</span>
            <div class="flex items-center gap-0.5">
              <UButton
                :to="`/matchup?champion=${championId}&position=${lane}`"
                icon="i-lucide-wand-sparkles"
                color="neutral"
                variant="ghost"
                size="xs"
                aria-label="Matchup"
                title="Against a champion"
              />
              <UButton
                v-if="aliasOf(championId)"
                icon="i-lucide-external-link"
                color="neutral"
                variant="ghost"
                size="xs"
                aria-label="Open on TrueMain"
                title="Open on TrueMain"
                @click="openOnSite(`/champions/${aliasOf(championId)!.toLowerCase()}`)"
              />
            </div>
          </div>
        </template>
      </BuildView>
    </div>
  </div>
</template>
