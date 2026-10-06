<script setup lang="ts">
import type { StaticSummonerSpellData } from '#shared/types/static-data'
import type { GamePlayer } from '~/types/game'
import type { LoadingPlayer } from '~/types/loading'

/**
 * One player on the board: champion with its level and summoner spells, then
 * who they are — standing, role, form on the champion, streak (#1828,
 * `GamePlayerIntel`). The items and K/D/A are left to the game's own
 * scoreboard, which shows them already. The right-hand team is drawn
 * mirrored, so each lane reads as a face-off across the middle. A dead
 * player's portrait goes grey under the seconds left before they are back,
 * counted down on screen from the respawn time the game gave at the death.
 */
const props = withDefaults(defineProps<{
  player: GamePlayer | null
  /** Seconds of game time now, run on the page's clock between readings. */
  clock: number
  /** The loading screen's line for this player (`loadingLineOf`). */
  line?: LoadingPlayer | null
  /** The loading screen is reading the game. */
  reading?: boolean
  mirrored?: boolean
}>(), { mirrored: false, line: null, reading: false })

const { champions, portraitOf, patch } = useChampionStatics()
const { summoners, pending } = useStaticData()

/** Data Dragon ids by alias, without case: the game writes `FiddleSticks` where Data Dragon has `Fiddlesticks`. */
const byAlias = computed(() => new Map([...champions.value.values()].map(champion => [champion.alias.toLowerCase(), champion.id])))

const championId = computed(() => (props.player ? byAlias.value.get(props.player.champion.toLowerCase()) ?? null : null))

const portrait = computed(() => {
  if (!props.player) return null
  if (championId.value !== null) return portraitOf(championId.value)
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

    <GamePlayerIntel
      :riot-id="player.riotId"
      :position="player.position"
      :champion-id="championId"
      :line="line"
      :reading="reading"
      :mirrored="mirrored"
    />
  </div>

  <div v-else class="flex h-12 items-center" :class="mirrored && 'justify-end'">
    <span class="stat-label">Empty slot</span>
  </div>
</template>
