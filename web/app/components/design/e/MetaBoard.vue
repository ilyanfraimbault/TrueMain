<script setup lang="ts">
import type { ChampionTierListResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_OPTIONS } from '#common/utils/positions'

// Variant E home, round 3: the patch's meta lane by lane. Each column's head is
// the way into the tier list for that lane; below it, the lane's strongest
// champions as compact rows. One request for every lane (`/champions/tierlist`
// unscoped), grouped here.
const props = defineProps<{
  championsById: Map<number, ChampionStaticListItem>
}>()

const PER_LANE = 5
const TIER_ORDER = ['S', 'A', 'B', 'C', 'D']

const apiFetch = useApiFetch()
const { data, status } = useAsyncData<ChampionTierListResponse>(
  'e-home-tierlist-all',
  (_nuxtApp, { signal }) => apiFetch<ChampionTierListResponse>('/champions/tierlist', { signal }),
  { server: false },
)
const pending = computed(() => status.value === 'idle' || status.value === 'pending')

const { pathFor } = useChampionSlugs()

const lanes = computed(() => {
  const groups = data.value?.tiers ?? []
  return POSITION_OPTIONS.map((lane) => {
    const entries = groups
      .slice()
      .sort((a, b) => TIER_ORDER.indexOf(a.tier) - TIER_ORDER.indexOf(b.tier))
      .flatMap(group => group.entries
        .filter(entry => entry.position === lane.value)
        .map(entry => ({ ...entry, tier: group.tier })))
      .slice(0, PER_LANE)
      .map((entry) => {
        const champ = props.championsById.get(entry.championId)
        return {
          ...entry,
          name: championDisplayName(entry.championId, champ),
          iconUrl: champ?.iconUrl ?? '',
        }
      })
    return { ...lane, entries }
  })
})
</script>

<template>
  <section
    class="e-hx-section"
    aria-labelledby="e-hx-meta-title"
  >
    <header class="e-hx-head">
      <div>
        <p class="eyebrow">
          This patch{{ data?.patchVersion ? ` · ${data.patchVersion}` : '' }}
        </p>
        <h2
          id="e-hx-meta-title"
          class="e-hx-h2"
        >
          The meta, lane by lane.
        </h2>
      </div>
      <NuxtLink
        to="/champions/tierlist"
        class="e-hx-more"
      >
        Full tier list
        <UIcon
          name="i-lucide-arrow-right"
          class="size-4"
        />
      </NuxtLink>
    </header>

    <div class="e-hx-lanes">
      <article
        v-for="lane in lanes"
        :key="lane.value"
        class="e-hx-lane"
      >
        <NuxtLink
          :to="{ path: '/champions/tierlist', query: { position: lane.value } }"
          class="e-hx-lane__head"
        >
          <img
            :src="lane.iconUrl"
            :alt="''"
            width="20"
            height="20"
            class="size-5"
          >
          <span class="e-hx-lane__name">{{ lane.label }}</span>
          <UIcon
            name="i-lucide-arrow-up-right"
            class="e-hx-lane__go size-4"
          />
        </NuxtLink>

        <ul
          v-if="pending"
          class="e-hx-lane__list"
          aria-hidden="true"
        >
          <li
            v-for="n in PER_LANE"
            :key="n"
            class="e-hx-row"
          >
            <USkeleton class="size-8 rounded-md" />
            <USkeleton class="h-3.5 flex-1" />
          </li>
        </ul>
        <ul
          v-else-if="lane.entries.length"
          class="e-hx-lane__list"
        >
          <li
            v-for="entry in lane.entries"
            :key="entry.championId"
          >
            <NuxtLink
              :to="{ path: pathFor(entry.championId), query: { position: lane.value } }"
              class="e-hx-row"
            >
              <SkeletonImage
                :src="entry.iconUrl"
                :alt="entry.name"
                width="32"
                height="32"
                class="size-8 shrink-0 rounded-md"
              />
              <span class="e-hx-row__name">{{ entry.name }}</span>
              <span class="e-hx-row__tier">{{ entry.tier }}</span>
              <span class="e-hx-row__wr">{{ formatPercentage(entry.winRate, 0) }}</span>
            </NuxtLink>
          </li>
        </ul>
        <p
          v-else
          class="e-hx-lane__empty"
        >
          No data for this lane yet.
        </p>
      </article>
    </div>
  </section>
</template>
