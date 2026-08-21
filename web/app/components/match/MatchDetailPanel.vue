<script setup lang="ts">
import type { MatchDetailParticipant } from '~~/shared/types/match-detail'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { getPositionIconUrl } from '~~/shared/utils/ddragon'

/**
 * Inline match-detail body rendered inside an expanded `MatchRow` accordion.
 * Owns the single-match fetch (lazy, client-only) and lays out the scoreboard,
 * a per-player build/skill breakdown (behind a player selector) and the rune
 * pages across three tabs. Static-data maps are passed down from the
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
  /** Champion of the row's owner — preselects that player in the Details tab. */
  selfChampionId?: number | null
}>()

const { data: detail, isLoading, notFound } = useMatchDetail(
  () => props.nameTag,
  () => props.matchId,
)

// Lane glyph badges are plain <img> (ten of them per scoreboard), so they need
// the canonical URL built explicitly rather than the raw `/positions/*.png`.
const canonicalIcon = useCanonicalIcon()

const participants = computed(() => detail.value?.participants ?? [])
const blueTeam = computed(() => participants.value.filter(p => p.teamId === 100))
const redTeam = computed(() => participants.value.filter(p => p.teamId === 200))
const blueWin = computed(() => blueTeam.value[0]?.win ?? false)

function champIcon(championId: number) {
  return props.champions.find(c => c.championId === championId)?.iconUrl ?? null
}
function champName(championId: number) {
  return props.champions.find(c => c.championId === championId)?.name ?? `Champion ${championId}`
}

// ─── Details tab: selected player ───────────────────────────────────────────
// Null until the user picks someone; the computed falls back to the row owner's
// champion (so the tab opens on "you"), then the first participant.
const selectedId = ref<number | null>(null)

const selectedParticipant = computed<MatchDetailParticipant | null>(() => {
  const list = participants.value
  if (!list.length) return null
  const byId = list.find(p => p.participantId === selectedId.value)
  if (byId) return byId
  if (props.selfChampionId) {
    const bySelf = list.find(p => p.championId === props.selfChampionId)
    if (bySelf) return bySelf
  }
  return list[0] ?? null
})

function selectPlayer(participantId: number) {
  selectedId.value = participantId
}

// ─── Jungle tab: both junglers' first clears (#1186) ────────────────────────
// Per team, prefer the actual JUNGLE participant; the builder identifies
// junglers by a jungle-CS threshold, so a jungle-farming laner can also carry
// a row — fall back to whoever has the most steps.
function junglerOf(team: MatchDetailParticipant[]) {
  const withClear = team.filter(p => p.jungleClear && p.jungleClear.steps.length > 0)
  if (!withClear.length) return null
  return withClear.find(p => p.teamPosition === 'JUNGLE')
    ?? withClear.reduce((best, p) =>
      p.jungleClear!.steps.length > best.jungleClear!.steps.length ? p : best)
}

const blueJungler = computed(() => junglerOf(blueTeam.value))
const redJungler = computed(() => junglerOf(redTeam.value))

// The Jungle tab only exists when the match carries first-clear data — older
// matches, remakes and timeline-less games get three tabs, not an empty one.
const tabItems = computed(() => [
  { value: 'general', label: 'General', slot: 'general' as const },
  { value: 'details', label: 'Details', slot: 'details' as const },
  ...(blueJungler.value || redJungler.value
    ? [{ value: 'jungle', label: 'Jungle', slot: 'jungle' as const }]
    : []),
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

    <div
      v-else-if="notFound || !detail"
      class="surface rounded-md p-6 text-center text-sm text-muted"
    >
      Match details unavailable.
    </div>

    <UTabs
      v-else
      :items="tabItems"
      default-value="general"
      variant="link"
      class="w-full"
      :unmount-on-hide="false"
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

      <!-- ── Details: player selector + single-player breakdown ──────── -->
      <template #details>
        <div class="mt-3 flex flex-col gap-3">
          <!-- Selector: blue team · vs · red team. Sized off the *row's* width
               (`MatchRow` is the `@container`), never the viewport: the same row
               renders full-bleed on a profile and inside a 2xl drawer, so a
               `sm:` here would pick the wrong layout in one of the two.
               Ten 56px portraits plus their gaps need ~694px, so the two teams
               only sit side by side from `@3xl`; below that they stack, which
               is what fits the drawer. `@sm` shrinks them again on a phone. -->
          <div class="flex flex-col items-center gap-2 overflow-x-auto pb-1 @3xl:flex-row @3xl:justify-between @3xl:gap-3">
            <template v-for="(team, teamIdx) in [blueTeam, redTeam]" :key="`team-${teamIdx}`">
              <div class="flex shrink-0 items-center gap-2">
                <button
                  v-for="p in team"
                  :key="`sel-${p.participantId}`"
                  type="button"
                  class="relative shrink-0 rounded transition-all"
                  :class="p.participantId === selectedParticipant?.participantId
                    ? 'ring-2 ring-primary'
                    : 'opacity-60 hover:opacity-100'"
                  :title="champName(p.championId)"
                  :aria-label="`Show ${champName(p.championId)} details`"
                  :aria-pressed="p.participantId === selectedParticipant?.participantId"
                  @click="selectPlayer(p.participantId)"
                >
                  <SkeletonImage
                    :src="champIcon(p.championId)"
                    :alt="champName(p.championId)"
                    class="size-11 rounded @sm:size-14"
                  />
                  <img
                    v-if="p.teamPosition"
                    :src="canonicalIcon(getPositionIconUrl(p.teamPosition))"
                    :alt="p.teamPosition"
                    class="absolute -bottom-1 -left-1 size-5 rounded-full bg-default p-0.5 ring-1 ring-default"
                    aria-hidden="true"
                  >
                </button>
              </div>

              <span
                v-if="teamIdx === 0"
                class="shrink-0 select-none text-2xl font-semibold text-muted @3xl:mx-2"
                aria-hidden="true"
              >vs</span>
            </template>
          </div>

          <!-- Selected player -->
          <div v-if="selectedParticipant" class="flex flex-col gap-2">
            <div class="flex items-center gap-2 px-0.5">
              <span class="text-sm font-semibold text-default">
                {{ selectedParticipant.gameName ?? selectedParticipant.summonerName }}
              </span>
              <span
                class="ml-auto rounded px-1.5 py-0.5 text-[10px] font-semibold"
                :class="selectedParticipant.win ? 'bg-sky-500/15 text-sky-400' : 'bg-red-500/15 text-red-400'"
              >
                {{ selectedParticipant.win ? 'Win' : 'Loss' }}
              </span>
            </div>
            <MatchDetailPlayerPanel
              :key="selectedParticipant.participantId"
              :participant="selectedParticipant"
              :champions="champions"
              :items="items"
            />
          </div>
        </div>
      </template>

      <!-- ── Jungle: both junglers' first clears on the minimap ──────── -->
      <template #jungle>
        <!-- Container-sized like the selector above: two 512px maps only fit
             side by side from @3xl; below that they stack. -->
        <div class="mt-3 grid grid-cols-1 gap-3 @3xl:grid-cols-2">
          <template v-for="(jungler, i) in [blueJungler, redJungler]" :key="`jungler-${i}`">
            <MatchJungleClearMap
              v-if="jungler"
              :participant="jungler"
              :champions="champions"
            />
            <div
              v-else
              class="surface flex items-center justify-center rounded-md p-6 text-sm text-muted"
            >
              No first-clear data for this jungler.
            </div>
          </template>
        </div>
      </template>

      <!-- ── Runes: compact 10-player grid ───────────────────────────── -->
      <template #runes>
        <!-- Container-sized for the same reason as the selector above: on a
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
