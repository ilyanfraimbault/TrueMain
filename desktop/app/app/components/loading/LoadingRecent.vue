<script setup lang="ts">
import type { RecentGame } from '~/types/loading'
import { formatRelativeTime } from '#common/utils/relativeTime'
import { LANE_LABELS, laneIconUrl, type Lane } from '~/types/draft'

/**
 * A player's latest games as bars, oldest to newest left to right: blue a
 * win, red a loss (the match row's edge colours). Hovering one names the game
 * — champion, role, KDA, when. Replaces the "W3"/"L4" streak chip, which read
 * as nothing (#1803).
 */
const props = defineProps<{ games: RecentGame[] }>()

const { portraitOf, nameOf } = useChampionStatics()

const bars = computed(() => [...props.games].reverse())

const when = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' })
const playedOn = (game: RecentGame) =>
  `${when.format(game.playedAt)} · ${formatRelativeTime(new Date(game.playedAt).toISOString())}`
const lane = (game: RecentGame) => (game.position && game.position in LANE_LABELS ? game.position as Lane : null)
</script>

<template>
  <div class="flex h-4 shrink-0 items-stretch" :aria-label="`Last ${games.length} games`">
    <UTooltip
      v-for="(game, index) in bars"
      :key="index"
      :delay-duration="0"
      :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
    >
      <span class="flex px-px" :aria-label="game.win ? 'Victory' : 'Defeat'">
        <span class="w-1 rounded-full" :class="game.win ? 'bg-sky-400' : 'bg-red-400'" />
      </span>
      <template #content>
        <GameTooltipSurface>
          <div class="flex items-center gap-2.5">
            <img
              v-if="portraitOf(game.championId)"
              :src="portraitOf(game.championId)!"
              :alt="nameOf(game.championId)"
              class="size-8 shrink-0 rounded"
            >
            <div class="min-w-0 leading-tight">
              <p class="flex items-center gap-1.5 text-sm font-semibold text-highlighted">
                {{ nameOf(game.championId) }}
                <span class="text-xs font-medium" :class="game.win ? 'text-sky-300' : 'text-red-400'">
                  {{ game.win ? 'Victory' : 'Defeat' }}
                </span>
              </p>
              <p class="mt-0.5 flex items-center gap-1.5 text-xs tabular-nums text-muted">
                <template v-if="lane(game)">
                  <img :src="laneIconUrl(lane(game)!)" :alt="LANE_LABELS[lane(game)!]" class="size-3.5 opacity-80">
                  <span>{{ LANE_LABELS[lane(game)!] }}</span>
                  <span class="text-dimmed">·</span>
                </template>
                <span class="text-default">
                  {{ game.kills }}<span class="text-dimmed"> / </span><span class="text-red-400">{{ game.deaths }}</span><span class="text-dimmed"> / </span>{{ game.assists }}
                </span>
              </p>
            </div>
          </div>
          <p class="mt-2 text-[11px] text-dimmed">{{ playedOn(game) }}</p>
        </GameTooltipSurface>
      </template>
    </UTooltip>
  </div>
</template>
