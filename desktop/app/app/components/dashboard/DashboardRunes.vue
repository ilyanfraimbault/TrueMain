<!--
  The rune pages of an opened game, compact: the site's Runes tab
  (web/app/components/match/MatchDetailRunePage.vue) — a small tile a player,
  the portrait with the keystone as its badge beside the primary tree's three
  runes over the secondary's two and the shards — five tiles a side, the
  player's own ringed. The name is in the portrait's tooltip.
-->
<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type { ChampionStaticListItem, RuneTreeResponse } from '#shared/types/static-data'

const props = defineProps<{
  sides: { teamId: number, players: MatchDetailParticipant[] }[]
  champions: ChampionStaticListItem[]
  runeTree: RuneTreeResponse
}>()

const championById = computed(() => new Map(props.champions.map(c => [c.championId, c])))
const perk = (id: number) => props.runeTree.perks[id] ?? null
const primary = (p: MatchDetailParticipant) => p.runes.filter(r => r.styleId === p.primaryStyleId && r.selectionIndex > 0)
const secondary = (p: MatchDetailParticipant) => p.runes.filter(r => r.styleId === p.subStyleId)
const shards = (p: MatchDetailParticipant) => [p.statPerkOffense, p.statPerkFlex, p.statPerkDefense].filter(id => id > 0)
const who = (p: MatchDetailParticipant) =>
  `${championById.value.get(p.championId)?.name ?? ''} — ${p.gameName ?? p.summonerName}`
</script>

<template>
  <div class="flex flex-col gap-2">
    <section v-for="side in sides" :key="side.teamId">
      <p class="px-1 pb-1 text-[11px] font-semibold" :class="side.players[0]?.win ? 'text-sky-300' : 'text-red-400'">
        {{ side.teamId === 100 ? 'Blue' : 'Red' }} side · {{ side.players[0]?.win ? 'Victory' : 'Defeat' }}
      </p>
      <div class="grid grid-cols-5 gap-1">
        <div
          v-for="p in side.players"
          :key="p.participantId"
          class="flex items-center justify-center gap-2 rounded-md bg-white/[0.025] py-1.5"
        >
          <div class="relative shrink-0">
            <SkeletonImage
              :src="championById.get(p.championId)?.iconUrl ?? null"
              :alt="who(p)"
              :title="who(p)"
              :width="28"
              :height="28"
              class="size-7 rounded"
            />
            <GameTooltipPerkIcon
              :perk="perk(p.keystoneId)"
              :width="16"
              :height="16"
              class="absolute -bottom-1 -right-1.5 size-4 rounded-full bg-ink-950 ring-1 ring-default"
            />
          </div>
          <div class="flex flex-col gap-0.5">
            <div class="flex gap-0.5">
              <GameTooltipPerkIcon v-for="rune in primary(p)" :key="rune.perkId" :perk="perk(rune.perkId)" :width="16" :height="16" class="size-4 rounded-full bg-black/40" />
            </div>
            <div class="flex items-center gap-0.5">
              <GameTooltipPerkIcon v-for="rune in secondary(p)" :key="rune.perkId" :perk="perk(rune.perkId)" :width="14" :height="14" class="size-3.5 rounded-full bg-black/40" />
              <GameTooltipPerkIcon v-for="(shard, index) in shards(p)" :key="`shard-${index}`" :perk="perk(shard)" :width="10" :height="10" class="size-2.5 rounded-full bg-black/40" />
            </div>
          </div>
        </div>
      </div>
    </section>
  </div>
</template>
