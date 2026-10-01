<!--
  Inside an opened match row: the site's match-detail panel
  (web/app/components/match/MatchDetailPanel.vue) — the same three views over
  the same payload, the scoreboard, one player's build and the rune pages —
  drawn sober and compact on the row's own surface, as the product owner asked
  (2026-09-30): no recessed well, no card inside the card, rows a third of the
  site's height. The payload comes from the client through `useMatchDetail`
  (`useSiteShims.ts`).
-->
<script setup lang="ts">
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'

const props = defineProps<{
  nameTag: string
  matchId: string
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
  selfChampionId: number
  selfTeamId: number
}>()

const { data: detail, isLoading, notFound } = useMatchDetail(() => props.nameTag, () => props.matchId)

const participants = computed(() => detail.value?.participants ?? [])
const self = computed(() => participants.value.find(p => p.championId === props.selfChampionId && p.teamId === props.selfTeamId) ?? null)
/** The player's side first, as they lived the game. */
const sides = computed(() => {
  const mine = props.selfTeamId
  return [mine, mine === 100 ? 200 : 100].map(teamId => ({
    teamId,
    players: participants.value.filter(p => p.teamId === teamId),
  })).filter(side => side.players.length)
})

const VIEWS = [
  { value: 'scoreboard', label: 'Scoreboard' },
  { value: 'build', label: 'Build' },
  { value: 'runes', label: 'Runes' },
] as const
const view = ref<(typeof VIEWS)[number]['value']>('scoreboard')
</script>

<template>
  <div class="border-t border-default/60 px-3 pb-3">
    <div v-if="isLoading && !detail" class="flex flex-col gap-1 pt-2">
      <USkeleton class="h-6 w-56 rounded-md" />
      <USkeleton v-for="index in 5" :key="index" class="h-7 w-full rounded" />
    </div>

    <p v-else-if="notFound || !detail" class="py-4 text-center text-xs text-muted">
      This game's details are not available from the client.
    </p>

    <template v-else>
      <!-- The site's link tabs, across the whole row. -->
      <div class="-mx-3 mb-2.5 flex border-b border-default/60" role="tablist">
        <button
          v-for="option in VIEWS"
          :key="option.value"
          type="button"
          role="tab"
          :aria-selected="view === option.value"
          class="relative flex-1 py-2 text-xs font-medium transition-colors"
          :class="view === option.value ? 'text-highlighted' : 'text-muted hover:text-highlighted'"
          @click="view = option.value"
        >
          {{ option.label }}
          <span v-if="view === option.value" class="absolute inset-x-0 -bottom-px h-0.5 rounded-full bg-primary" />
        </button>
      </div>

      <div v-show="view === 'scoreboard'" class="flex flex-col gap-2">
        <DashboardScoreboard
          v-for="side in sides"
          :key="side.teamId"
          :players="side.players"
          :team-id="side.teamId"
          :self-id="self?.participantId ?? null"
          :champions="champions"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree"
        />
      </div>

      <DashboardPlayerBuild
        v-if="view === 'build'"
        :participants="participants"
        :self-id="self?.participantId ?? null"
        :self-team-id="selfTeamId"
        :champions="champions"
        :items="items"
      />

      <DashboardRunes
        v-if="view === 'runes'"
        :sides="sides"
        :self-id="self?.participantId ?? null"
        :champions="champions"
        :rune-tree="runeTree"
      />
    </template>
  </div>
</template>
