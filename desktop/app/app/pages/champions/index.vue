<script setup lang="ts">
import type { Lane } from '~/types/draft'

/**
 * Every champion, as a grid of portraits — narrowed to a lane by the tier
 * list's own rows (a champion is on a lane when the site tiers it there).
 * A portrait opens the champion's builds.
 */
const { champions, portraitOf } = useChampionStatics()
const { entries } = useTierList()

const lane = ref<Lane | null>(null)
const search = ref('')

/** The lane each champion is tiered on, and its tier there. */
const tiers = computed(() => {
  const map = new Map<string, string>()
  for (const entry of entries.value) map.set(`${entry.championId}:${entry.position}`, entry.tier)
  return map
})

const list = computed(() => {
  const query = search.value.trim().toLowerCase()
  return [...champions.value.values()]
    .filter(champion => !query || champion.name.toLowerCase().includes(query))
    .filter(champion => lane.value === null || tiers.value.has(`${champion.id}:${lane.value}`))
    .sort((a, b) => a.name.localeCompare(b.name))
})
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-6">
    <PageHeader title="Champions" icon="i-lucide-swords" />

    <div class="flex items-center gap-3">
      <RolePicker v-model:position="lane" />
      <UInput v-model="search" icon="i-lucide-search" placeholder="Search a champion" size="sm" class="ml-auto w-56" autofocus />
    </div>

    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="grid grid-cols-[repeat(auto-fill,minmax(5.5rem,1fr))] gap-2 pb-2">
        <NuxtLink
          v-for="champion in list"
          :key="champion.id"
          :to="lane ? `/champions/${champion.id}?lane=${lane}` : `/champions/${champion.id}`"
          class="group flex flex-col items-center gap-1.5 rounded-lg p-2 transition-colors hover:bg-elevated"
        >
          <div class="relative">
            <img v-if="portraitOf(champion.id)" :src="portraitOf(champion.id)!" :alt="champion.name" class="size-14 rounded-lg ring-1 ring-default transition group-hover:ring-primary/60" loading="lazy">
            <TierBadge
              v-if="lane && tiers.get(`${champion.id}:${lane}`)"
              :tier="tiers.get(`${champion.id}:${lane}`)!"
              class="absolute -bottom-1 -right-1 h-5! min-w-5! rounded bg-ink-950/90"
            />
          </div>
          <span class="w-full truncate text-center text-xs text-default">{{ champion.name }}</span>
        </NuxtLink>
      </div>
    </div>
  </div>
</template>
