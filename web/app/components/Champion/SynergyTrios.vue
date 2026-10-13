<script setup lang="ts">
import { formatCount } from '~~/shared/utils/counts'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionTrioSynergyEntry } from '~~/shared/types/champions'

// The synergies panel's trio completions for the chosen duo: the best third
// pick alongside the champion and the selected partner. The panel owns the
// fetch; this renders its states.
defineProps<{
  championName: string
  partnerName: string | null
  loading: boolean
  error: unknown
  completions: ChampionTrioSynergyEntry[]
  /** Games the duo shares — the ceiling for every trio drawn from it. */
  pairGames: number
  minGames: number
  championById: Map<number, ChampionStaticListItem>
}>()
</script>

<template>
  <div class="flex flex-col gap-3 border-t border-default pt-4">
    <p class="px-2 text-xs font-semibold uppercase tracking-wide text-primary">
      Best third pick with {{ partnerName }}
    </p>

    <template v-if="loading">
      <USkeleton
        v-for="i in 3"
        :key="`trio-skel-${i}`"
        class="h-8 w-full rounded-md"
      />
    </template>

    <FetchErrorAlert
      v-else-if="error"
      :error="error"
      title="Failed to load the trio suggestions"
      class="my-2"
    />

    <!-- A duo's games are the ceiling for every trio drawn from it, so a thin
         duo cannot support a third dimension. Show the real count. -->
    <UEmpty
      v-else-if="completions.length === 0"
      size="xs"
      icon="i-lucide-users-round"
    >
      <template #description>
        {{ championName }} and {{ partnerName }} share
        {{ formatCount(pairGames) }} games — not enough to suggest a third pick
        (minimum {{ minGames }} games together with the same teammate).
      </template>
    </UEmpty>

    <template v-else>
      <div class="flex flex-col gap-1">
        <ChampionSynergyRow
          v-for="completion in completions"
          :key="`${completion.championId}-${completion.position}`"
          :champion="championById.get(completion.championId) ?? null"
          :champion-id="completion.championId"
          :position="completion.position"
          :games="completion.games"
          :win-rate="completion.winRate"
          :synergy="completion.synergy"
        />
      </div>
    </template>
  </div>
</template>
