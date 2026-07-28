<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionPosition } from '~/utils/positions'
import { POSITION_BY_VALUE } from '~/utils/positions'

/**
 * Centre stage of the builder (#921): the matchup — your champion and role
 * against the lane opponent — as the primary control of the page. Everything
 * else on the builder (the eight remaining draft slots) is a refinement of
 * what is picked here, so this block is deliberately the largest surface and
 * sits above the fold.
 *
 * Purely presentational: the page owns the draft state, this component only
 * emits picks.
 */
defineProps<{
  champions: ChampionStaticListItem[]
  playedChampion: ChampionStaticListItem | null
  playedChampionId: number | null
  playedPosition: ChampionPosition | null
  opponentChampion: ChampionStaticListItem | null
  opponentChampionId: number | null
}>()

defineEmits<{
  'update:playedChampionId': [value: number | null]
  'update:playedPosition': [value: ChampionPosition | null]
  'update:opponentChampionId': [value: number | null]
}>()

const PORTRAIT_PX = 72
</script>

<template>
  <section
    class="glass rounded-2xl p-4 sm:p-6"
    aria-label="Matchup"
  >
    <div class="grid items-stretch gap-3 sm:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)]">
      <!-- Your side: the only place the played champion and role can be set. -->
      <div class="flex flex-col items-center gap-3 rounded-xl bg-primary/5 p-4 ring-1 ring-inset ring-primary/25">
        <p class="text-xs font-medium uppercase tracking-wider text-primary">
          You play
        </p>
        <SkeletonImage
          v-if="playedChampion"
          :src="playedChampion.iconUrl"
          :alt="playedChampion.name"
          :width="PORTRAIT_PX"
          :height="PORTRAIT_PX"
          class="size-18 rounded-2xl ring-2 ring-primary/50"
        />
        <div
          v-else
          class="flex size-18 items-center justify-center rounded-2xl ring-1 ring-inset ring-primary/25"
        >
          <UIcon
            name="i-lucide-user-round"
            class="size-8 text-dimmed"
          />
        </div>
        <ChampionPicker
          :champions="champions"
          :champion-id="playedChampionId"
          placeholder="Choose your champion"
          size="lg"
          trigger-class="w-full max-w-64"
          @update:champion-id="$emit('update:playedChampionId', $event)"
        />
        <RolePicker
          :position="playedPosition"
          hide-all
          @update:position="$emit('update:playedPosition', $event)"
        />
      </div>

      <!-- Lane label doubles as the separator: it names what the two sides are
           fighting over once a role is picked. -->
      <div class="flex flex-row items-center justify-center gap-3 sm:flex-col">
        <span
          class="rounded-full bg-primary/10 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary ring-1 ring-inset ring-primary/25"
        >
          vs
        </span>
        <span
          v-if="playedPosition"
          class="text-xs uppercase tracking-wider text-dimmed"
        >
          {{ POSITION_BY_VALUE.get(playedPosition)?.label ?? playedPosition }}
        </span>
      </div>

      <div class="flex flex-col items-center gap-3 rounded-xl p-4 ring-1 ring-inset ring-accented">
        <p class="text-xs font-medium uppercase tracking-wider text-muted">
          Lane opponent
        </p>
        <SkeletonImage
          v-if="opponentChampion"
          :src="opponentChampion.iconUrl"
          :alt="opponentChampion.name"
          :width="PORTRAIT_PX"
          :height="PORTRAIT_PX"
          class="size-18 rounded-2xl ring-2 ring-accented"
        />
        <div
          v-else
          class="flex size-18 items-center justify-center rounded-2xl ring-1 ring-inset ring-accented"
        >
          <UIcon
            name="i-lucide-swords"
            class="size-8 text-dimmed"
          />
        </div>
        <ChampionPicker
          :champions="champions"
          :champion-id="opponentChampionId"
          placeholder="Choose the enemy laner"
          size="lg"
          trigger-class="w-full max-w-64"
          @update:champion-id="$emit('update:opponentChampionId', $event)"
        />
        <p class="text-center text-xs text-dimmed">
          {{ opponentChampion
            ? 'Only games of this matchup are used.'
            : 'Optional — pin it to make the build matchup-specific.' }}
        </p>
      </div>
    </div>
  </section>
</template>
