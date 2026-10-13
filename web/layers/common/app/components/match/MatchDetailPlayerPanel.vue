<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type { StaticItemData } from '#shared/types/static-data'

/**
 * Single-player breakdown of the Build tab: laning @15 and the per-minute
 * stats side by side, then the full build order and skill order.
 */
defineProps<{
  participant: MatchDetailParticipant
  items: Record<number, StaticItemData>
}>()
</script>

<template>
  <div class="flex flex-col gap-3">
    <div class="grid gap-3 sm:grid-cols-2">
      <MatchDetailLaningStats :participant="participant" />
      <MatchDetailPerMinuteStats :participant="participant" />
    </div>
    <MatchDetailBuildOrder :item-events="participant.itemEvents" :items="items" />
    <MatchDetailSkillOrder :champion-id="participant.championId" :skill-events="participant.skillEvents" />
  </div>
</template>
