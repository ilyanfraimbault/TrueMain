<script setup lang="ts">
import type { CompositionBuildRequest } from '~~/shared/types/composition'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { describeFetchError } from '~/utils/errors'

/**
 * Provenance drawer for the composition recommendation (#940): the games the
 * "Games used" stat counts, one row per game (reusing the plain match-history
 * `MatchRow`), each annotated with the similarity score the aggregation
 * weighed it by and the Riot account that piloted it. Ordered the way the
 * selection ordered them — mains first, then best score, recency breaking
 * ties — so the page is a plain paginated slice of that order, never re-sorted
 * client-side.
 *
 * Fetches lazily on open rather than alongside the recommendation itself: the
 * matchup page refires the recommendation on every draft edit (400ms
 * debounce), and hydrating up to a hundred match rows on each keystroke for a
 * panel most visits never open would be paid by everyone (#940).
 */
const props = defineProps<{
  open: boolean
  championId: number | null
  /** Same body the recommendation was fetched with — the draft is the selection's identity. */
  draftRequest: CompositionBuildRequest | null
  championName: string | null
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse | null
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
}>()

const PAGE_SIZE = 10

const { data, isLoading, error, fetchPage, clear } = useCompositionBuildGames()
const page = ref(1)

function loadPage(targetPage: number) {
  if (props.championId === null || !props.draftRequest) return
  page.value = targetPage
  void fetchPage(props.championId, props.draftRequest, targetPage, PAGE_SIZE)
}

// Fetch on open, and again if the draft changes while the drawer stays open
// (a debounced recommendation can still land while the user is browsing the
// list) — closed, it just goes stale and refetches from page 1 next open.
watch(() => props.open, (isOpen) => {
  if (isOpen) loadPage(1)
})
watch(() => props.draftRequest, () => {
  if (props.open) loadPage(1)
}, { deep: true })

onBeforeUnmount(clear)

const title = computed(() =>
  props.championName ? `Games used for ${props.championName}` : 'Games used')

function pilotLabel(pilot: { gameName: string, tagLine: string | null } | null): string {
  if (!pilot) return 'Unknown player'
  return pilot.tagLine ? `${pilot.gameName}#${pilot.tagLine}` : pilot.gameName
}
</script>

<template>
  <UDrawer
    :open="open"
    direction="right"
    :title="title"
    description="The games this build was computed from, in the order the selection picked them."
    :ui="{ content: 'w-full sm:max-w-xl' }"
    @update:open="emit('update:open', $event)"
  >
    <template #body>
      <div class="flex flex-col gap-3">
        <UAlert
          v-if="error"
          color="error"
          variant="soft"
          title="Games unavailable"
          :description="describeFetchError(error)"
        />

        <template v-if="isLoading && !data">
          <MatchRowSkeleton v-for="i in 4" :key="`games-drawer-skel-${i}`" />
        </template>

        <template v-else-if="data">
          <p
            v-if="data.games.length === 0"
            class="py-8 text-center text-sm text-muted"
          >
            No sampled games to show.
          </p>

          <div
            v-for="game in data.games"
            :key="game.match.matchId"
            class="flex flex-col gap-1.5"
          >
            <!-- Pilot + score strip: sits above the row so the row itself stays
                 the unmodified match-history component. -->
            <div class="flex items-center gap-2 px-0.5 text-xs text-muted">
              <SkeletonImage
                :src="game.pilot ? getProfileIconUrl(game.pilot.profileIconId, data.patch) : null"
                :alt="pilotLabel(game.pilot)"
                :width="18"
                :height="18"
                class="size-[18px] shrink-0 rounded-full"
              />
              <span class="truncate font-medium text-default">{{ pilotLabel(game.pilot) }}</span>
              <UBadge
                v-if="game.isTruemain"
                color="primary"
                variant="subtle"
                size="sm"
              >
                Main
              </UBadge>
              <!-- maxPossibleScore is 0 when the draft carried no composition
                   slot — every game then scores 0 too, and "0/0 match" reads
                   as broken rather than as "nothing to compare against". -->
              <span
                v-if="data.maxPossibleScore > 0"
                class="ml-auto shrink-0 tabular-nums"
              >
                {{ game.score }}/{{ data.maxPossibleScore }} match
              </span>
            </div>
            <MatchRow
              :match="game.match"
              :champions="champions"
              :items="items"
              :summoner-spells="summonerSpells"
              :rune-tree="runeTree ?? { styles: [], perks: {}, perkStyles: {}, shardSlots: [] }"
            />
          </div>

          <UPagination
            v-if="data.total > data.pageSize"
            :page="page"
            :total="data.total"
            :items-per-page="data.pageSize"
            :sibling-count="1"
            color="neutral"
            variant="ghost"
            active-color="primary"
            active-variant="soft"
            class="justify-center pt-2"
            @update:page="loadPage"
          />
        </template>
      </div>
    </template>
  </UDrawer>
</template>
