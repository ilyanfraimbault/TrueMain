<script setup lang="ts">
import type { BuildRunePage } from '~~/shared/types/champions'
import type { RuneTreeResponse } from '~~/shared/types/static-data'
import { variationOptions } from '~~/shared/utils/build'

const props = defineProps<{
  runePages: BuildRunePage[]
  runeTree: RuneTreeResponse
  /** Scaffolding rather than data — see `ChampionBuildTabs`' own `pending`. */
  pending?: boolean
}>()

// Same rule as the other variation panels (#1466): the floor, the cap, and
// nothing at all when a single page dominates — the core block already draws
// that page, in full, above.
const visiblePages = computed(() => variationOptions(props.runePages))
</script>

<template>
  <SectionCard
    v-if="visiblePages.length"
    :level="2"
    title="Rune variations"
  >
    <div class="flex flex-wrap items-start justify-around gap-y-4">
      <div
        v-for="(page, index) in visiblePages"
        :key="`rune-${index}`"
        class="flex flex-col items-center gap-2"
      >
        <RateBadge
          :games="page.games"
          :pick-rate="page.pickRate"
          :win-rate="page.winRate"
          :pending="pending"
        />
        <ChampionCoreRunes
          :page="page"
          :tree="runeTree"
          :size="36"
          :keystone-size="40"
        />
      </div>
    </div>
  </SectionCard>
</template>
