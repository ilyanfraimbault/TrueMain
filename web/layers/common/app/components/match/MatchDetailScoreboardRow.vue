<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'
import { formatPercentage } from '#shared/utils/ddragon'
import { championDisplayName } from '#shared/utils/champion-name'
import { formatKda } from '#shared/utils/kda'

// One player of the scoreboard. The portrait, the spells and runes and the
// inventory are the match row's own columns (`MatchRowPortrait`,
// `MatchRowSpellsRunes`, `MatchItemGrid`), so a game reads the same collapsed
// and open; the rest is what only the detail carries.
const props = defineProps<{
  participant: MatchDetailParticipant
  champion: ChampionStaticListItem | null
  /** Highest damage on the side — the bar's full width. */
  maxDamage: number
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
}>()

const championName = computed(() => championDisplayName(props.participant.championId, props.champion))
</script>

<template>
  <li class="flex items-center gap-2 px-3 py-2">
    <!-- The scoreboard names the level, not the role: no `position`. -->
    <MatchRowPortrait
      :icon-url="champion?.iconUrl ?? null"
      :champion-name="championName"
      :position="null"
      :champion-level="participant.champLevel"
      :champion-id="participant.championId"
      :size="40"
    />
    <MatchRowSpellsRunes :self="participant" :summoner-spells="summonerSpells" :rune-tree="runeTree" />
    <MatchDetailScoreboardIdentity :participant="participant" class="flex-[1.2]" />

    <div class="flex w-[4.5rem] shrink-0 flex-col items-center text-xs">
      <span class="font-semibold tabular-nums">
        {{ participant.kills }}<span class="text-muted/70">/</span><span class="text-red-400">{{ participant.deaths }}</span><span class="text-muted/70">/</span>{{ participant.assists }}
      </span>
      <span class="text-[10px] text-muted tabular-nums">{{ formatKda(participant) }}</span>
    </div>

    <div class="flex w-[3.5rem] shrink-0 flex-col items-center text-[10px] text-muted tabular-nums">
      <span>{{ participant.cs }} CS</span>
      <span>{{ formatPercentage(participant.killParticipation, 0) }} KP</span>
    </div>

    <div class="hidden shrink-0 sm:block">
      <MatchDetailScoreboardDamage
        :damage="participant.totalDamageDealtToChampions"
        :damage-per-min="participant.damagePerMin"
        :max-damage="maxDamage"
      />
    </div>

    <!-- Right cluster, edge-aligned by one `ml-auto` so the score stays flush
         right even where the items are hidden. -->
    <div class="ml-auto flex shrink-0 items-center gap-2">
      <div class="hidden md:block">
        <MatchItemGrid
          :item-ids="participant.items"
          :trinket-item-id="participant.trinketItemId"
          :role-bound-item-id="participant.roleBoundItemId"
          :items="items"
          :size="20"
        />
      </div>
      <MatchDetailScoreboardScore :participant="participant" />
    </div>
  </li>
</template>
