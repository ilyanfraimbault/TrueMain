<script setup lang="ts">
import type { PersonaAccount } from '~~/shared/types/players'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { truemainNameTag } from '~~/shared/utils/truemain-path'
import { isApexTier } from '#common/utils/tiers'

// One account of a known player (#2016). A tracked account is drawn the way
// the leaderboard draws a truemain — identity linking to its profile, rank,
// mains linking to the player's champion pages — and an account TrueMain does
// not track yet is listed with its Riot ID alone: there is no page to open.
const props = defineProps<{
  account: PersonaAccount
  championsById: Map<number, ChampionStaticListItem>
  patch: string | null
}>()

const nameTag = computed(() => truemainNameTag(props.account.gameName, props.account.tagLine))

const rankLabel = computed(() => {
  const ranked = props.account.ranked
  if (!ranked) return null
  const tier = ranked.tier.charAt(0) + ranked.tier.slice(1).toLowerCase()
  const division = isApexTier(ranked.tier) ? '' : ` ${ranked.division}`
  return `${tier}${division} · ${ranked.leaguePoints.toLocaleString('en-US')} LP`
})

const winRate = computed(() => {
  const ranked = props.account.ranked
  if (!ranked || ranked.wins === null || ranked.losses === null || ranked.wins + ranked.losses === 0) return null
  return ranked.wins / (ranked.wins + ranked.losses)
})

const champion = (id: number) => props.championsById.get(id)
</script>

<template>
  <li class="surface surface-hover flex flex-wrap items-center gap-x-6 gap-y-3 rounded-lg px-3 py-2.5">
    <Account
      :identity="{ gameName: account.gameName, tagLine: account.tagLine, profileIconId: account.profileIconId, platformId: account.platformId }"
      :region="account.region"
      :patch="patch"
      :to="account.tracked ? undefined : false"
      size="lg"
      loading="lazy"
      class="min-w-48 flex-1"
    />

    <div
      v-if="account.ranked && rankLabel"
      class="flex w-56 items-center gap-2"
    >
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
    </div>

    <ul
      v-if="account.topChampions.length"
      class="flex items-center gap-1.5"
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
  </li>
</template>
