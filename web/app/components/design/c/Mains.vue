<script setup lang="ts">
import type { LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championSplashUrlFromIcon, formatPercentage, getProfileIconUrl } from '~~/shared/utils/ddragon'
import { formatCompactCount } from '~~/shared/utils/counts'
import { truemainNameTag, truemainProfilePath } from '~~/shared/utils/truemain-path'
import { leaderboardChampionIcon, leaderboardChampionName } from '#common/utils/leaderboard-row'
import { winRateTone } from '#common/utils/rate-tone'

/**
 * Design exploration, variant C — chapter 02, "The mains": the leaderboard's
 * top players as an editorial index. Each row is one link; the cursor parts
 * the row like a canopy and shows the player's main champion's splash beneath
 * it, through a lens that follows the pointer. The splash is fetched on the
 * first hover or focus of its row only.
 */
const props = defineProps<{
  rows: LeaderboardRowResponse[]
  championsById: Map<number, ChampionStaticListItem>
  loading: boolean
  patch: string | null
  tracked: number | null
}>()

const iconOf = useCanonicalIcon()
const splashOf = useCanonicalSplash()

const entries = computed(() => props.rows.map((row, index) => {
  const main = row.topChampions[0] ?? null
  const mainIcon = main ? leaderboardChampionIcon(props.championsById, main.championId) : null
  return {
    row,
    key: `${row.identity.gameName}-${row.identity.tagLine}`,
    rank: String(index + 1).padStart(2, '0'),
    href: truemainProfilePath(truemainNameTag(row.identity.gameName, row.identity.tagLine)),
    avatar: row.identity.profileIconId ? iconOf(getProfileIconUrl(row.identity.profileIconId, props.patch)) : undefined,
    platform: row.identity.platformId?.replace(/\d+$/, '') ?? null,
    main,
    mainName: main ? leaderboardChampionName(props.championsById, main.championId) : null,
    mainIcon: iconOf(mainIcon),
    mainSplash: splashOf(championSplashUrlFromIcon(mainIcon)),
    playRate: main ? formatPercentage(main.playRate, 0) : null,
    winRate: row.stats.winRate === null ? null : formatPercentage(row.stats.winRate, 0),
  }
}))

// Splashes requested only once a row has been hovered or focused.
const woken = ref(new Set<string>())
function wake(key: string) {
  if (woken.value.has(key)) return
  woken.value = new Set(woken.value).add(key)
}
</script>

<template>
  <section
    class="c-mains"
    aria-labelledby="c-mains-title"
  >
    <DesignCChapter
      id="c-mains-title"
      index="02"
      kicker="The mains"
      :lines="['The players', 'behind the builds.']"
      :meta="tracked !== null ? `${formatCompactCount(tracked)} truemains tracked` : null"
    >
      <p>
        The top of the truemains leaderboard by current rank, with the champion each of them mains.
      </p>
    </DesignCChapter>

    <div
      v-if="loading"
      class="c-mains__list"
      aria-hidden="true"
    >
      <div
        v-for="i in 7"
        :key="i"
        class="c-main is-skeleton"
      >
        <USkeleton class="h-8 w-10" />
        <span class="flex items-center gap-3"><USkeleton class="size-11 rounded-full" /><USkeleton class="h-6 w-44" /></span>
        <USkeleton class="hidden h-5 w-32 md:block" />
        <USkeleton class="ml-auto h-5 w-12" />
      </div>
    </div>

    <ol
      v-else-if="entries.length"
      v-reveal.stagger="{ effect: 'c-row', step: 70 }"
      class="c-mains__list"
    >
      <li
        v-for="entry in entries"
        :key="entry.key"
      >
        <NuxtLink
          v-spotlight
          :to="entry.href"
          class="c-main"
          @pointerenter="wake(entry.key)"
          @focus="wake(entry.key)"
        >
          <span
            class="c-main__reveal"
            aria-hidden="true"
          >
            <img
              v-if="entry.mainSplash && woken.has(entry.key)"
              :src="entry.mainSplash"
              alt=""
              decoding="async"
            >
          </span>
          <span
            class="c-main__rank"
            aria-hidden="true"
          >{{ entry.rank }}</span>
          <span class="c-main__player">
            <span class="c-main__avatar">
              <img
                v-if="entry.avatar"
                :src="entry.avatar"
                alt=""
                width="44"
                height="44"
                loading="lazy"
                decoding="async"
              >
            </span>
            <span class="c-main__id">
              <span class="c-main__name">{{ entry.row.identity.gameName }}<span class="c-main__tag">#{{ entry.row.identity.tagLine }}</span></span>
              <span class="c-main__sub">
                <span
                  v-if="entry.platform"
                  class="c-mono"
                >{{ entry.platform }}</span>
                <span
                  v-if="entry.row.persona"
                  class="c-main__persona"
                >{{ entry.row.persona.displayName }}</span>
                <span
                  v-if="entry.main && entry.mainName"
                  class="c-main__sub-champ"
                ><img
                  v-if="entry.mainIcon"
                  :src="entry.mainIcon"
                  alt=""
                  width="16"
                  height="16"
                  loading="lazy"
                >{{ entry.mainName }}</span>
              </span>
            </span>
          </span>
          <span
            v-if="entry.main"
            class="c-main__champ"
          >
            <img
              v-if="entry.mainIcon"
              :src="entry.mainIcon"
              alt=""
              width="34"
              height="34"
              loading="lazy"
              decoding="async"
              class="c-main__champ-icon"
            >
            <span class="c-main__champ-text">
              <span class="c-main__champ-name">{{ entry.mainName }}<span
                v-if="entry.main.isOtp"
                class="c-main__otp"
              >OTP</span></span>
              <span class="c-mono">{{ entry.playRate }} play rate</span>
            </span>
          </span>
          <span
            v-else
            class="c-main__champ"
          />
          <RankEmblem
            v-if="entry.row.ranked"
            :rank="entry.row.ranked"
            :record="entry.row.stats"
            :size="28"
            class="c-main__emblem"
          />
          <span
            v-else
            class="c-main__emblem"
          />
          <span
            class="c-main__wr"
            :class="winRateTone(entry.row.stats.winRate)"
          >
            <template v-if="entry.winRate">{{ entry.winRate }}<span class="c-mono">WR</span></template>
          </span>
          <UIcon
            name="i-lucide-arrow-up-right"
            class="c-main__arrow"
          />
        </NuxtLink>
      </li>
    </ol>

    <p
      v-else
      class="c-mains__empty"
    >
      No ranked truemains yet.
    </p>

    <div class="c-mains__foot">
      <NuxtLink
        to="/truemains"
        class="c-textlink"
      >
        <span
          class="c-roll"
          data-text="Open the leaderboard"
        ><span>Open the leaderboard</span></span>
        <UIcon
          name="i-lucide-arrow-right"
          class="c-textlink__icon"
        />
      </NuxtLink>
    </div>
  </section>
</template>
