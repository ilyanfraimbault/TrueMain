<script setup lang="ts">
import type { CompositionBuildRequest, CompositionBuildResponse } from '#shared/types/composition'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { POSITION_BY_VALUE, type ChampionPosition } from '#common/utils/positions'

// The matchup page's recommendation, in each of its states: loading, the
// standard-build fallback, no similar games, and the recommendation itself.
const props = defineProps<{
  loading: boolean
  recommendation: CompositionBuildResponse | null
  playedChampionId: number
  playedPosition: ChampionPosition
  playedChampion: ChampionStaticListItem | null
  opponentChampion: ChampionStaticListItem | null
  draftRequest: CompositionBuildRequest | null
  champions: ChampionStaticListItem[]
}>()

/** Owned by the page, because the strip that opens it lives there (#1111). */
const gamesDrawerOpen = defineModel<boolean>('gamesDrawerOpen', { required: true })

/**
 * There is exactly one degraded state left (#1075): the matchup was pinned and
 * **never recorded**, so the API returns zero games rather than relaxing the
 * filter (`CompositionMatchQueryService`: `MatchupFound = !matchupRequested ||
 * selectedMatches.Count > 0`). With no matchup build to render, the champion's
 * standard build is the only thing there is.
 *
 * A *thin* matchup is no longer a degraded state. It used to be hidden below
 * eight games behind an alert and a "Show it anyway" button — which contradicted
 * the rule the champion page has followed since #923 ("show the matchup whatever
 * its volume, with the game count on it, rather than hiding thin sections"), and
 * which mattered far more than it looks: the production measurement behind that
 * decision put the **median** champion x opponent x position pair at 4 games, so
 * the floor was withholding the matchup build for over half of all pairs. The
 * build now always renders, and `RecommendationPanel` qualifies it in place —
 * its own low-sample tooltip fires under 20 games, well above anything this
 * floor caught.
 */
const matchupMissing = computed(() =>
  props.recommendation !== null
  && props.recommendation.matchupRequested
  && !props.recommendation.matchupFound)

/** Tooltip on the standard build's warning dot — the whole explanation, in one line. */
const missingMatchupNotice = computed(() => {
  const champion = props.playedChampion?.name ?? 'this champion'
  const opponent = props.opponentChampion?.name ?? 'that champion'
  const roleLabel = POSITION_BY_VALUE.get(props.playedPosition)?.label.toLowerCase() ?? null
  const role = roleLabel ? ` at ${roleLabel}` : ''
  return `No ${champion} vs ${opponent} game${role}, standard build shown`
})
</script>

<template>
  <!-- Any fetch in flight, first or not (#1659 follow-up): the recommendation
       panel's own layout with nothing resolved in it, rather than the previous
       answer dimmed. A cold recommendation runs for tens of seconds, and over
       that span a dimmed panel does not read as loading — it reads as a page
       that dimmed itself, while its stale numbers stay legible enough to be
       taken for the new ones. The header is scaffolded here because the panel
       owning it is what we're waiting for. -->
  <SectionCard v-if="loading">
    <template #title>
      <div
        class="flex flex-wrap items-center gap-x-2.5 gap-y-1"
        aria-hidden="true"
      >
        <USkeleton class="size-7 rounded-lg" />
        <USkeleton class="h-4 w-56" />
        <USkeleton class="size-7 rounded-lg" />
      </div>
    </template>
    <ChampionBuildCoreSkeleton />
  </SectionCard>

  <template v-else-if="recommendation">
    <!-- Matchup pinned but never recorded: there is no matchup build to
         render, so this falls back to the champion's baseline one. The whole
         explanation rides in the card's warning-icon tooltip — a banner here
         was three lines of prose above a build the reader can already see. -->
    <BuilderFallbackBuild
      v-if="matchupMissing"
      :champion-id="playedChampionId"
      :position="playedPosition"
      :champion-name="playedChampion?.name ?? null"
      :notice="missingMatchupNotice"
    />

    <SectionCard
      v-else-if="recommendation.build.gamesConsidered === 0"
      :title="playedChampion ? `Recommended for ${playedChampion.name}` : 'Recommendation'"
    >
      <!-- Description-only: this card's title is an `<h3>` and `UEmpty`'s
           `title` prop renders an `<h2>` — see FallbackBuild.vue. -->
      <UEmpty icon="i-lucide-search-x">
        <template #description>
          <span class="block font-medium text-highlighted">No similar games found</span>
          Nothing recorded for this champion at this position yet.
        </template>
      </UEmpty>
    </SectionCard>

    <!-- Any matchup with at least one game: shown, however thin. The panel
         carries its own low-sample warning dot, so nothing is added here. -->
    <BuilderRecommendationPanel
      v-else
      v-model:games-drawer-open="gamesDrawerOpen"
      :recommendation="recommendation"
      :champion-name="playedChampion?.name ?? null"
      :champion-icon-url="playedChampion?.iconUrl ?? null"
      :opponent-name="opponentChampion?.name ?? null"
      :opponent-icon-url="opponentChampion?.iconUrl ?? null"
      :draft-request="draftRequest"
      :champions="champions"
    />
  </template>
</template>
