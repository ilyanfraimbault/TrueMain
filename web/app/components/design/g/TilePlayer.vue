<script setup lang="ts">
import type { LeaderboardRowReadModel as LeaderboardRowResponse } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage, getProfileIconUrl } from '~~/shared/utils/ddragon'
import { truemainNameTag, truemainProfilePath } from '~~/shared/utils/truemain-path'
import { leaderboardChampionIcon, leaderboardChampionName } from '#common/utils/leaderboard-row'
import { formatRank } from '#shared/utils/ranks'

// The truemain at the top of the ladder (the leaderboard's first row, ranked by
// standing): who they are, their rank, and the champion they main, whose splash
// waits at the tile's edge and slides in under the cursor.
const props = defineProps<{
  row: LeaderboardRowResponse | null
  championsById: Map<number, ChampionStaticListItem>
  patch: string | null
  loading: boolean
  index: number
}>()

const canonicalIcon = useCanonicalIcon()

const main = computed(() => props.row?.topChampions[0] ?? null)
// Client-only (the static list): the art and the name wait for it rather than
// flashing the `Champion 92` stand-in.
const mainKnown = computed(() => (main.value ? props.championsById.has(main.value.championId) : false))
const mainName = computed(() => (main.value ? leaderboardChampionName(props.championsById, main.value.championId) : ''))
const mainIcon = computed(() => (main.value ? canonicalIcon(leaderboardChampionIcon(props.championsById, main.value.championId), 48) : undefined))
const splash = useChampionSplash(() => (main.value && mainKnown.value ? leaderboardChampionIcon(props.championsById, main.value.championId) : null))

const avatar = computed(() => {
  const id = props.row?.identity.profileIconId ?? 0
  return id > 0 ? canonicalIcon(getProfileIconUrl(id, props.patch), 96) : undefined
})
const to = computed(() => (props.row
  ? truemainProfilePath(truemainNameTag(props.row.identity.gameName, props.row.identity.tagLine))
  : '/truemains'))
const rank = computed(() => (props.row?.ranked
  ? formatRank(props.row.ranked.tier, props.row.ranked.division, { lp: props.row.ranked.leaguePoints })
  : null))
const winRate = computed(() => (props.row?.stats.winRate == null ? null : formatPercentage(props.row.stats.winRate, 0)))
</script>

<template>
  <DesignGTile
    area="player"
    :index="index"
    :to="to"
    label="Top truemain"
    icon="i-lucide-crown"
    class="g-player"
  >
    <template #art>
      <span
        class="g-player__art"
        aria-hidden="true"
      >
        <DesignGArt
          :src="splash"
          :width="1280"
          :height="755"
          position="70% 25%"
        />
      </span>
    </template>

    <span
      v-if="loading"
      class="g-player__content"
      aria-hidden="true"
    >
      <span class="g-skel size-14 rounded-2xl" />
      <span class="flex flex-col gap-2">
        <span class="g-skel h-5 w-36" />
        <span class="g-skel h-3.5 w-48" />
      </span>
    </span>

    <span
      v-else-if="row"
      class="g-player__content"
    >
      <span class="g-player__avatar">
        <img
          v-if="avatar"
          :src="avatar"
          alt=""
          width="56"
          height="56"
          class="img-skeleton"
        >
        <UIcon
          v-else
          name="i-lucide-user"
          class="size-6 text-dimmed"
        />
      </span>
      <span class="g-player__who">
        <span class="g-player__name">
          <span class="truncate">{{ row.identity.gameName }}</span>
          <span
            v-if="row.identity.tagLine"
            class="g-player__tag"
          >#{{ row.identity.tagLine }}</span>
        </span>
        <span class="g-player__line">
          <span
            v-if="row.persona"
            class="g-player__persona"
          >{{ row.persona.displayName }}</span>
          <span
            v-if="row.ranked"
            class="g-player__rank"
          >
            <RankIcon
              :tier="row.ranked.tier"
              :size="18"
              loading="lazy"
            />
            {{ rank }}
          </span>
        </span>
        <span
          v-if="main"
          class="g-player__main"
        >
          <template v-if="mainKnown">
            <img
              v-if="mainIcon"
              :src="mainIcon"
              alt=""
              width="20"
              height="20"
              class="g-player__champ img-skeleton"
            >
            <span class="text-default">{{ mainName }}</span>
          </template>
          <span
            v-else
            class="g-skel h-3.5 w-20"
            aria-hidden="true"
          />
          <span class="text-dimmed">{{ formatPercentage(main.playRate, 0) }} of games</span>
          <template v-if="winRate">
            <span
              class="g-dot"
              aria-hidden="true"
            />
            <span class="text-dimmed">{{ winRate }} WR</span>
          </template>
        </span>
      </span>
    </span>

    <span
      v-else
      class="g-player__content g-empty"
    >No ranked truemains yet.</span>
  </DesignGTile>
</template>
