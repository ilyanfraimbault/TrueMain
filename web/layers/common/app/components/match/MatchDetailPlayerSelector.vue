<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { getPositionIconUrl } from '#shared/utils/ddragon'

// The Build tab's player picker: blue side · vs · red side, one portrait per
// player badged with their lane; the picked one is ringed, the others dimmed.
const props = defineProps<{
  blueTeam: MatchDetailParticipant[]
  redTeam: MatchDetailParticipant[]
  champions: ChampionStaticListItem[]
}>()

/** The picked player's participantId. */
const selected = defineModel<number | null>({ required: true })

const championById = computed(() => new Map(props.champions.map(c => [c.championId, c])))
const championName = (championId: number) => championById.value.get(championId)?.name ?? `Champion ${championId}`
</script>

<template>
  <!-- Sized off the *row's* width (`MatchRow` is the `@container`), never the
       viewport: the same row renders full-bleed on a profile and inside a 2xl
       drawer. Ten 56px portraits plus their gaps need ~694px, so the two sides
       only sit side by side from `@3xl`; `@sm` shrinks them again on a phone.
       `p-1` is load-bearing: the scroller clips on both axes, and the picked
       portrait's `ring-2` and the lane badge's `-bottom-1 -left-1` both bleed
       outside the button box. -->
  <div class="flex flex-col items-center gap-2 overflow-x-auto p-1 pb-2 @3xl:flex-row @3xl:justify-between @3xl:gap-3">
    <template v-for="(team, teamIdx) in [blueTeam, redTeam]" :key="`team-${teamIdx}`">
      <div class="flex shrink-0 items-center gap-2">
        <button
          v-for="p in team"
          :key="`sel-${p.participantId}`"
          type="button"
          class="relative shrink-0 rounded transition-all"
          :class="p.participantId === selected ? 'ring-2 ring-primary' : 'opacity-60 hover:opacity-100'"
          :title="championName(p.championId)"
          :aria-label="`Show ${championName(p.championId)} details`"
          :aria-pressed="p.participantId === selected"
          @click="selected = p.participantId"
        >
          <SkeletonImage
            :src="championById.get(p.championId)?.iconUrl ?? null"
            :alt="championName(p.championId)"
            class="size-11 rounded @sm:size-14"
          />
          <SkeletonImage
            v-if="p.teamPosition"
            :src="getPositionIconUrl(p.teamPosition)"
            :alt="p.teamPosition"
            class="absolute -bottom-1 -left-1 size-5 rounded-full bg-default p-0.5 ring-1 ring-default"
            aria-hidden="true"
            transparent
          />
        </button>
      </div>

      <span
        v-if="teamIdx === 0"
        class="shrink-0 select-none text-2xl font-semibold text-muted @3xl:mx-2"
        aria-hidden="true"
      >vs</span>
    </template>
  </div>
</template>
