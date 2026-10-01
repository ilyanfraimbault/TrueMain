<!--
  One side of an opened game, compact: the site's scoreboard
  (web/app/components/match/MatchDetailScoreboard.vue) — champion and level,
  name, K/D/A, CS and kill participation, damage against the side's top
  dealer — with each player's build laid out as on the match row above it:
  spells over each other, keystone over the secondary tree, the site's item
  grid. No card of its own; the player's own line is marked in the primary
  colour.
-->
<script setup lang="ts">
import type { MatchDetailParticipant } from '~~/shared/types/match-detail'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

const props = defineProps<{
  players: MatchDetailParticipant[]
  teamId: number
  selfId: number | null
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
}>()

const championById = computed(() => new Map(props.champions.map(c => [c.championId, c])))
const win = computed(() => props.players[0]?.win ?? false)
const totals = computed(() => props.players.reduce(
  (sum, p) => ({ k: sum.k + p.kills, d: sum.d + p.deaths, a: sum.a + p.assists, gold: sum.gold + p.goldEarned }),
  { k: 0, d: 0, a: 0, gold: 0 },
))
const maxDamage = computed(() => Math.max(1, ...props.players.map(p => p.totalDamageDealtToChampions)))

const thousands = (value: number) => (value >= 1000 ? `${(value / 1000).toFixed(1)}k` : `${value}`)
const kda = (p: MatchDetailParticipant) => (p.deaths === 0 ? 'Perfect' : ((p.kills + p.assists) / p.deaths).toFixed(1))
</script>

<template>
  <section>
    <header class="flex items-center gap-2 px-1 pb-1 text-[11px]">
      <span class="font-semibold" :class="win ? 'text-sky-300' : 'text-red-400'">
        {{ teamId === 100 ? 'Blue' : 'Red' }} side · {{ win ? 'Victory' : 'Defeat' }}
      </span>
      <span class="ml-auto tabular-nums text-dimmed">
        {{ totals.k }} / {{ totals.d }} / {{ totals.a }} · {{ thousands(totals.gold) }} gold
      </span>
    </header>

    <ul class="flex flex-col">
      <li
        v-for="p in players"
        :key="p.participantId"
        class="grid h-10 grid-cols-[1.75rem_minmax(0,1fr)_4rem_3.25rem_4.5rem_auto] items-center gap-2.5 rounded px-1"
        :class="p.participantId === selfId ? 'bg-primary/8' : ''"
      >
        <div class="relative">
          <SkeletonImage
            :src="championById.get(p.championId)?.iconUrl ?? null"
            :alt="championById.get(p.championId)?.name ?? ''"
            :title="championById.get(p.championId)?.name"
            :width="28"
            :height="28"
            class="size-7 rounded"
          />
          <span class="absolute -bottom-1 -right-1 rounded-sm bg-default px-0.5 text-[8px] font-bold leading-3 tabular-nums text-muted">{{ p.champLevel }}</span>
        </div>

        <span
          class="truncate text-xs"
          :class="p.participantId === selfId ? 'font-semibold text-highlighted' : 'text-muted'"
        >{{ p.gameName ?? p.summonerName }}</span>

        <div class="leading-none tabular-nums">
          <p class="text-xs font-medium text-default">
            {{ p.kills }}<span class="text-dimmed">/</span><span class="text-red-400">{{ p.deaths }}</span><span class="text-dimmed">/</span>{{ p.assists }}
          </p>
          <p class="mt-0.5 text-[10px] text-dimmed">{{ kda(p) }} KDA</p>
        </div>

        <div class="text-right leading-none tabular-nums">
          <p class="text-[11px] text-default">{{ p.cs }} CS</p>
          <p class="mt-0.5 text-[10px] text-dimmed">{{ Math.round(p.killParticipation * 100) }}% KP</p>
        </div>

        <div class="flex flex-col gap-1" :title="`${p.totalDamageDealtToChampions} damage to champions`">
          <span class="text-[11px] leading-none tabular-nums text-default">{{ thousands(p.totalDamageDealtToChampions) }}</span>
          <div class="h-1 overflow-hidden rounded-full bg-white/6">
            <div
              class="h-full rounded-full"
              :class="p.totalDamageDealtToChampions === maxDamage ? 'bg-primary' : 'bg-primary/40'"
              :style="{ width: `${Math.round((p.totalDamageDealtToChampions / maxDamage) * 100)}%` }"
            />
          </div>
        </div>

        <div class="flex items-center gap-1">
          <div class="flex flex-col gap-0.5">
            <GameTooltipSummonerSpellIcon :spell="summonerSpells[p.summoner1Id] ?? null" :width="16" :height="16" class="size-4 rounded-sm" />
            <GameTooltipSummonerSpellIcon :spell="summonerSpells[p.summoner2Id] ?? null" :width="16" :height="16" class="size-4 rounded-sm" />
          </div>
          <div class="flex flex-col items-center gap-0.5">
            <GameTooltipPerkIcon :perk="runeTree.perks[p.keystoneId] ?? null" :width="16" :height="16" class="size-4 rounded-full bg-black/40" />
            <GameTooltipPerkStyleIcon :style="runeTree.perkStyles[p.subStyleId] ?? null" :width="12" :height="12" class="size-3" />
          </div>
          <MatchItemGrid
            class="ml-0.5"
            :item-ids="p.items"
            :trinket-item-id="p.trinketItemId"
            :role-bound-item-id="p.roleBoundItemId"
            :items="items"
            :size="16"
          />
        </div>
      </li>
    </ul>
  </section>
</template>
