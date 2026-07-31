<script setup lang="ts">
import type { CompositionBuildRequest } from '~~/shared/types/composition'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import type { CompositionGamePilot } from '~~/shared/types/composition'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { favoriteNameTag } from '~/utils/favorites'
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

function pilotLabel(pilot: CompositionGamePilot | null): string {
  if (!pilot) return 'Unknown player'
  return pilot.tagLine ? `${pilot.gameName}#${pilot.tagLine}` : pilot.gameName
}

// Route slug of the pilot's profile — the same `name-tag` form the truemain
// routes use, and the identity the row's detail fetch
// (`/truemains/{nameTag}/matches/{matchId}`) is scoped by. So one value both
// links the pilot's profile and makes the row expandable; without a resolved
// Riot account the row degrades to a static, non-expandable article.
function pilotNameTag(pilot: CompositionGamePilot | null): string | null {
  if (!pilot) return null
  return favoriteNameTag(pilot.gameName, pilot.tagLine)
}

// Resolved eagerly: a `<component :is>` given the *string* "NuxtLink" renders a
// literal <nuxtlink> element (no resolution happens for dynamic string names),
// which looks right in the DOM inspector but navigates nowhere.
const NuxtLinkComponent = resolveComponent('NuxtLink')

// Profile icons are keyed by the *current* Data Dragon version, not by the
// patch the games were played on: `data.patch` is a game version ("15.14"),
// and Data Dragon has no `15.14.1` bundle for every one of those — the icon
// 404s and the row keeps its skeleton forever, which read as an endless
// spinner next to every pilot.
const { data: ddragonVersions } = useDDragonVersions()
const iconPatch = computed(() => ddragonVersions.value?.[0] ?? data.value?.patch ?? null)

function pilotIconUrl(pilot: CompositionGamePilot | null): string | null {
  // Icon id 0 is the API's "never resolved" marker — there is no such asset,
  // so asking for it would only 404.
  if (!pilot || pilot.profileIconId <= 0) return null
  return getProfileIconUrl(pilot.profileIconId, iconPatch.value)
}
</script>

<template>
  <UDrawer
    :open="open"
    direction="right"
    :title="title"
    description="The games this build was computed from, in the order the selection picked them."
    :ui="{
      // Wide enough for a MatchRow to clear its @xl tier, where the row shows
      // both team compositions — the reason to open this drawer at all.
      content: 'w-full sm:max-w-2xl',
      // The drawer owns the full viewport height: the game list scrolls inside
      // the body and the pagination stays pinned in the footer, instead of the
      // whole column scrolling and the pager drifting off the bottom.
      container: 'h-full min-h-0 gap-3 overflow-hidden',
      body: 'min-h-0 flex-1 overflow-y-auto',
      footer: 'shrink-0',
    }"
    @update:open="emit('update:open', $event)"
  >
    <template #body>
      <div class="flex flex-col gap-2 pb-1">
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
            class="flex flex-col gap-1"
          >
            <!-- Pilot + score strip: sits above the row so the row itself stays
                 the unmodified match-history component (which sizes itself off
                 the drawer's width — it's an @container). The pilot links to
                 their profile; no "Main" badge — every game here comes from a
                 player the selection picked for this champion, so the badge
                 labelled the majority of the list and told the reader nothing. -->
            <div class="flex items-center gap-2 px-0.5 text-xs text-muted">
              <component
                :is="pilotNameTag(game.pilot) ? NuxtLinkComponent : 'span'"
                :to="pilotNameTag(game.pilot)
                  ? `/truemains/${encodeURIComponent(pilotNameTag(game.pilot)!)}`
                  : undefined"
                class="flex min-w-0 items-center gap-2"
                :class="game.pilot ? 'hover:text-primary' : ''"
              >
                <SkeletonImage
                  v-if="pilotIconUrl(game.pilot)"
                  :src="pilotIconUrl(game.pilot)"
                  :alt="pilotLabel(game.pilot)"
                  :width="18"
                  :height="18"
                  class="size-[18px] shrink-0 rounded-full"
                />
                <!-- No resolvable icon: a settled placeholder rather than a
                     skeleton, which would pulse forever on a pilot whose
                     account never carried a profile icon. -->
                <span
                  v-else
                  class="flex size-[18px] shrink-0 items-center justify-center rounded-full bg-elevated"
                  aria-hidden="true"
                >
                  <UIcon name="i-lucide-user" class="size-2.5 text-dimmed" />
                </span>
                <span class="truncate font-medium text-default">{{ pilotLabel(game.pilot) }}</span>
              </component>
            </div>
            <!-- `name-tag` makes the row an accordion over the pilot's slice of
                 the game: the detail endpoint is scoped by whoever played it,
                 and here that's the pilot, not the visiting user. -->
            <MatchRow
              :match="game.match"
              :champions="champions"
              :items="items"
              :summoner-spells="summonerSpells"
              :rune-tree="runeTree ?? { styles: [], perks: {}, perkStyles: {}, shardSlots: [] }"
              :name-tag="pilotNameTag(game.pilot)"
            />
          </div>

        </template>
      </div>
    </template>

    <!-- Pagination lives in the footer, outside the scrolling body, so it stays
         pinned to the bottom of the drawer whatever the page is showing. -->
    <template #footer>
      <UPagination
        v-if="data && data.total > data.pageSize"
        :page="page"
        :total="data.total"
        :items-per-page="data.pageSize"
        :sibling-count="1"
        color="neutral"
        variant="ghost"
        active-color="primary"
        active-variant="soft"
        class="justify-center"
        @update:page="loadPage"
      />
    </template>
  </UDrawer>
</template>
