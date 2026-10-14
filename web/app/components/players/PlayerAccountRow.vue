<script setup lang="ts">
import type { PersonaAccount } from '~~/shared/types/players'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { truemainNameTag } from '~~/shared/utils/truemain-path'
import { formatRank } from '#common/utils/tiers'
import { formatRelativeTime } from '#common/utils/relativeTime'

// One account of a known player (#2016): when it last played, then the account
// the way the leaderboard draws a truemain — identity linking to its profile,
// rank, mains linking to the player's champion pages. Only tracked accounts
// reach this row; the page lists no account it could not open.
const props = defineProps<{
  account: PersonaAccount
  championsById: Map<number, ChampionStaticListItem>
  patch: string | null
}>()

const nameTag = computed(() => truemainNameTag(props.account.gameName, props.account.tagLine))

const rankLabel = computed(() => {
  const ranked = props.account.ranked
  return ranked ? formatRank(ranked.tier, ranked.division, { lp: ranked.leaguePoints }) : null
})

const winRate = computed(() => {
  const ranked = props.account.ranked
  if (!ranked || ranked.wins === null || ranked.losses === null || ranked.wins + ranked.losses === 0) return null
  return ranked.wins / (ranked.wins + ranked.losses)
})

const champion = (id: number) => props.championsById.get(id)

const lastPlayed = computed(() => (props.account.lastPlayedAtUtc ? formatRelativeTime(props.account.lastPlayedAtUtc) : null))
const lastPlayedTitle = computed(() => (props.account.lastPlayedAtUtc
  ? `Last ranked game ${new Date(props.account.lastPlayedAtUtc).toUTCString()}`
  : 'No ranked game stored for this account'))
</script>

<template>
  <li class="surface surface-hover flex flex-wrap items-center gap-x-6 gap-y-3 rounded-lg px-3 py-2.5">
    <Account
      :identity="{ gameName: account.gameName, tagLine: account.tagLine, profileIconId: account.profileIconId, platformId: account.platformId }"
      :region="account.region"
      :patch="patch"
      size="lg"
      loading="lazy"
      class="min-w-48 flex-1"
    />

    <!-- Fixed widths, so the rank, mains and date line up from row to row. -->
    <div class="flex w-52 items-center gap-2">
      <template v-if="account.ranked && rankLabel">
        <RankIcon
          :tier="account.ranked.tier"
          :size="28"
          loading="lazy"
        />
        <div class="min-w-0 leading-tight">
          <p class="text-sm font-semibold text-highlighted">
            {{ rankLabel }}
          </p>
          <p
            v-if="winRate !== null"
            class="text-xs text-muted tabular-nums"
          >
            {{ account.ranked.wins }}W {{ account.ranked.losses }}L · {{ formatPercentage(winRate, 0) }}
          </p>
        </div>
      </template>
      <span
        v-else
        class="text-sm text-dimmed"
      >Unranked</span>
    </div>

    <ul
      class="flex w-28 items-center gap-1.5"
      aria-label="Mains"
    >
      <li
        v-for="main in account.topChampions"
        :key="main.championId"
      >
        <ChampionLink
          v-if="champion(main.championId)"
          :champion-id="main.championId"
          :name="champion(main.championId)!.name"
          :icon-url="champion(main.championId)!.iconUrl"
          :name-tag="nameTag"
          :title="`${champion(main.championId)!.name} · ${main.games} games · ${formatPercentage(main.playRate, 0)} of games${main.isOtp ? ' · OTP' : ''}`"
          class="size-8"
        />
      </li>
    </ul>

    <span
      class="w-24 text-right text-xs tabular-nums"
      :class="lastPlayed ? 'text-muted' : 'text-dimmed'"
      :title="lastPlayedTitle"
    >{{ lastPlayed ?? '—' }}</span>
  </li>
</template>
