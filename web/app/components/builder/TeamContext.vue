<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { POSITION_OPTIONS, type ChampionPosition } from '~/utils/positions'

/**
 * Secondary half of the matchup page (#921): the eight draft slots that are
 * *not* the matchup. Deliberately smaller and quieter than `MatchupStage` —
 * these only re-weight the similarity search, while the role opponent above is
 * a hard filter on the sampled games.
 *
 * The played role is omitted from both columns: the player's champion and the
 * role opponent are owned by the matchup stage, so showing them here again
 * would offer two controls for the same slot.
 */
defineProps<{
  champions: ChampionStaticListItem[]
  playedPosition: ChampionPosition
  allySlots: Record<ChampionPosition, number | null>
  enemySlots: Record<ChampionPosition, number | null>
  /** Enables the clear button — the page decides whether anything is set. */
  hasPicks: boolean
}>()

defineEmits<{
  'update:ally': [position: ChampionPosition, championId: number | null]
  'update:enemy': [position: ChampionPosition, championId: number | null]
  'clear': []
}>()

// Both columns keep the canonical role order so every ally row sits directly
// across from the enemy in the same role (top vs top, jungle vs jungle, …).
</script>

<template>
  <section
    class="glass space-y-4 rounded-xl p-4 sm:p-5"
    aria-label="Rest of the draft"
  >
    <div class="flex flex-wrap items-baseline gap-x-3 gap-y-1">
      <h2 class="text-sm font-medium text-default">
        Rest of the draft
      </h2>
      <UButton
        v-if="hasPicks"
        class="ms-auto"
        variant="ghost"
        color="neutral"
        size="xs"
        icon="i-lucide-eraser"
        @click="$emit('clear')"
      >
        Clear
      </UButton>
    </div>

    <div class="grid gap-x-6 gap-y-4 sm:grid-cols-2">
      <div class="space-y-2">
        <h3 class="text-xs font-medium uppercase tracking-wider text-muted">
          Your team
        </h3>
        <ul class="space-y-1.5">
          <template
            v-for="option in POSITION_OPTIONS"
            :key="option.value"
          >
            <li
              v-if="option.value !== playedPosition"
              class="glass-hover flex items-center gap-3 rounded-lg px-2.5 py-1.5"
            >
              <SkeletonImage
                :src="option.iconUrl"
                :alt="option.label"
                :width="18"
                :height="18"
                class="size-[18px] shrink-0 opacity-80"
              />
              <ChampionPicker
                :champions="champions"
                :champion-id="allySlots[option.value]"
                placeholder="Any champion"
                size="sm"
                trigger-class="w-full"
                class="flex-1"
                @update:champion-id="$emit('update:ally', option.value, $event)"
              />
            </li>
          </template>
        </ul>
      </div>

      <div class="space-y-2">
        <h3 class="text-xs font-medium uppercase tracking-wider text-muted">
          Enemy team
        </h3>
        <ul class="space-y-1.5">
          <template
            v-for="option in POSITION_OPTIONS"
            :key="option.value"
          >
            <li
              v-if="option.value !== playedPosition"
              class="glass-hover flex items-center gap-3 rounded-lg px-2.5 py-1.5"
            >
              <SkeletonImage
                :src="option.iconUrl"
                :alt="option.label"
                :width="18"
                :height="18"
                class="size-[18px] shrink-0 opacity-80"
              />
              <ChampionPicker
                :champions="champions"
                :champion-id="enemySlots[option.value]"
                placeholder="Any champion"
                size="sm"
                trigger-class="w-full"
                class="flex-1"
                @update:champion-id="$emit('update:enemy', option.value, $event)"
              />
            </li>
          </template>
        </ul>
      </div>
    </div>
  </section>
</template>
