<script setup lang="ts">
import type { MatchSummaryParticipant } from '#shared/types/matches'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { POSITION_BY_VALUE } from '#common/utils/positions'

// Team compositions of a match-history row. Both sides sorted into canonical
// role order (TOP → SUPPORT) so the two rows line up laner-vs-laner. Positions
// can be missing on old rows ingested before the field was exposed — those
// keep the server's participant order, so the columns never pretend to a
// pairing the data can't back.
const props = defineProps<{
  participants: MatchSummaryParticipant[]
  selfTeamId: number
  championById: Map<number, ChampionStaticListItem>
}>()

const POSITION_ORDER = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const

function sortByPosition(team: MatchSummaryParticipant[]): MatchSummaryParticipant[] {
  return [...team].sort((a, b) => {
    const ia = POSITION_ORDER.indexOf(a.position as typeof POSITION_ORDER[number])
    const ib = POSITION_ORDER.indexOf(b.position as typeof POSITION_ORDER[number])
    return (ia === -1 ? POSITION_ORDER.length : ia) - (ib === -1 ? POSITION_ORDER.length : ib)
  })
}

const allies = computed(() =>
  sortByPosition(props.participants.filter(p => p.teamId === props.selfTeamId)))
const enemies = computed(() =>
  sortByPosition(props.participants.filter(p => p.teamId !== props.selfTeamId)))

function participantTooltip(p: MatchSummaryParticipant): string {
  const champ = props.championById.get(p.championId)?.name ?? `Champion ${p.championId}`
  const role = p.position ? POSITION_BY_VALUE.get(p.position)?.label : null
  const who = p.gameName ? ` — ${p.gameName}${p.tagLine ? `#${p.tagLine}` : ''}` : ''
  return role ? `${champ} · ${role}${who}` : `${champ}${who}`
}
</script>

<template>
  <!-- Two horizontal rows of 5, allies over enemies, each sorted TOP →
       SUPPORT so a column pairs laner-vs-laner (ally above enemy = same
       role). Ten icons need ~7rem, which the row has from @xl once the
       secondary stats are held back to @3xl — narrower than that (a phone,
       a half-width card) they still drop.

       Sized with explicit width/height props, not a responsive `size-*`
       class: SkeletonImage reserves its box from those props via an inline
       style, which is what actually pins the item icons beside them to 24px
       at every width (their own `size-5 @2xl:size-6` classes never take
       effect — inline style always wins). Without matching props here these
       rendered at 20px until the row cleared the real @2xl breakpoint,
       visibly smaller than the items sitting right next to them. -->
  <div class="hidden shrink-0 flex-col gap-0.5 @xl:flex">
    <div class="flex gap-0.5">
      <SkeletonImage
        v-for="(p, idx) in allies"
        :key="`ally-${idx}`"
        :src="championById.get(p.championId)?.iconUrl ?? null"
        :alt="participantTooltip(p)"
        :title="participantTooltip(p)"
        :width="24"
        :height="24"
        loading="lazy"
        class="size-6 rounded"
      />
    </div>
    <div class="flex gap-0.5">
      <SkeletonImage
        v-for="(p, idx) in enemies"
        :key="`enemy-${idx}`"
        :src="championById.get(p.championId)?.iconUrl ?? null"
        :alt="participantTooltip(p)"
        :title="participantTooltip(p)"
        :width="24"
        :height="24"
        loading="lazy"
        class="size-6 rounded"
      />
    </div>
  </div>
</template>
