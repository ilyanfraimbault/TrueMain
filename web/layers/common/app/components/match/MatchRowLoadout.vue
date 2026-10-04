<script setup lang="ts">
import type { MatchSummarySelf } from '#shared/types/matches'
import type {
  RuneTreeResponse,
  StaticItemData,
  StaticPerkData,
  StaticPerkStyleData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'

// Loadout strip of a match-history row: summoner spells + runes + item block,
// tightly grouped (small internal gaps) so spells → runes → items read
// left-to-right as one continuous build. Summoners and runes each stack 2-high
// to match the two rows of the item grid.
const props = defineProps<{
  self: MatchSummarySelf
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
}>()

const summoner1: ComputedRef<StaticSummonerSpellData | null> = computed(
  () => props.summonerSpells[props.self.summoner1Id] ?? null,
)
const summoner2: ComputedRef<StaticSummonerSpellData | null> = computed(
  () => props.summonerSpells[props.self.summoner2Id] ?? null,
)

const keystone: ComputedRef<StaticPerkData | null> = computed(() => {
  if (!props.self.keystoneId) return null
  return props.runeTree.perks[props.self.keystoneId] ?? null
})

const subStyle: ComputedRef<StaticPerkStyleData | null> = computed(() => {
  if (!props.self.subStyleId) return null
  return props.runeTree.perkStyles[props.self.subStyleId] ?? null
})
</script>

<template>
  <!-- Below @md the loadout wraps onto its own line (`order-last` keeps
       it under the row rather than between the KDA and the accolade).
       A phone can't hold meta + portrait + KDA + a 9rem build strip on
       one line, and the alternative — dropping half the build — throws
       away the thing the row exists to show. -->
  <div class="order-last flex w-full items-center justify-center @md:order-none @md:w-auto @md:shrink-0">
    <div class="flex shrink-0 items-center gap-1">
      <div class="flex flex-col gap-0.5">
        <GameTooltipSummonerSpellIcon
          :spell="summoner1"
          :width="22"
          :height="22"
          loading="lazy"
          class="size-[18px] rounded @2xl:size-[22px]"
        />
        <GameTooltipSummonerSpellIcon
          :spell="summoner2"
          :width="22"
          :height="22"
          loading="lazy"
          class="size-[18px] rounded @2xl:size-[22px]"
        />
      </div>
      <div class="flex flex-col items-center gap-0.5">
        <GameTooltipPerkIcon
          :perk="keystone"
          :width="22"
          :height="22"
          loading="lazy"
          class="size-[18px] rounded-full bg-black/40 @2xl:size-[22px]"
        />
        <GameTooltipPerkStyleIcon
          :style="subStyle"
          :width="18"
          :height="18"
          loading="lazy"
          class="size-[15px] @2xl:size-[18px]"
        />
      </div>

      <!-- Items: the shared inventory block the scoreboard draws per player. -->
      <MatchItemGrid
        :item-ids="self.items"
        :trinket-item-id="self.trinketItemId"
        :role-bound-item-id="self.roleBoundItemId"
        :items="items"
      />
    </div>
  </div>
</template>
