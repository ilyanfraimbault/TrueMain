<script setup lang="ts">
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'
import { resolveWinProbability } from '#common/utils/win-probability-swing'
import { MATCH_DETAIL_SOURCE } from '#common/utils/match-detail-source'

/**
 * Inline match-detail body rendered inside an expanded match row, on the site's
 * profile and the desktop dashboard alike. Reads the game through the page's
 * `MATCH_DETAIL_SOURCE` (TrueMain's copy by default) and lays out the
 * scoreboard, the win-probability timeline, a per-player build and skill order
 * (behind a player selector) and every player's rune page, across full-width
 * tabs. Static-data maps are passed down from the
 * surrounding page so they hit the shared caches instead of being re-fetched
 * per open row.
 */
const props = defineProps<{
  nameTag: string
  matchId: string
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
  /** Champion of the row's owner — preselects that player in the Build tab. */
  selfChampionId?: number | null
}>()

const readDetail = inject(MATCH_DETAIL_SOURCE, useMatchDetail)
const { data: detail, isLoading, notFound } = readDetail(
  () => props.nameTag,
  () => props.matchId,
)

const participants = computed(() => detail.value?.participants ?? [])
const blueTeam = computed(() => participants.value.filter(p => p.teamId === 100))
const redTeam = computed(() => participants.value.filter(p => p.teamId === 200))
const blueWin = computed(() => blueTeam.value[0]?.win ?? false)

// ─── Timeline tab: the win-probability curve (#1911) ─────────────────────────
// Desktop only: built from the client's timeline the app attaches (the API sends
// no curve, #2009); a short game gets no tab rather than an empty chart. Read
// for the row owner's side.
const winProbability = computed(() => (detail.value ? resolveWinProbability(detail.value) : null))
const ownerTeamId = computed(() => {
  const owner = props.selfChampionId ? participants.value.find(p => p.championId === props.selfChampionId) : null
  return owner?.teamId ?? 100
})

const tabItems = computed(() => [
  { value: 'general', label: 'Overview', slot: 'general' as const },
  ...(winProbability.value ? [{ value: 'timeline', label: 'Timeline', slot: 'timeline' as const }] : []),
  { value: 'details', label: 'Build', slot: 'details' as const },
  { value: 'runes', label: 'Runes', slot: 'runes' as const },
])
</script>

<template>
  <!-- Fully opaque body so the expanded panel reads as one card with the row
       header instead of showing page through the tabs and the gaps between
       cards. This used to be `bg-default/90` plus a heavy backdrop-blur to hide
       the animated eclipse behind it; the backdrop no longer renders outside
       the home hero, so the fill can simply be solid.
       `bg-muted` rather than `bg-default`: the body is the *recessed* step of
       the ladder, a well the `surface` panels inside it sit up out of. Painting
       it at the page colour would punch a hole through the row instead. -->
  <div class="border-t border-default bg-muted px-3 pb-3 pt-3">
    <!-- Detailed skeleton, not a spinner: the accordion opens straight to
         ~the loaded height and the real content swaps in without the row
         lurching once the (large) detail fetch resolves. -->
    <MatchDetailSkeleton v-if="isLoading && !detail" />

    <UEmpty
      v-else-if="notFound || !detail"
      size="sm"
      icon="i-lucide-file-question"
      description="Match details unavailable."
    />

    <!-- Full-width pills, one per tab: the dashboard's segmented control. -->
    <UTabs
      v-else
      :items="tabItems"
      default-value="general"
      variant="pill"
      color="neutral"
      size="sm"
      class="w-full"
      :unmount-on-hide="false"
      :ui="{
        list: 'w-full ring ring-default',
        indicator: 'rounded-md bg-accented shadow-none',
        trigger: 'flex-1 data-[state=active]:text-highlighted',
      }"
    >
      <!-- ── General: scoreboard ─────────────────────────────────────── -->
      <template #general>
        <div class="mt-3 flex flex-col gap-3">
          <MatchDetailScoreboard
            :participants="blueTeam"
            :team-id="100"
            :win="blueWin"
            :champions="champions"
            :items="items"
            :summoner-spells="summonerSpells"
            :rune-tree="runeTree"
          />
          <MatchDetailScoreboard
            :participants="redTeam"
            :team-id="200"
            :win="!blueWin"
            :champions="champions"
            :items="items"
            :summoner-spells="summonerSpells"
            :rune-tree="runeTree"
          />
        </div>
      </template>

      <!-- ── Timeline: win-probability curve and turning points ──────── -->
      <template #timeline>
        <MatchDetailWinProbability
          v-if="winProbability"
          class="mt-3"
          :win-probability="winProbability"
          :perspective-team-id="ownerTeamId"
          :participants="participants"
          :champions="champions"
        />
      </template>

      <!-- ── Build: player selector + single-player breakdown ───────── -->
      <template #details>
        <MatchDetailBuildTab
          class="mt-3"
          :blue-team="blueTeam"
          :red-team="redTeam"
          :champions="champions"
          :items="items"
          :self-champion-id="selfChampionId"
        />
      </template>

      <!-- ── Runes: compact 10-player grid ───────────────────────────── -->
      <template #runes>
        <!-- Container-sized like the Build tab's selector: on a
             1440px viewport `lg:` matched inside the 2xl drawer too, packing
             five rune pages into a 624px box. -->
        <div class="mt-3 grid grid-cols-2 gap-2 @md:grid-cols-3 @2xl:grid-cols-5">
          <MatchDetailRunePage
            v-for="p in participants"
            :key="`runes-${p.participantId}`"
            :participant="p"
            :champions="champions"
            :rune-tree="runeTree"
          />
        </div>
      </template>
    </UTabs>
  </div>
</template>
