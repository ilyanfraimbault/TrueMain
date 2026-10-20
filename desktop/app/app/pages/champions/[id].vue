<script setup lang="ts">
import { POSITIONS, positionLabel } from '#common/utils/positions'
import type { Lane } from '~/types/draft'

/**
 * One champion: the site's champion banner (`ChampionHero`) on a lane, and its
 * builds on that lane in the same view the draft opens after a pick. The lanes
 * offered are the ones the site tiers it on, most played first.
 */
const route = useRoute()
const router = useRouter()

const championId = computed(() => Number(route.params.id))
const { nameOf, aliasOf, portraitOf, splashOf } = useChampionStatics()
const { entries, entryOf, patch } = useTierList()

/** The lanes the champion is tiered on, most played first. */
const lanes = computed<Lane[]>(() => entries.value
  .filter(entry => entry.championId === championId.value)
  .sort((a, b) => b.games - a.games)
  .map(entry => entry.position as Lane)
  .filter(lane => POSITIONS.includes(lane)))

const lane = computed<Lane | null>({
  get: () => {
    const asked = String(route.query.position ?? '').toUpperCase() as Lane
    return POSITIONS.includes(asked) ? asked : lanes.value[0] ?? null
  },
  set: value => void router.replace({ query: { ...route.query, position: value ?? undefined } }),
})

const entry = computed(() => entryOf(championId.value, lane.value))

/** The lane's builds, or the player's own kits on it (#2185); kept in the URL like the lane. */
const view = computed<'builds' | 'kits'>({
  get: () => (route.query.view === 'kits' ? 'kits' : 'builds'),
  set: value => void router.replace({ query: { ...route.query, view: value === 'kits' ? 'kits' : undefined } }),
})
const views = [
  { label: 'Builds', value: 'builds', icon: 'i-lucide-scroll-text' },
  { label: 'My kits', value: 'kits', icon: 'i-lucide-swords' },
]

// The banner's figures, as on the site: the lane's patch against the one before.
const { data: trend, pending: trendPending } = useChampionTrend(championId, lane, () => lane.value !== null)
</script>

<template>
  <div class="flex h-full flex-col">
    <!-- The site's champion banner: the tier list's sample for the lane (the
         API's default slice — mains, every rank) and the lane's figures
         against the patch before; the app's lane picker stands in for the
         lane icon. -->
    <div class="shrink-0 px-4 pt-4">
      <ChampionHero
        :champion-name="nameOf(championId)"
        :champion-icon-url="portraitOf(championId)"
        :champion-id="championId"
        :splash-url="splashOf(championId)"
        :position="lane ?? ''"
        :patch="patch || null"
        :total-games="entry?.games ?? 0"
        truemains-only
        :tier="entry?.tier ?? null"
        :trend="trend?.points ?? null"
        :trend-loading="trendPending"
      >
        <template v-if="lanes.length" #lane>
          <RolePicker v-model:position="lane" hide-all />
        </template>
        <template #actions>
          <FavoriteToggle
            :champion="{ championId, position: lane }"
            :champion-label="[nameOf(championId), positionLabel(lane)].filter(Boolean).join(' ')"
          />
        </template>
      </ChampionHero>
    </div>

    <div v-if="lane" class="flex min-h-0 flex-1 flex-col gap-3 p-4">
      <UTabs
        v-model="view"
        :items="views"
        :content="false"
        variant="pill"
        color="neutral"
        size="xs"
        class="shrink-0 self-start"
        :ui="{ list: 'ring ring-default', indicator: 'rounded-md bg-accented shadow-none', trigger: 'px-3 data-[state=active]:text-highlighted' }"
      />
      <KitPanel v-if="view === 'kits'" :champion-id="championId" :position="lane" class="min-h-0 flex-1" />
      <BuildView v-else :champion-id="championId" :position="lane" class="min-h-0 flex-1">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <span class="text-sm font-semibold text-highlighted">{{ positionLabel(lane, { short: true }) }}</span>
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
