<script setup lang="ts">
import type { MatchDetailParticipant } from '~~/shared/types/match-detail'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

const props = defineProps<{
  participants: MatchDetailParticipant[]
  teamId: number
  win: boolean
  nameTag: string
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
}>()

function champ(id: number) {
  return props.champions.find(c => c.championId === id) ?? null
}

function champName(id: number) {
  return champ(id)?.name ?? `Champion ${id}`
}

// Highest damage across the rendered team — drives the damage bar fill.
const maxDamage = computed(() =>
  props.participants.reduce((max, p) => Math.max(max, p.totalDamageDealtToChampions), 0),
)

function damagePct(value: number) {
  return maxDamage.value > 0 ? Math.round((value / maxDamage.value) * 100) : 0
}

function kda(p: MatchDetailParticipant) {
  if (p.deaths === 0) return 'Perfect'
  return ((p.kills + p.assists) / p.deaths).toFixed(2)
}

// Six inventory slots (items 0..5) left-aligned; trinket is items[6].
function inventory(p: MatchDetailParticipant) {
  return p.items.slice(0, 6)
}

const sideLabel = computed(() => (props.teamId === 100 ? 'Blue' : 'Red'))
</script>

<template>
  <section class="glass overflow-hidden rounded-md border border-default/60 bg-elevated/40">
    <header
      class="flex items-center justify-between px-3 py-2 text-xs font-semibold"
      :class="win ? 'text-sky-400' : 'text-red-400'"
    >
      <span>{{ sideLabel }} team — {{ win ? 'Victory' : 'Defeat' }}</span>
      <span class="text-muted">KDA · CS · KP · Damage</span>
    </header>

    <ul class="divide-y divide-default/40">
      <li
        v-for="p in participants"
        :key="p.participantId"
        class="flex items-center gap-2 px-3 py-2"
      >
        <!-- Champion + level -->
        <div class="relative shrink-0">
          <ChampionLink
            :champion-id="p.championId"
            :name="champName(p.championId)"
            :icon-url="champ(p.championId)?.iconUrl"
            class="block size-10 overflow-hidden rounded"
          />
          <span
            class="absolute -bottom-1 -right-1 inline-flex size-4 items-center justify-center rounded-full bg-default text-[10px] font-bold leading-none ring-1 ring-default"
          >
            {{ p.champLevel }}
          </span>
        </div>

        <!-- Summoner spells -->
        <div class="flex shrink-0 flex-col gap-0.5">
          <GameTooltipSummonerSpellIcon
            :spell="summonerSpells[p.summoner1Id] ?? null"
            :width="18"
            :height="18"
            class="size-[18px] rounded"
          />
          <GameTooltipSummonerSpellIcon
            :spell="summonerSpells[p.summoner2Id] ?? null"
            :width="18"
            :height="18"
            class="size-[18px] rounded"
          />
        </div>

        <!-- Keystone + secondary tree -->
        <div class="flex shrink-0 flex-col items-center gap-0.5">
          <GameTooltipPerkIcon
            :perk="p.keystoneId ? runeTree.perks[p.keystoneId] ?? null : null"
            :width="18"
            :height="18"
            class="size-[18px] rounded-full bg-black/40"
          />
          <GameTooltipPerkStyleIcon
            :style="p.subStyleId ? runeTree.perkStyles[p.subStyleId] ?? null : null"
            :width="14"
            :height="14"
            class="size-[14px]"
          />
        </div>

        <!-- Identity + rank -->
        <div class="flex min-w-0 flex-[1.2] flex-col">
          <NuxtLink
            v-if="p.gameName"
            :to="`/truemains/${encodeURIComponent(`${p.gameName}-${p.tagLine ?? ''}`)}`"
            class="truncate text-xs font-medium text-default hover:underline"
          >
            {{ p.gameName }}
          </NuxtLink>
          <span v-else class="truncate text-xs font-medium text-muted">{{ p.summonerName }}</span>
          <span v-if="p.rank" class="flex items-center gap-1 text-[10px] text-muted">
            <RankIcon :tier="p.rank.tier" :size="14" />
            {{ p.rank.tier }} {{ p.rank.division }}
          </span>
        </div>

        <!-- KDA -->
        <div class="flex w-[4.5rem] shrink-0 flex-col items-center text-xs">
          <span class="font-semibold tabular-nums">
            {{ p.kills }}<span class="text-muted/70">/</span><span class="text-red-400">{{ p.deaths }}</span><span class="text-muted/70">/</span>{{ p.assists }}
          </span>
          <span class="text-[10px] text-muted tabular-nums">{{ kda(p) }} KDA</span>
        </div>

        <!-- CS + KP -->
        <div class="flex w-[3.5rem] shrink-0 flex-col items-center text-[10px] text-muted tabular-nums">
          <span>{{ p.cs }} CS</span>
          <span>{{ Math.round(p.killParticipation * 100) }}% KP</span>
        </div>

        <!-- Damage bar -->
        <div class="hidden w-[5rem] shrink-0 flex-col gap-0.5 sm:flex">
          <span class="text-[10px] tabular-nums text-muted">{{ p.totalDamageDealtToChampions.toLocaleString() }}</span>
          <div class="h-1.5 w-full overflow-hidden rounded-full bg-default/40">
            <div
              class="h-full rounded-full"
              :class="win ? 'bg-sky-500' : 'bg-red-500'"
              :style="{ width: `${damagePct(p.totalDamageDealtToChampions)}%` }"
            />
          </div>
        </div>

        <!-- Items -->
        <div class="ml-auto hidden shrink-0 items-center gap-1 md:flex">
          <div class="flex gap-0.5">
            <template v-for="(itemId, idx) in inventory(p)" :key="`item-${idx}`">
              <div v-if="!itemId" class="size-5 shrink-0" aria-hidden="true" />
              <GameTooltipItemIcon
                v-else
                :item="items[itemId] ?? null"
                :width="20"
                :height="20"
                class="size-5 rounded"
              />
            </template>
          </div>
          <GameTooltipItemIcon
            :item="p.trinketItemId ? items[p.trinketItemId] ?? null : null"
            :width="20"
            :height="20"
            class="size-5 rounded-full"
          />
        </div>
      </li>
    </ul>
  </section>
</template>
