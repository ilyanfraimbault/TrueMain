<script setup lang="ts">
import type { CompositionBuildRequest, CompositionBuildResponse } from '~~/shared/types/composition'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionPosition } from '~/utils/positions'
import type { LaneVerdict } from '~/utils/lane-verdict'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { formatGoldDiff, goldDiffTone, laneVerdict } from '~/utils/lane-verdict'

/**
 * Full composition recommendation (#563): confidence strip + the same core
 * panels the champion page renders (spells, starter, skill order, boots, core
 * path, runes) and the pruned build tree. Self-contained: mounts only when a
 * recommendation exists, so the static asset fetches (items, rune tree,
 * summoners, champion spells) fire lazily with the right patch instead of on
 * page load.
 */
const props = defineProps<{
  recommendation: CompositionBuildResponse
  championName: string | null
  championIconUrl: string | null
  /** Role opponent, when the matchup is pinned — headlines the card (#921). */
  opponentName?: string | null
  opponentIconUrl?: string | null
  /** Same opponent by id: what the lane verdict in the confidence strip reads (#976). */
  opponentChampionId?: number | null
  /**
   * Same body the recommendation was fetched with — reused verbatim by the
   * provenance drawer (#940) so it lists exactly this recommendation's
   * selection rather than re-deriving the draft from scratch.
   */
  draftRequest: CompositionBuildRequest | null
  champions: ChampionStaticListItem[]
}>()

/**
 * Below this many sampled games the aggregation is thin enough that single
 * games swing every dimension — the panel stays visible but carries an
 * explicit warning instead of fabricated certainty.
 */
const LOW_SAMPLE_FLOOR = 20

/** Mean similarity under this reads as "barely draft-specific". */
const LOW_SIMILARITY_FLOOR = 0.2

const build = computed(() => props.recommendation.build)
const confidence = computed(() => props.recommendation.confidence)

const assetsPatch = computed(() => props.recommendation.patch ?? null)
const { runeTree, itemsMap } = useBuildAssets(assetsPatch)
const { data: summonersMap } = useStaticSummonerSpells(assetsPatch)
const { data: championStatic } = useChampionStatic(
  () => props.recommendation.championId,
  () => assetsPatch.value,
)

const winRate = computed(() =>
  build.value.gamesConsidered > 0 ? build.value.wins / build.value.gamesConsidered : null)

// The matchup is the headline when it is pinned: the card answers "what do I
// build into this opponent", not "what does this champion build".
const headline = computed(() => {
  if (!props.championName) {
    return 'Recommended build'
  }
  return props.opponentName
    ? `Recommended build for ${props.championName} vs ${props.opponentName}`
    : `Recommended build for ${props.championName}`
})

const draftRequested = computed(() => confidence.value.maxPossibleScore > 0)
const lowSample = computed(() => confidence.value.sampleSize < LOW_SAMPLE_FLOOR)
const lowSimilarity = computed(() =>
  draftRequested.value && confidence.value.meanSimilarity < LOW_SIMILARITY_FLOOR)

// A terse warning shown inline next to the title when the sample is thin —
// just the fact, no advisory tail (the numbers strip already qualifies it).
const lowDataMessage = computed(() => {
  if (lowSample.value) {
    return `Only ${confidence.value.sampleSize} similar game${confidence.value.sampleSize === 1 ? '' : 's'}`
  }
  if (lowSimilarity.value) {
    return 'Few games resemble this draft'
  }
  return null
})

const gamesDrawerOpen = ref(false)

// ─── Lane verdict (#976) ─────────────────────────────────────────────────────
// Lives in the confidence strip, beside the win rate it qualifies: "we win 54% of
// these games" and "we end 15 minutes 300 gold up" are the same sentence read at
// two points in the game, and separating them made the reader compare two cards.
//
// Its own fetch off the matchup aggregate — global slice, every patch, keyed on the
// matchup alone — so editing a draft slot never refires it, and it says nothing
// about the composition sample the rest of the strip counts.
const isJungle = computed(() => props.recommendation.position === 'JUNGLE')
const laneNoun = computed<'lane' | 'matchup'>(() => (isJungle.value ? 'matchup' : 'lane'))

// No opponent pinned means nothing to show here — and the composable treats a
// null position as "don't fetch", so this also skips the network round-trip
// entirely rather than fetching the full matchup leaderboard just to discard it.
const { data: matchupData } = useChampionMatchups(
  () => props.recommendation.championId,
  () => (props.opponentChampionId == null ? null : props.recommendation.position as ChampionPosition),
  { opponentChampionId: () => props.opponentChampionId ?? null },
)

const matchup = computed(() => {
  const opponent = props.opponentChampionId
  if (opponent == null) return null
  return matchupData.value?.matchups.find(m => m.opponentChampionId === opponent) ?? null
})

const goldDiff = computed(() => matchup.value?.averageGoldDiffAt15 ?? null)
const goldLanes = computed(() => matchup.value?.goldDiffLaneGames ?? 0)
const verdict = computed(() => laneVerdict(goldDiff.value, goldLanes.value, laneNoun.value))

/**
 * The gap's cell caption. Never blank, and never the same sentence for the two
 * reasons a verdict can be missing: nothing measured at all, versus measured on a
 * sample too thin to band (the number still shows — it is the label that would be
 * the overclaim).
 */
const goldCaption = computed(() => {
  if (goldDiff.value === null) return 'not measured yet'
  const games = `${goldLanes.value.toLocaleString('en-US')} game${goldLanes.value === 1 ? '' : 's'}`
  return verdict.value === null ? `${games} — too few to call` : `avg over ${games}`
})

/** One cell of the confidence strip. `badge` carries the verdict, `tone` colours the value. */
interface StatCell {
  label: string
  value: string
  caption: string
  hint: string
  tone: string
  badge: LaneVerdict | null
}

/** Lane figures only exist once a role opponent is pinned; without one, three stats. */
const laneStats = computed<StatCell[]>(() => {
  if (matchup.value === null) return []
  return [
    {
      label: isJungle.value ? 'Ahead at 15' : 'Lane win rate',
      value: matchup.value.laneWinRate == null
        ? '—'
        : formatPercentage(matchup.value.laneWinRate, 0),
      tone: matchup.value.laneWinRate == null
        ? 'text-dimmed'
        : matchup.value.laneWinRate >= 0.5 ? 'text-emerald-400' : 'text-red-400',
      caption: matchup.value.laneWinRate == null
        ? 'nothing decided yet'
        : `of ${matchup.value.decidedLaneGames.toLocaleString('en-US')} decided`,
      hint: `Share of games of this matchup that reached 15 minutes clearly ahead, out of `
        + 'those that ended clearly ahead or behind. Measured across every recorded game '
        + 'of the matchup — not the sample this build was computed from.',
      badge: null,
    },
    {
      label: 'Gold @15',
      value: goldDiff.value === null ? '—' : formatGoldDiff(goldDiff.value),
      tone: goldDiff.value === null ? 'text-dimmed' : goldDiffTone(goldDiff.value),
      caption: goldCaption.value,
      hint: 'Average gold held over the opponent at 15 minutes across every measured game '
        + `of this matchup. The ${laneNoun.value} verdict bands this number: even inside `
        + '±150, decided past ±300.',
      badge: verdict.value,
    },
  ]
})

const stats = computed<StatCell[]>(() => [
  {
    label: 'Games used',
    value: String(build.value.gamesConsidered),
    caption: `${confidence.value.truemainGameCount} by mains · of `
      + `${confidence.value.candidatePoolSize.toLocaleString('en-US')} scanned`,
    hint: 'The build below is computed from these games only — games piloted by a '
      + 'main of the champion first, then the most similar to your draft, out of all '
      + 'recent games scanned for this champion and role.',
    tone: '',
    badge: null,
  },
  {
    label: 'Draft match',
    value: draftRequested.value ? formatPercentage(confidence.value.meanSimilarity) : '—',
    caption: 'avg similarity',
    hint: 'Average similarity between those games and your draft.',
    tone: '',
    badge: null,
  },
  {
    label: 'Win rate',
    value: winRate.value === null ? '—' : formatPercentage(winRate.value),
    caption: 'across those games',
    hint: 'Win rate across the games the build is computed from.',
    tone: '',
    badge: null,
  },
  ...laneStats.value,
])
</script>

<template>
  <SectionCard>
    <template #title>
      <div class="flex flex-wrap items-center gap-x-2.5 gap-y-1">
        <SkeletonImage
          v-if="championIconUrl"
          :src="championIconUrl"
          :alt="championName ?? ''"
          :width="28"
          :height="28"
          class="size-7 rounded-lg ring-1 ring-primary/40"
        />
        <h2 class="text-sm font-medium text-default">
          {{ headline }}
        </h2>
        <SkeletonImage
          v-if="opponentIconUrl"
          :src="opponentIconUrl"
          :alt="opponentName ?? ''"
          :width="28"
          :height="28"
          class="size-7 rounded-lg ring-1 ring-accented"
        />
        <!-- Thin-data qualifier: only the icon shows next to the title; the
             message lives in its tooltip so it never crowds the header. -->
        <UTooltip
          v-if="lowDataMessage"
          :text="lowDataMessage"
          :delay-duration="150"
        >
          <UIcon
            name="i-lucide-triangle-alert"
            class="size-4 text-warning"
          />
        </UTooltip>
      </div>
    </template>
    <div class="space-y-6">
      <!-- Confidence strip — always first: the numbers qualify everything below.
           Three cells without a pinned opponent, five with (the lane pair sits
           beside the win rate it reads against, not in a card of its own). -->
      <dl
        class="grid gap-4"
        :class="stats.length > 3
          ? 'grid-cols-2 sm:grid-cols-3 lg:grid-cols-5'
          : 'grid-cols-3'"
      >
        <div
          v-for="stat in stats"
          :key="stat.label"
          :title="stat.hint"
        >
          <dt class="flex items-center gap-1 text-sm text-muted">
            {{ stat.label }}
            <!-- Opens the provenance drawer: only meaningful once there's a
                 sample to list, and only for the stat it annotates. -->
            <UTooltip
              v-if="stat.label === 'Games used' && build.gamesConsidered > 0"
              text="See the games this build was computed from"
              :delay-duration="150"
            >
              <!-- A filled square button, not the borderless ghost icon it
                   used to be: at `ghost` + `:padded="false"` it read as part
                   of the label rather than as a control, and nothing said the
                   games behind the number could be opened. -->
              <UButton
                icon="i-lucide-eye"
                color="neutral"
                variant="subtle"
                size="xs"
                square
                aria-label="See the games this build was computed from"
                @click="() => { gamesDrawerOpen = true }"
              />
            </UTooltip>
          </dt>
          <dd
            class="text-lg font-semibold leading-tight tabular-nums"
            :class="stat.tone"
          >
            {{ stat.value }}
          </dd>
          <!-- The verdict rides under its own number, so the label is never read
               as a qualifier of the win rate two cells over. -->
          <dd v-if="stat.badge">
            <UBadge
              :color="stat.badge.color"
              :variant="stat.badge.variant"
              size="sm"
              class="font-semibold"
            >
              {{ stat.badge.label }}
            </UBadge>
          </dd>
          <dd class="text-xs text-dimmed">
            {{ stat.caption }}
          </dd>
        </div>
      </dl>

      <!-- Same layout skeleton as the champion page's build panel: flexible
           left column, fixed 240px runes column at lg+ (see BuildPanel.vue for
           the sizing rationale). -->
      <div class="grid gap-x-6 gap-y-5 lg:grid-cols-[minmax(0,1fr)_240px]">
        <div class="flex flex-col gap-5 sm:flex-row sm:items-start">
          <div class="flex flex-col gap-5">
            <ChampionCoreSpells
              :summoners="build.summonerSpells"
              :summoners-map="summonersMap ?? {}"
            />
            <ChampionCoreStarterItems
              :starter="build.starterItems"
              :items-map="itemsMap"
            />
          </div>
          <div class="flex flex-1 flex-col gap-5">
            <div class="flex flex-wrap items-start justify-around gap-6">
              <ChampionCoreSkillOrder
                v-if="championStatic"
                :skill-order="build.skillOrder"
                :champion-static="championStatic"
              />
              <ChampionCoreBoots
                :boots="build.boots"
                :items-map="itemsMap"
              />
            </div>
            <div class="flex justify-center">
              <ChampionCoreBuildPath
                :path="build.corePath"
                :items-map="itemsMap"
              />
            </div>
          </div>
        </div>
        <div class="w-full shrink-0 overflow-hidden lg:w-[240px]">
          <ChampionCoreRunes
            v-if="build.runePage && runeTree"
            :page="build.runePage"
            :tree="runeTree"
          />
          <p
            v-else
            class="text-sm text-muted"
          >
            No rune data in the sampled games.
          </p>
        </div>
      </div>

      <!-- Build tree — same component as the champion page, recomputed from the
           sampled games only. -->
      <ChampionBuildPanelBuildTree
        v-if="build.buildTree.length > 0"
        :tree="build.buildTree"
        :first-item-id="build.firstItemId"
        :item-path="build.corePath?.itemIds ?? []"
        :items-map="itemsMap"
      />
    </div>

    <BuilderGamesDrawer
      :open="gamesDrawerOpen"
      :champion-id="recommendation.championId"
      :draft-request="draftRequest"
      :champion-name="championName"
      :champions="champions"
      :items="itemsMap"
      :summoner-spells="summonersMap ?? {}"
      :rune-tree="runeTree"
      @update:open="gamesDrawerOpen = $event"
    />
  </SectionCard>
</template>
