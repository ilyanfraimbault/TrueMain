<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type { ChampionStaticListItem, StaticItemData } from '#shared/types/static-data'
import { resultTone } from '#common/utils/result-tone'

// The match detail's Build tab: the player picker, then the picked player's
// name, result and breakdown (`MatchDetailPlayerPanel`).
const props = defineProps<{
  blueTeam: MatchDetailParticipant[]
  redTeam: MatchDetailParticipant[]
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  /** Champion of the row's owner — the tab opens on that player. */
  selfChampionId?: number | null
}>()

// Null until the user picks someone; the computed falls back to the row
// owner's champion (so the tab opens on "you"), then the first participant.
const pickedId = ref<number | null>(null)

const selected = computed<MatchDetailParticipant | null>(() => {
  const list = [...props.blueTeam, ...props.redTeam]
  return list.find(p => p.participantId === pickedId.value)
    ?? (props.selfChampionId ? list.find(p => p.championId === props.selfChampionId) : undefined)
    ?? list[0]
    ?? null
})
const tone = computed(() => resultTone(selected.value?.win ?? false))
</script>

<template>
  <div class="flex flex-col gap-3">
    <MatchDetailPlayerSelector
      :model-value="selected?.participantId ?? null"
      :blue-team="blueTeam"
      :red-team="redTeam"
      :champions="champions"
      @update:model-value="pickedId = $event"
    />

    <div v-if="selected" class="flex flex-col gap-2">
      <div class="flex items-center gap-2 px-0.5">
        <span class="text-sm font-semibold text-default">
          {{ selected.gameName ?? selected.summonerName }}
        </span>
        <span class="ml-auto rounded px-1.5 py-0.5 text-[10px] font-semibold" :class="tone.chip">
          {{ tone.short }}
        </span>
      </div>
      <MatchDetailPlayerPanel
        :key="selected.participantId"
        :participant="selected"
        :items="items"
      />
    </div>
  </div>
</template>
