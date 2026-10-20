<script setup lang="ts">
import type { LiveParticipant } from '#shared/types/live'
import type { ChampionStaticListItem, RuneTreeResponse, StaticSummonerSpellData } from '#shared/types/static-data'
import { truemainNameTag, truemainProfilePath } from '#shared/utils/truemain-path'
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { positionLabel } from '#common/utils/positions'
import { formatRank } from '#common/utils/tiers'

// One player of a live game (#2103): the champion with the inferred lane on its
// badge, the spells and runes they locked in, the Riot ID — linked when TrueMain
// holds the account — and their Solo/Duo rank. The profile's own player is
// lifted in the primary tint so the eye finds them in the lobby.
const props = defineProps<{
  participant: LiveParticipant
  champion: ChampionStaticListItem | null
  summonerSpells: Record<number, StaticSummonerSpellData> | null
  runeTree: RuneTreeResponse | null
}>()

const profilePath = computed(() => {
  const { gameName, tagLine, isTracked, isSubject } = props.participant
  return isTracked && !isSubject && gameName && tagLine
    ? truemainProfilePath(truemainNameTag(gameName, tagLine))
    : null
})

const championName = computed(() => props.champion?.name ?? `Champion ${props.participant.championId}`)
const positionName = computed(() => positionLabel(props.participant.position))

const name = computed(() => props.participant.gameName ?? (props.participant.isBot ? 'Bot' : 'Hidden player'))

const loadout = computed(() => ({
  summoner1Id: props.participant.spell1Id,
  summoner2Id: props.participant.spell2Id,
  keystoneId: props.participant.keystoneId ?? 0,
  subStyleId: props.participant.secondaryStyleId ?? 0,
}))

const rankLabel = computed(() => {
  const ranked = props.participant.ranked
  return ranked ? formatRank(ranked.tier, ranked.division, { lp: ranked.leaguePoints }) : null
})

const record = computed(() => {
  const ranked = props.participant.ranked
  if (!ranked || ranked.wins === null || ranked.losses === null) return null
  const rate = ranked.winRate === null ? '' : ` · ${Math.round(ranked.winRate * 100)}%`
  return `${ranked.wins}W ${ranked.losses}L${rate}`
})
</script>

<template>
  <li
    class="flex items-center gap-3 rounded-lg px-2 py-1.5"
    :class="participant.isSubject ? 'bg-primary/10 ring-1 ring-primary/30' : 'glass-hover'"
    :data-subject="participant.isSubject || undefined"
  >
    <div class="relative shrink-0">
      <ChampionLink
        :champion-id="participant.championId"
        :name="championName"
        :icon-url="champion?.iconUrl ?? null"
        class="block size-9 rounded-md"
      />
      <span
        v-if="participant.position"
        class="absolute -bottom-1 -right-1 flex size-4 items-center justify-center rounded-full bg-default ring-1 ring-default"
        :title="positionName ?? undefined"
      >
        <img :src="getPositionIconUrl(participant.position)" :alt="positionName ?? ''" class="size-3">
      </span>
    </div>

    <MatchRowSpellsRunes
      v-if="summonerSpells && runeTree"
      :self="loadout"
      :summoner-spells="summonerSpells"
      :rune-tree="runeTree"
    />

    <div class="flex min-w-0 flex-1 flex-col">
      <NuxtLink
        v-if="profilePath"
        :to="profilePath"
        class="truncate text-sm font-medium text-default transition-colors hover:text-primary"
      >
        {{ name }}
      </NuxtLink>
      <span
        v-else
        class="truncate text-sm font-medium"
        :class="participant.isSubject ? 'text-highlighted' : 'text-muted'"
      >{{ name }}</span>
      <span
        v-if="record"
        class="truncate text-xs tabular-nums text-dimmed"
      >{{ record }}</span>
    </div>

    <div class="flex shrink-0 items-center gap-1.5 text-right">
      <template v-if="participant.ranked && rankLabel">
        <span class="hidden text-xs font-semibold tabular-nums text-highlighted sm:inline">{{ rankLabel }}</span>
        <RankEmblem
          :rank="participant.ranked"
          :record="participant.ranked"
          :size="24"
          crest-only
        />
      </template>
      <span
        v-else
        class="text-xs text-dimmed"
      >Unranked</span>
    </div>
  </li>
</template>
