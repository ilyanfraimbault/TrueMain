<script setup lang="ts">
import type { GamePlayer, GameState } from '~/types/game'
import { itemGold, laneRows, leftTeam } from '~/utils/item-value'

/**
 * The item value while the scoreboard is open (TAB, #1752): what each team's
 * items are worth, an arrow toward the side ahead with the gap, then the
 * same for each lane, ours against theirs. Arithmetic on what the scoreboard
 * itself shows; in its own panel rather than pinned to Riot's rows, which
 * move with the resolution and the HUD scale.
 */
const props = defineProps<{ game: GameState }>()

const { items } = useStaticData()
const { champions, portraitOf, patch } = useChampionStatics()

const byAlias = computed(() => new Map([...champions.value.values()].map(champion => [champion.alias.toLowerCase(), champion.id])))
function portrait(player: GamePlayer | null) {
  if (!player) return null
  const id = byAlias.value.get(player.champion.toLowerCase())
  if (id !== undefined) return portraitOf(id)
  return patch.value ? `https://ddragon.leagueoflegends.com/cdn/${patch.value}/img/champion/${player.champion}.png` : null
}

const value = (player: GamePlayer | null) => (player ? itemGold(player, items.value) : 0)
const left = computed(() => leftTeam(props.game))
const total = (ours: boolean) => props.game.players
  .filter(player => (player.team === left.value) === ours)
  .reduce((sum, player) => sum + value(player), 0)

const teams = computed(() => ({ ours: total(true), theirs: total(false) }))
const rows = computed(() => laneRows(props.game).map(({ ally, enemy }) => ({
  ally,
  enemy,
  ours: value(ally),
  theirs: value(enemy),
  key: `${ally?.riotId}-${enemy?.riotId}`,
})))

const number = (gold: number) => gold.toLocaleString('en-US')
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="grid grid-cols-[1fr_auto_1fr] items-center gap-3">
      <span class="stat-value justify-self-end text-lg leading-none tabular-nums text-ally">{{ number(teams.ours) }}</span>
      <OverlayGoldGap :ours="teams.ours" :theirs="teams.theirs" large />
      <span class="stat-value text-lg leading-none tabular-nums text-enemy">{{ number(teams.theirs) }}</span>
    </div>

    <ul class="flex flex-col gap-1 border-t border-default pt-2">
      <li v-for="row in rows" :key="row.key" class="grid grid-cols-[1.25rem_1fr_auto_1fr_1.25rem] items-center gap-2">
        <img v-if="portrait(row.ally)" :src="portrait(row.ally)!" :alt="row.ally?.championName" class="img-skeleton size-5 rounded">
        <span v-else />
        <span class="justify-self-end text-[11px] tabular-nums text-muted">{{ number(row.ours) }}</span>
        <OverlayGoldGap :ours="row.ours" :theirs="row.theirs" />
        <span class="text-[11px] tabular-nums text-muted">{{ number(row.theirs) }}</span>
        <img v-if="portrait(row.enemy)" :src="portrait(row.enemy)!" :alt="row.enemy?.championName" class="img-skeleton size-5 rounded">
        <span v-else />
      </li>
    </ul>
  </div>
</template>
