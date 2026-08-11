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
 * Two portraits facing each other, and nothing else. The picks are made by
 * clicking a portrait (`ChampionSlot` opens a champion search); the role, which
 * gates every fetch on the page, sits on its own header row above them. The
 * select fields this replaced were the loudest thing in the stage — a wide
 * combobox wrapping a single word — while the portrait beside each one carried
 * no interaction at all.
 *
 * Near-textless on purpose (#1067): the only words left are the picked role,
 * which the icon-only strip cannot express, and the champion names under filled
 * tiles. Ownership is carried by the accent alone — your portrait's ring is
 * `primary`, the opponent's neutral — and not by a tinted panel: the former
 * `bg-primary/5` + `ring-primary/25` side panel was a rose-gold *surface* tint,
 * which the design system no longer allows.
 *
 * Purely presentational: the page owns the draft state, this component only
 * emits picks.
 */
defineProps<{
  champions: ChampionStaticListItem[]
  playedChampionId: number | null
  playedPosition: ChampionPosition | null
  opponentChampionId: number | null
}>()

defineEmits<{
  'update:playedChampionId': [value: number | null]
  'update:playedPosition': [value: ChampionPosition | null]
  'update:opponentChampionId': [value: number | null]
}>()
</script>

<template>
  <section
    class="surface space-y-5 rounded-2xl p-4 sm:p-6"
    aria-label="Matchup"
  >
    <!-- Role first: it gates the whole page (no build is fetched without it)
         and it used to hide under the champion select. The strip is icons only,
         so the picked role is named in words beside it — that name is the only
         text in the block, which is why it earns its place. -->
    <div class="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-default pb-4">
      <RolePicker
        :position="playedPosition"
        hide-all
        @update:position="$emit('update:playedPosition', $event)"
      />
      <p
        v-if="playedPosition"
        class="text-xs text-muted"
      >
        {{ POSITION_BY_VALUE.get(playedPosition)?.label ?? playedPosition }}
      </p>
    </div>

    <div class="flex items-center justify-center gap-5 py-2 sm:gap-12">
      <BuilderChampionSlot
        :champions="champions"
        :champion-id="playedChampionId"
        title="Choose your champion"
        accent
        @update:champion-id="$emit('update:playedChampionId', $event)"
      />

      <UIcon
        name="i-lucide-swords"
        class="size-6 shrink-0 text-dimmed sm:size-7"
      />

      <BuilderChampionSlot
        :champions="champions"
        :champion-id="opponentChampionId"
        title="Choose the role opponent"
        @update:champion-id="$emit('update:opponentChampionId', $event)"
      />
    </div>
  </section>
</template>
