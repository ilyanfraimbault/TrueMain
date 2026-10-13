<script setup lang="ts">
import type { MatchSummarySelf } from '#shared/types/matches'
import type { RuneTreeResponse, StaticSummonerSpellData } from '#shared/types/static-data'

// What a player locked in before the game: the two summoner spells over each
// other, then the keystone over the secondary tree. The match row draws it at
// the head of its loadout (`MatchRowLoadout`), the scoreboard beside each
// player's portrait — both carry the same ids, so `self` takes either a row's
// self or a scoreboard participant.
const props = defineProps<{
  self: Pick<MatchSummarySelf, 'summoner1Id' | 'summoner2Id' | 'keystoneId' | 'subStyleId'>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
}>()

const spells = computed(() => [props.self.summoner1Id, props.self.summoner2Id]
  .map(id => props.summonerSpells[id] ?? null))
const keystone = computed(() => props.runeTree.perks[props.self.keystoneId] ?? null)
const subStyle = computed(() => props.runeTree.perkStyles[props.self.subStyleId] ?? null)
</script>

<template>
  <div class="flex shrink-0 items-center gap-1">
    <div class="flex flex-col gap-0.5">
      <GameTooltipSummonerSpellIcon
        v-for="(spell, index) in spells"
        :key="index"
        :spell="spell"
        :width="18"
        :height="18"
        loading="lazy"
        class="size-[18px] rounded"
      />
    </div>
    <div class="flex flex-col items-center gap-0.5">
      <GameTooltipPerkIcon :perk="keystone" :width="18" :height="18" loading="lazy" class="size-[18px] rounded-full bg-black/40" />
      <GameTooltipPerkStyleIcon :style="subStyle" :width="14" :height="14" loading="lazy" class="size-3.5" />
    </div>
  </div>
</template>
