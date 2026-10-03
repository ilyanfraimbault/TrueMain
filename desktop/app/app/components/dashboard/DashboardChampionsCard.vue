<!--
  The dashboard's champions card: the site's "Main champions"
  (web/app/components/profile/ProfileMainChampions.vue) in the ranked card's
  frame — heading inside the card, rows without rules — and reading
  performance rather than play rate: each champion's games and KDA, and its
  win rate on the data axis. A row opens the champion's builds on the lane the
  player plays it.
-->
<script setup lang="ts">
import type { ChampionLine } from '~/utils/player-form'
import { kdaOf } from '~/utils/player-form'
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { winRateTone } from '#common/utils/rate-tone'

const props = defineProps<{ champions: ChampionLine[] }>()

const { portraitOf, nameOf } = useChampionStatics()

const shown = computed(() => props.champions.slice(0, 5))
const winRate = (line: ChampionLine) => line.wins / line.games
const linkOf = (line: ChampionLine) => (line.position ? `/champions/${line.championId}?lane=${line.position}` : `/champions/${line.championId}`)
</script>

<template>
  <section v-if="shown.length" class="surface flex flex-col gap-2 rounded-lg px-4 py-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">Champions</h2>
    <ul class="-mx-2 flex flex-col">
      <li v-for="line in shown" :key="line.championId">
        <NuxtLink :to="linkOf(line)" class="flex items-center gap-3 rounded-md px-2 py-1.5 transition-colors hover:bg-accented">
          <SkeletonImage :src="portraitOf(line.championId)" :alt="nameOf(line.championId)" :width="32" :height="32" class="size-8 shrink-0 rounded-md" />
          <div class="min-w-0 flex-1 leading-tight">
            <p class="truncate text-sm font-medium text-highlighted">{{ nameOf(line.championId) }}</p>
            <p class="flex items-center gap-1 text-[11px] tabular-nums text-muted">
              <img v-if="line.position" :src="getPositionIconUrl(line.position)" alt="" class="size-3">
              {{ line.games }} game{{ line.games === 1 ? '' : 's' }} · {{ kdaOf(line).toFixed(1) }} KDA
            </p>
          </div>
          <div class="shrink-0 text-right leading-tight tabular-nums">
            <p class="text-sm font-semibold" :class="winRateTone(winRate(line))">{{ Math.round(winRate(line) * 100) }}%</p>
            <p class="text-[10px] text-dimmed">{{ line.wins }}W {{ line.games - line.wins }}L</p>
          </div>
        </NuxtLink>
      </li>
    </ul>
  </section>
</template>
