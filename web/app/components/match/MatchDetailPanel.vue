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

const tabItems = [
  { value: 'general', label: 'General', slot: 'general' as const },
  { value: 'details', label: 'Details', slot: 'details' as const },
  { value: 'runes', label: 'Runes', slot: 'runes' as const },
]
</script>

<template>
  <!-- Opaque body surface so the expanded panel reads as one card with the row
       header instead of letting the animated backdrop bleed through the tabs
       and gaps between cards. The heavy backdrop-blur keeps the rose-gold
       eclipse from showing through the surface's remaining transparency. -->
  <div class="border-t border-default/60 bg-default/90 px-3 pb-3 pt-3 backdrop-blur-2xl">
    <!-- Detailed skeleton, not a spinner: the accordion opens straight to
         ~the loaded height and the real content swaps in without the row
         lurching once the (large) detail fetch resolves. -->
    <MatchDetailSkeleton v-if="isLoading && !detail" />

    <div
      v-else-if="notFound || !detail"
      class="rounded-md border border-default/60 bg-elevated/60 p-6 text-center text-sm text-muted"
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
          <!-- Selector: blue team · vs · red team, spread across the full width -->
          <div class="flex items-center justify-center gap-3 overflow-x-auto pb-1 sm:justify-between">
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
                    class="size-14 rounded"
                  />
                  <img
                    v-if="p.teamPosition"
                    :src="getPositionIconUrl(p.teamPosition)"
                    :alt="p.teamPosition"
                    class="absolute -bottom-1 -left-1 size-5 rounded-full bg-default p-0.5 ring-1 ring-default"
                    aria-hidden="true"
                  >
                </button>
              </div>

              <span
                v-if="teamIdx === 0"
                class="mx-2 shrink-0 select-none text-2xl font-semibold text-muted"
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

      <!-- ── Runes: compact 10-player grid ───────────────────────────── -->
      <template #runes>
        <div class="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
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
