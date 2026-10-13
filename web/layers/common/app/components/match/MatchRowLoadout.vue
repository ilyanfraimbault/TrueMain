<script setup lang="ts">
import type { MatchSummarySelf } from '#shared/types/matches'
import type {
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'

// Build of a match row as one strip: the two summoner spells over each other,
// the keystone over the secondary tree, then the inventory the scoreboard draws
// too (`MatchItemGrid`).
const props = defineProps<{
  self: MatchSummarySelf
  items: Record<number, StaticItemData>
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
    <MatchItemGrid
      class="ml-0.5"
      :item-ids="self.items"
      :trinket-item-id="self.trinketItemId"
      :role-bound-item-id="self.roleBoundItemId"
      :items="items"
      :size="20"
    />
  </div>
</template>
