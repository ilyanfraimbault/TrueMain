<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'
import { formatKilo } from '#common/utils/match-detail-format'
import { resultTone } from '#common/utils/result-tone'

// One side of the Overview tab: a header with the side's result and real team
// totals (no fabricated objective/score data), then one `MatchDetailScoreboardRow`
// per player.
const props = defineProps<{
  participants: MatchDetailParticipant[]
  teamId: number
  win: boolean
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
}>()

const championById = computed(() => new Map(props.champions.map(c => [c.championId, c])))
const tone = computed(() => resultTone(props.win))
const sideLabel = computed(() => (props.teamId === 100 ? 'Blue' : 'Red'))

// Highest damage across the rendered side — drives every row's damage bar.
const maxDamage = computed(() =>
  props.participants.reduce((max, p) => Math.max(max, p.totalDamageDealtToChampions), 0),
)

const teamKda = computed(() => props.participants.reduce(
  (acc, p) => {
    acc.k += p.kills
    acc.d += p.deaths
    acc.a += p.assists
    return acc
  },
  { k: 0, d: 0, a: 0 },
))
const teamGold = computed(() => props.participants.reduce((sum, p) => sum + p.goldEarned, 0))
</script>

<template>
  <section class="surface overflow-hidden rounded-md">
    <header
      class="flex items-center justify-between px-3 py-2 text-xs font-semibold"
      :class="tone.text"
    >
      <span>{{ sideLabel }} team — {{ tone.label }}</span>
      <span class="flex items-center gap-2 text-muted tabular-nums">
        <span>
          {{ teamKda.k }}<span class="text-muted/60">/</span>{{ teamKda.d }}<span class="text-muted/60">/</span>{{ teamKda.a }}
        </span>
        <span class="text-muted/50">·</span>
        <span class="inline-flex items-center gap-1">
          <UIcon name="i-lucide-coins" class="size-3 text-amber-400/80" />
          {{ formatKilo(teamGold) }}
        </span>
      </span>
    </header>

    <ul class="divide-y divide-default/40">
      <MatchDetailScoreboardRow
        v-for="p in participants"
        :key="p.participantId"
        :participant="p"
        :champion="championById.get(p.championId) ?? null"
        :max-damage="maxDamage"
        :items="items"
        :summoner-spells="summonerSpells"
        :rune-tree="runeTree"
      />
    </ul>
  </section>
</template>
