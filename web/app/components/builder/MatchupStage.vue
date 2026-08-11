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
 * Two portraits facing each other, the role strip centred underneath, and no
 * panel around any of it. The picks are made by clicking a portrait
 * (`ChampionSlot` opens a champion search); the select fields this replaced were
 * the loudest thing in the stage — a wide combobox wrapping a single word —
 * while the portrait beside each one carried no interaction at all.
 *
 * **No surface on purpose** (#1069). Every other block on the page is a card, so
 * a card here made the matchup one panel among three rather than the thing the
 * page is about. Two lit portraits on the page background read as the subject;
 * the same two inside a bordered box read as a form.
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
    class="space-y-5 py-2"
    aria-label="Matchup"
  >
    <div class="flex items-center justify-center gap-5 sm:gap-12">
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

    <!-- Role under the two picks, on the same centre line. It gates every fetch
         on the page, but it is one choice among five against ~170 champions —
         reading it second matches the order the picks are actually made in.
         The strip is icons only, so the picked role is named in words beside
         it; that name is the only text here, which is why it earns its place. -->
    <div class="flex justify-center">
      <div class="relative">
        <RolePicker
          :position="playedPosition"
          hide-all
          @update:position="$emit('update:playedPosition', $event)"
        />
        <!-- Hung off the strip rather than laid out beside it: in a centred
             flex row the label's width would shift the strip off the portraits'
             centre line, and shift it again on every role whose name is a
             different length. -->
        <p
          v-if="playedPosition"
          class="absolute start-full top-1/2 ms-3 -translate-y-1/2 whitespace-nowrap text-xs text-muted"
        >
          {{ POSITION_BY_VALUE.get(playedPosition)?.label ?? playedPosition }}
        </p>
      </div>
    </div>
  </section>
</template>
