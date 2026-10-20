<script setup lang="ts">
import type { ChampionStaticListItem, RuneTreeResponse, StaticSummonerSpellData } from '#shared/types/static-data'
import { getQueueLabel } from '#common/utils/queues'
import { formatDuration, formatRelativeTime } from '#common/utils/relativeTime'

// A player's Live tab (#2103): the game they are in right now, read through
// spectator-v5 when the tab is opened and on "Check now" — never polled. Mounted
// only while the tab is open, so a profile view that stays on the history costs
// Riot nothing. The clock below ticks locally between two checks; it never asks
// the API anything.
const props = defineProps<{
  nameTag: string
  champions: ChampionStaticListItem[]
  summonerSpells: Record<number, StaticSummonerSpellData> | null | undefined
  runeTree: RuneTreeResponse | null | undefined
}>()

const { data: live, isLoading, isInitialLoading, error, check } = useTruemainLive(() => props.nameTag)

const championsById = computed(() => new Map(props.champions.map(champion => [champion.championId, champion])))

const now = ref(new Date())
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => { timer = setInterval(() => { now.value = new Date() }, 1000) })
onScopeDispose(() => clearInterval(timer))

const game = computed(() => live.value?.status === 'inGame' ? live.value.game : null)

// Riot's game time when it answered, plus what has passed since: the client's
// own clock, not a call per second.
const clock = computed(() => {
  const current = game.value
  if (!current || !current.startedAtUtc || !live.value) return null
  const sinceCheck = (now.value.getTime() - new Date(live.value.checkedAtUtc).getTime()) / 1000
  return formatDuration(current.gameLengthSeconds + Math.max(0, sinceCheck))
})

const idleStatus = computed(() => {
  if (error.value && !live.value) return 'error' as const
  if (live.value && live.value.status !== 'inGame') return live.value.status
  return null
})
</script>

<template>
  <!-- A container query, not a viewport one: the tab sits in the profile's main
       column beside an 18rem card column, so the sides go next to each other only
       when that column itself is wide enough for two readable rows. -->
  <div class="@container flex flex-col gap-3">
    <div
      v-if="isInitialLoading"
      class="flex flex-col gap-3"
      aria-busy="true"
    >
      <USkeleton class="h-10 rounded-xl" />
      <div class="grid gap-3 @3xl:grid-cols-2">
        <USkeleton class="h-72 rounded-xl" />
        <USkeleton class="h-72 rounded-xl" />
      </div>
    </div>

    <LiveGameIdle
      v-else-if="idleStatus"
      :status="idleStatus"
      :checked-at-utc="live?.checkedAtUtc ?? null"
      :checking="isLoading"
      :now="now"
      @check="check"
    />

    <template v-else-if="game && live">
      <div class="glass flex flex-wrap items-center justify-between gap-3 rounded-xl px-4 py-2.5">
        <div class="flex items-center gap-3">
          <span class="relative flex size-2.5">
            <span class="absolute inline-flex size-full animate-ping rounded-full bg-primary opacity-60" />
            <span class="relative inline-flex size-2.5 rounded-full bg-primary" />
          </span>
          <span class="text-sm font-semibold text-highlighted">{{ getQueueLabel(game.queueId, game.gameMode) }}</span>
          <span class="text-sm tabular-nums text-muted">{{ clock ?? 'Loading screen' }}</span>
        </div>
        <div class="flex items-center gap-3">
          <span class="text-xs text-dimmed">Checked {{ formatRelativeTime(live.checkedAtUtc, now) }}</span>
          <UButton
            icon="i-lucide-refresh-cw"
            label="Check now"
            size="sm"
            color="primary"
            variant="soft"
            :loading="isLoading"
            @click="check"
          />
        </div>
      </div>

      <div class="grid items-start gap-3 @3xl:grid-cols-2">
        <LiveGameTeam
          v-for="team in game.teams"
          :key="team.teamId"
          :team="team"
          :champions-by-id="championsById"
          :summoner-spells="summonerSpells ?? null"
          :rune-tree="runeTree ?? null"
        />
      </div>
    </template>
  </div>
</template>
