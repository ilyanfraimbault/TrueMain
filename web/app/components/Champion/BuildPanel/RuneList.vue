<script setup lang="ts">
import type { BuildRunePage } from '~~/shared/types/champions'
import type { RuneTreeResponse } from '~~/shared/types/static-data'
import { filterByPickRate } from '~~/shared/utils/build'

const props = defineProps<{
  runePages: BuildRunePage[]
  runeTree: RuneTreeResponse
}>()

// Same pickrate floor as the other variation panels.
const visiblePages = computed(() => filterByPickRate(props.runePages))
</script>

<template>
  <SectionCard title="Rune variations">
    <div
      v-if="visiblePages.length"
      class="flex flex-wrap items-start justify-around gap-y-4"
    >
      <div
        v-for="(page, index) in visiblePages"
        :key="`rune-${index}`"
        class="flex flex-col items-center gap-2"
      >
        <RateBadge
          :pick-rate="page.pickRate"
          :win-rate="page.winRate"
        />
        <ChampionCoreRunes
          :page="page"
          :tree="runeTree"
          :size="36"
          :keystone-size="40"
        />
      </div>
    </div>
    <p
      v-else
      class="text-sm text-muted"
    >
      No rune data
    </p>
  </SectionCard>
</template>
