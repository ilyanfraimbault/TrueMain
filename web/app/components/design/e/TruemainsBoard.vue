<script setup lang="ts">
import type { LeaderboardRowReadModel as LeaderboardRowResponse } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatCompactCount } from '~~/shared/utils/counts'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { truemainNameTag, truemainProfilePath } from '~~/shared/utils/truemain-path'
import { leaderboardChampionIcon, leaderboardChampionName } from '#common/utils/leaderboard-row'
import { winRateTone } from '#common/utils/rate-tone'

// Variant E home, round 3: the truemains — what sets TrueMain apart — as
// compact player cards: the player, the known person behind the account when
// a source names one, their rank, and the champion they main with the share of
// their games on it. No splash art.
const props = defineProps<{
  rows: LeaderboardRowResponse[]
  championsById: Map<number, ChampionStaticListItem>
  loading: boolean
  patch: string | null
  tracked: number | null
}>()

const cards = computed(() => props.rows.map((row) => {
  const main = row.topChampions[0] ?? null
  return {
    row,
    key: `${row.identity.gameName}-${row.identity.tagLine}`,
    href: truemainProfilePath(truemainNameTag(row.identity.gameName, row.identity.tagLine)),
    main,
    mainName: main ? leaderboardChampionName(props.championsById, main.championId) : null,
    mainIcon: main ? leaderboardChampionIcon(props.championsById, main.championId) : null,
  }
}))
</script>

<template>
  <section
    class="e-hx-section"
    aria-labelledby="e-hx-mains-title"
  >
    <header class="e-hx-head">
      <div>
        <p class="eyebrow">
          Truemains
        </p>
        <h2
          id="e-hx-mains-title"
          class="e-hx-h2"
        >
          The players who truly main them.
        </h2>
        <p
          v-if="tracked"
          class="e-hx-sub"
        >
          {{ formatCompactCount(tracked) }} one-champion players on the leaderboard, ranked by what they play.
        </p>
      </div>
      <NuxtLink
        to="/truemains"
        class="e-hx-more"
      >
        Leaderboard
        <UIcon
          name="i-lucide-arrow-right"
          class="size-4"
        />
      </NuxtLink>
    </header>

    <ul
      v-if="loading"
      class="e-hx-players"
      aria-hidden="true"
    >
      <li
        v-for="n in 6"
        :key="n"
        class="e-hx-player"
      >
        <USkeleton class="size-11 rounded-xl" />
        <USkeleton class="h-4 w-32" />
      </li>
    </ul>
    <ul
      v-else-if="cards.length"
      class="e-hx-players"
    >
      <li
        v-for="card in cards"
        :key="card.key"
      >
        <NuxtLink
          :to="card.href"
          class="e-hx-player"
        >
          <Account
            :identity="card.row.identity"
            :region="card.row.region"
            :patch="patch"
            size="md"
            :to="false"
            class="min-w-0 flex-1"
          />
          <!-- Own column, empty when no source names the person, so every
               card's figures line up. -->
          <span class="e-hx-persona-cell">
            <UTooltip
              v-if="card.row.persona"
              :text="`Known player: ${card.row.persona.displayName}`"
            >
              <span class="e-hx-persona">
                <UIcon
                  name="i-lucide-badge-check"
                  class="size-3.5 shrink-0"
                />
                <span class="truncate">{{ card.row.persona.displayName }}</span>
              </span>
            </UTooltip>
          </span>
          <UTooltip
            v-if="card.main"
            :text="`${card.mainName} · ${formatPercentage(card.main.playRate, 0)} of their games`"
          >
            <span class="e-hx-main">
              <img
                v-if="card.mainIcon"
                :src="card.mainIcon"
                alt=""
                width="28"
                height="28"
                class="size-7 rounded-md"
              >
              <span class="e-hx-main__share">{{ formatPercentage(card.main.playRate, 0) }}</span>
            </span>
          </UTooltip>
          <span
            v-else
            class="e-hx-main"
          />
          <RankEmblem
            v-if="card.row.ranked"
            :rank="card.row.ranked"
            :record="card.row.stats"
            class="shrink-0"
          />
          <span
            v-if="card.row.stats.winRate !== null"
            class="e-hx-player__wr"
            :class="winRateTone(card.row.stats.winRate)"
          >{{ formatPercentage(card.row.stats.winRate, 0) }}</span>
        </NuxtLink>
      </li>
    </ul>
    <p
      v-else
      class="e-hx-lane__empty"
    >
      No ranked truemains yet.
    </p>
  </section>
</template>
