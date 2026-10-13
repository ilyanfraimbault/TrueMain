<script setup lang="ts">
import type { MatchSummarySelf } from '#shared/types/matches'
import type {
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'

// Build of a match row as one strip: the spells and runes (`MatchRowSpellsRunes`),
// then the inventory (`MatchItemGrid`) — the two blocks the scoreboard draws too,
// split apart there.
defineProps<{
  self: MatchSummarySelf
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
}>()
</script>

<template>
  <div class="flex shrink-0 items-center gap-1">
    <MatchRowSpellsRunes :self="self" :summoner-spells="summonerSpells" :rune-tree="runeTree" />
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
