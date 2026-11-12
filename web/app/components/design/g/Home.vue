<script setup lang="ts">
import type { ChampionOverviewRowReadModel as ChampionOverviewRow } from '#shared/contract/api'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import type { DesignHomeData } from '~/composables/useDesign'

// Variant G ("Bento") home: the page opens on a grid of live tiles, each one a
// question the site answers on real data — who is everywhere this patch, who is
// banned, who wins, how much we read, who sits on top of the ladder — and each
// one a door to the page that answers it in full. The full tier-list and
// truemains panels follow underneath.
const props = defineProps<DesignHomeData>()

const rows = computed(() => props.overview?.topRows ?? [])
const patch = computed(() => props.overview?.patchVersion ?? null)

const heroRow = computed(() => rows.value[0] ?? null)
const banRow = computed(() => rows.value.reduce<ChampionOverviewRow | null>(
  (best, row) => (row.banRate !== null && (best === null || row.banRate > (best.banRate ?? -1)) ? row : best),
  null,
))
const winRow = computed(() => rows.value.reduce<ChampionOverviewRow | null>(
  (best, row) => (best === null || row.winRate > best.winRate ? row : best),
  null,
))
const champion = (row: ChampionOverviewRow | null) => (row ? props.championsById.get(row.championId) : undefined)

const topPlayer = computed(() => props.truemainRows[0] ?? null)

// The tracked tile's crowd: the leaderboard's first faces, real profile icons.
const canonicalIcon = useCanonicalIcon()
const crowd = computed(() => props.truemainRows.slice(0, 5).flatMap((row) => {
  const id = row.identity.profileIconId
  const src = id > 0 ? canonicalIcon(getProfileIconUrl(id, props.ddragonPatch), 64) : undefined
  return src ? [{ key: `${row.identity.gameName}#${row.identity.tagLine}`, src }] : []
}))
</script>

<template>
  <div class="g-home">
    <header class="g-intro">
      <h1 class="g-intro__title">
        Real builds from <span class="g-intro__accent">real mains</span>.
      </h1>
      <p class="g-intro__lede">
        Builds, runes and skill orders from the players who truly mastered your champion.
      </p>
    </header>

    <div class="g-bento">
      <DesignGTileHero
        :index="0"
        :row="heroRow"
        :champion="champion(heroRow)"
        :patch="patch"
        :pending="tierlistPending"
      />
      <DesignGTileChampion
        kind="ban"
        :index="1"
        :row="banRow"
        :champion="champion(banRow)"
        :pool-size="rows.length"
        :pending="tierlistPending"
      />
      <DesignGTileChampion
        kind="win"
        :index="2"
        :row="winRow"
        :champion="champion(winRow)"
        :pool-size="rows.length"
        :pending="tierlistPending"
      />
      <DesignGTileStat
        area="games"
        :index="3"
        to="/champions"
        label="Main games analyzed"
        icon="i-lucide-database"
        :value="gamesAnalyzed"
        :pending="overviewPending"
      />
      <DesignGTileStat
        area="tracked"
        :index="4"
        to="/truemains"
        label="Truemains tracked"
        icon="i-lucide-users"
        :value="trackedTruemains"
        :pending="truemainsInitialLoading"
      >
        <template #extra>
          <span
            v-if="crowd.length"
            class="g-crowd"
            aria-hidden="true"
          >
            <img
              v-for="(face, i) in crowd"
              :key="face.key"
              :src="face.src"
              alt=""
              width="28"
              height="28"
              class="g-crowd__face img-skeleton"
              :style="{ '--k': i }"
            >
          </span>
        </template>
      </DesignGTileStat>
      <DesignGTilePlayer
        :index="5"
        :row="topPlayer"
        :champions-by-id="championsById"
        :patch="ddragonPatch"
        :loading="truemainsInitialLoading"
      />
      <DesignGTileSearch :index="6" />
      <DesignGTileDesktop :index="7" />
      <DesignGTileTierlist
        :index="8"
        :rows="rows"
        :champions-by-id="championsById"
        :pending="tierlistPending"
      />
    </div>

    <section
      class="g-more"
      aria-labelledby="g-more-title"
    >
      <div class="g-more__head">
        <p class="eyebrow">
          This patch
        </p>
        <h2
          id="g-more-title"
          class="g-more__title"
        >
          The most played and banned champions, and the players who main them.
        </h2>
      </div>
      <div class="g-more__grid">
        <HomeTierlistPanelSkeleton v-if="tierlistPending" />
        <HomeTierlistPanel
          v-else
          :top-rows="rows"
          :champions-by-id="championsById"
        />
        <HomeTruemainsPanel
          :rows="truemainRows"
          :champions-by-id="championsById"
          :initial-loading="truemainsInitialLoading"
          :patch="ddragonPatch"
        />
      </div>
    </section>
  </div>
</template>
