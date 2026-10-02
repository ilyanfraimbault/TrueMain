<script setup lang="ts">
import type { StaticSummonerSpellData } from '#shared/types/static-data'
import type { GamePlayer } from '~/types/game'

/**
 * One player on the board: champion with its level, summoner spells, Riot ID
 * and K/D/A, then the items. The right-hand team is drawn mirrored, so each
 * lane reads as a face-off across the middle. A dead player's portrait goes
 * grey under the seconds left before they are back, counted down on screen
 * from the respawn time the game gave at the death.
 */
const props = withDefaults(defineProps<{
  player: GamePlayer | null
  /** Seconds of game time now, run on the page's clock between readings. */
  clock: number
  mirrored?: boolean
}>(), { mirrored: false })

const { champions, portraitOf, patch } = useChampionStatics()
const { summoners, pending } = useStaticData()

/** Data Dragon ids by alias, without case: the game writes `FiddleSticks` where Data Dragon has `Fiddlesticks`. */
const byAlias = computed(() => new Map([...champions.value.values()].map(champion => [champion.alias.toLowerCase(), champion.id])))

const portrait = computed(() => {
  if (!props.player) return null
  const id = byAlias.value.get(props.player.champion.toLowerCase())
  if (id !== undefined) return portraitOf(id)
  return patch.value ? `https://ddragon.leagueoflegends.com/cdn/${patch.value}/img/champion/${props.player.champion}.png` : null
})

/** Summoner spells by Data Dragon id, read off the icon file the static map keeps (`SummonerFlash.png`). */
const spellsByKey = computed(() => {
  const out = new Map<string, StaticSummonerSpellData>()
  for (const spell of Object.values(summoners.value)) {
    const file = spell.iconUrl.split('/').pop()?.replace(/\.png$/, '')
    if (file) out.set(file, spell)
  }
  return out
})

const name = computed(() => props.player?.riotId.split('#')[0] ?? '')
const tag = computed(() => props.player?.riotId.split('#')[1] ?? '')

const respawnIn = computed(() => {
  const at = props.player?.respawnAt
  if (!props.player?.dead || at === null || at === undefined) return null
  return Math.max(0, Math.ceil(at - props.clock))
})
</script>

<template>
  <div v-if="player" class="flex min-w-0 items-center gap-2.5" :class="mirrored && 'flex-row-reverse'">
    <div class="relative shrink-0">
      <div
        class="relative size-12 overflow-hidden rounded-lg bg-elevated ring-1 ring-white/10"
        :title="player.championName"
      >
        <img v-if="portrait" :src="portrait" :alt="player.championName" class="size-full object-cover transition" :class="player.dead && 'opacity-40 grayscale'">
        <span v-if="respawnIn" class="absolute inset-0 flex items-center justify-center stat-value text-base text-highlighted drop-shadow-[0_1px_2px_rgb(0_0_0/0.9)]">{{ respawnIn }}</span>
      </div>
      <span
        class="absolute -bottom-1 rounded-sm bg-default px-1 text-[10px] font-bold leading-3.5 tabular-nums text-muted ring-1 ring-default"
        :class="mirrored ? '-left-1' : '-right-1'"
      >{{ player.level }}</span>
    </div>

    <div class="flex shrink-0 flex-col gap-0.5">
      <GameTooltipSummonerSpellIcon
        v-for="(spell, index) in player.spells"
        :key="index"
        :spell="spellsByKey.get(spell.key) ?? null"
        :fallback-label="spell.name"
        :pending="pending"
        :settled="!pending"
        :width="18"
        :height="18"
        class="size-[18px] rounded-sm"
      />
    </div>

    <div class="min-w-0 flex-1 leading-tight" :class="mirrored && 'text-right'">
      <p class="truncate text-[13px] text-default" :title="player.riotId">
        {{ name }}<span v-if="tag" class="text-dimmed">#{{ tag }}</span>
      </p>
      <p class="mt-0.5 text-xs tabular-nums text-muted">
        {{ player.kills }}<span class="text-dimmed"> / </span><span class="text-red-400">{{ player.deaths }}</span><span class="text-dimmed"> / </span>{{ player.assists }}
      </p>
    </div>

    <GameItemRow :items="player.items" :mirrored="mirrored" />
  </div>

  <div v-else class="flex h-12 items-center" :class="mirrored && 'justify-end'">
    <span class="stat-label">Empty slot</span>
  </div>
</template>
