<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionPosition } from '~/utils/positions'
import { POSITION_BY_VALUE } from '~/utils/positions'

/**
 * Centre stage of the matchup page (#921): the matchup — your champion and
 * role against the role opponent — as the primary control of the page.
 * Everything else on the page (the eight remaining draft slots) is a
 * refinement of what is picked here, so this block sits above the fold.
 *
 * Composed as one band rather than two boxes: role on top, then the two sides
 * on a shared baseline. The previous version stacked label/portrait/picker
 * vertically in two ring-bordered sub-panels of *different heights* — your side
 * carried the role picker, the opponent's did not — so nothing lined up across
 * the `vs` axis. Both sides are now the same row shape, which makes them align
 * by construction and halves the height.
 *
 * Ownership is carried by the accent, not by a tinted panel: your label and
 * your portrait ring are `primary`, the opponent's are neutral. The old
 * `bg-primary/5` + `ring-primary/25` panel was a rose-gold *surface* tint,
 * which the design system no longer allows — and against the warm neutral it
 * replaced, it barely registered anyway.
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
    class="surface space-y-4 rounded-2xl p-4 sm:p-6"
    aria-label="Matchup"
  >
    <!-- Role first: it gates the whole page (no build is fetched without it)
         and it is the one pick that used to hide under the champion select. -->
    <div class="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-default pb-4">
      <p class="stat-label">
        Your role
      </p>
      <RolePicker
        :position="playedPosition"
        hide-all
        @update:position="$emit('update:playedPosition', $event)"
      />
      <!-- The strip is icons only, so the picked role is named in words —
           it used to be the label under the `vs` chip. -->
      <p
        v-if="playedPosition"
        class="text-xs text-muted"
      >
        {{ POSITION_BY_VALUE.get(playedPosition)?.label ?? playedPosition }}
      </p>
      <p
        v-else
        class="text-xs text-dimmed"
      >
        Required — builds are read per role.
      </p>
    </div>

    <div class="grid gap-4 sm:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] sm:items-center">
      <!-- Your side: the only place the played champion can be set.
           All four portrait states use the same `ring-2 ring-inset`, so picking
           a champion swaps the fill without nudging the row by the ring width. -->
      <div class="flex items-center gap-3 sm:gap-4">
        <SkeletonImage
          v-if="playedChampion"
          :src="playedChampion.iconUrl"
          :alt="playedChampion.name"
          :width="PORTRAIT_PX"
          :height="PORTRAIT_PX"
          class="size-18 shrink-0 rounded-xl ring-2 ring-inset ring-primary"
        />
        <div
          v-else
          class="flex size-18 shrink-0 items-center justify-center rounded-xl bg-muted ring-2 ring-inset ring-primary/40"
        >
          <UIcon
            name="i-lucide-user-round"
            class="size-7 text-dimmed"
          />
        </div>
        <div class="min-w-0 flex-1 space-y-1.5">
          <p class="stat-label text-primary">
            You play
          </p>
          <ChampionPicker
            :champions="champions"
            :champion-id="playedChampionId"
            placeholder="Choose your champion"
            size="lg"
            trigger-class="w-full"
            @update:champion-id="$emit('update:playedChampionId', $event)"
          />
        </div>
      </div>

      <!-- The chip is the whole separator: on a dark surface a 1px rule between
           two panels of the same fill is invisible anyway, and the grid gap
           already does the separating. -->
      <div class="flex items-center justify-center">
        <!-- Not `stat-label`: this is a marker between two 72px portraits and
             needs the size the 10px micro-label doesn't have. -->
        <span class="rounded-full border border-accented bg-muted px-2.5 py-1 font-mono text-xs font-semibold uppercase tracking-widest text-muted">
          vs
        </span>
      </div>

      <div class="flex items-center gap-3 sm:gap-4">
        <SkeletonImage
          v-if="opponentChampion"
          :src="opponentChampion.iconUrl"
          :alt="opponentChampion.name"
          :width="PORTRAIT_PX"
          :height="PORTRAIT_PX"
          class="size-18 shrink-0 rounded-xl ring-2 ring-inset ring-accented"
        />
        <div
          v-else
          class="flex size-18 shrink-0 items-center justify-center rounded-xl bg-muted ring-2 ring-inset ring-accented"
        >
          <UIcon
            name="i-lucide-swords"
            class="size-7 text-dimmed"
          />
        </div>
        <div class="min-w-0 flex-1 space-y-1.5">
          <p class="stat-label">
            Role opponent
          </p>
          <ChampionPicker
            :champions="champions"
            :champion-id="opponentChampionId"
            placeholder="Any opponent"
            size="lg"
            trigger-class="w-full"
            @update:champion-id="$emit('update:opponentChampionId', $event)"
          />
        </div>
      </div>
    </div>
  </section>
</template>
