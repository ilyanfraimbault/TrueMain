<script setup lang="ts">
import type { Lane, TeamRow } from '~/types/draft'
import type { DraftState } from '~/types/lcu'
import type { ViewedPick } from '~/composables/useDraftSubject'
import { LANES, LANE_LABELS, laneIconUrl } from '~/types/draft'

/**
 * Champion select, laid out like the reference client: each side's bans and
 * the clock over the two teams, our lane duel between them, and under them
 * what the draft is for right now — the picks worth making while ours is
 * open, the build to run once it is locked.
 */
const props = withDefaults(defineProps<{
  draft: DraftState
  /** What the strip says the draft is waiting for. */
  label?: string
}>(), { label: 'Champion select' })

const { nameOf } = useChampionStatics()
const { laneEntries, entryOf } = useTierList()
const { state: lcu } = useLcuState()

const draft = toRef(props, 'draft')

// ─── The pool, and the answer for it ────────────────────────────────────────

/** How many of the player's own champions are ranked: their ten most played on the lane. */
const POOL_SIZE = 10

/** The player's champions, most mastery first — empty until the client has said. */
const championPool = computed(() => lcu.value.championPool ?? [])
const hasPool = computed(() => championPool.value.length > 0)

/** Rank the player's own champions, or every champion played on the lane. On by default when there is a pool. */
const myPool = ref(true)

/** Champions nobody can pick any more: banned, or locked on either side. */
const unavailable = computed(() => new Set([
  ...props.draft.allyBans,
  ...props.draft.enemyBans,
  ...props.draft.enemyChampions,
  ...props.draft.myTeam.filter(slot => slot.locked && !slot.isMe && slot.championId !== null).map(slot => slot.championId!),
]))

/**
 * What the endpoint ranks. Our pool: the player's most-mastered champions that
 * are played on our lane, the first ten still available. Otherwise every
 * champion the tier list has on the lane — split into several requests when
 * it is more than one can score.
 */
const candidates = computed(() => {
  const position = props.draft.myPosition
  if (!position) return []
  if (myPool.value && hasPool.value) {
    return championPool.value
      .filter(id => !unavailable.value.has(id) && entryOf(id, position) !== null)
      .slice(0, POOL_SIZE)
  }
  return laneEntries(position).filter(entry => !unavailable.value.has(entry.championId)).map(entry => entry.championId)
})

const pinnedLanes = ref<Record<number, string>>({})
const { recommendation, pending: ranking, error: rankError } = useDraftRecommendation(draft, pinnedLanes, candidates)

const enemyLanes = computed(() => recommendation.value?.enemyLanes ?? [])
const { slots, pinned, hasCorrections, swap, reset } = useLaneAssignment(enemyLanes)
watch(pinned, value => (pinnedLanes.value = value), { deep: true })

// ─── The two teams ──────────────────────────────────────────────────────────

/** Rows follow the lanes when the queue assigns them, so row n is a matchup. */
const byLane = computed(() => props.draft.myPosition !== '')

const allyRows = computed<TeamRow[]>(() => {
  const team = props.draft.myTeam
  const cells = byLane.value
    ? LANES.map(lane => team.find(slot => slot.position === lane) ?? null)
    : Array.from({ length: 5 }, (_, index) => team[index] ?? null)
  return cells.map((slot, index) => ({
    championId: slot?.championId ?? null,
    lane: (slot?.position || (byLane.value ? LANES[index] : null)) as Lane | null,
    locked: slot?.locked ?? false,
    me: slot?.isMe ?? false,
  }))
})

/**
 * With a lane answer the enemies follow the lanes and can be corrected by
 * drag; without one — early draft, or a queue with no lanes — they are listed
 * in the order they picked, and only an empty board carries the lanes.
 */
const enemyRows = computed<TeamRow[]>(() => {
  if (recommendation.value) {
    return slots.value.map(slot => ({
      championId: slot.championId !== null && props.draft.enemyChampions.includes(slot.championId) ? slot.championId : null,
      lane: slot.lane,
      locked: true,
    }))
  }
  const open = byLane.value && props.draft.enemyChampions.length === 0
  return Array.from({ length: 5 }, (_, index) => ({
    championId: props.draft.enemyChampions[index] ?? null,
    lane: (open ? LANES[index] ?? null : null) as Lane | null,
    locked: true,
  }))
})


// ─── Whose build, and which half of the screen ──────────────────────────────

const viewed = ref<ViewedPick | null>(null)
const previewed = ref<number | null>(null)
const { subject, shownView, duel } = useDraftSubject(draft, enemyLanes, viewed, previewed)
const { build, pending: buildPending, error: buildError } = useDraftBuild(subject)

/** A click reads that champion's build; a second click on it goes back. */
function view(team: 'ally' | 'enemy', championId: number) {
  const isMe = team === 'ally' && championId === props.draft.myChampion
  if (isMe) {
    // Our own card toggles our build while the pick is open; once it is
    // locked the build is already the default, and the click only comes back to it.
    const onMine = mode.value === 'build' && shownView.value === null && previewed.value === null
    viewed.value = null
    previewed.value = null
    showBuild.value = !props.draft.myChampionLocked && !onMine
    return
  }
  viewed.value = shownView.value?.championId === championId ? null : { team, championId }
}

/** Our own build, opened by hand while the pick is still open. */
const showBuild = ref(false)
// A lock, or a new hover, is a new moment in the draft: back to what it calls for.
watch(() => [props.draft.myChampion, props.draft.myChampionLocked].join(':'), () => {
  showBuild.value = false
  previewed.value = null
})

/** Picks while ours is open and there is a lane to rank for; the build otherwise. */
const mode = computed<'pick' | 'build'>(() => {
  if (shownView.value || previewed.value !== null || showBuild.value) return 'build'
  return !props.draft.myChampionLocked && byLane.value ? 'pick' : 'build'
})

const canGoBack = computed(() => shownView.value !== null || previewed.value !== null || showBuild.value)

/**
 * The one card ringed: the champion whose build and lane are on screen. It is
 * ours by default — our slot, even empty, since the picks ranked are for it —
 * and the clicked champion while its build is shown.
 */
const selectedCell = computed(() => {
  const shown = shownView.value
  if (!shown) return { ally: allyRows.value.findIndex(row => row.me), enemy: -1 }
  const rows = shown.team === 'ally' ? allyRows.value : enemyRows.value
  const index = rows.findIndex(row => row.championId === shown.championId)
  return shown.team === 'ally' ? { ally: index, enemy: -1 } : { ally: -1, enemy: index }
})

/** Its lane opponent, marked on the other side so the duel reads across the board. */
const opponentCell = computed(() => {
  const id = duel.value.opponentId
  if (id === null) return { ally: -1, enemy: -1 }
  const onAllies = shownView.value?.team === 'enemy'
  const index = (onAllies ? allyRows.value : enemyRows.value).findIndex(row => row.championId === id)
  return onAllies ? { ally: index, enemy: -1 } : { ally: -1, enemy: index }
})
function back() {
  viewed.value = null
  previewed.value = null
  showBuild.value = false
}

// ─── The damage mix (#1907) ─────────────────────────────────────────────────

/**
 * Our side's picks for the damage bar: the locked ones, and our own pick being
 * weighed — hovered in the client, or a suggestion opened here — as a preview.
 * Other allies' hovers are not counted: they are not picks yet.
 */
const allyDamagePicks = computed(() => {
  const picks: { championId: number, position: string | null, preview?: boolean }[] = allyRows.value
    .filter(row => row.championId !== null && row.locked && !(row.me && previewed.value !== null))
    .map(row => ({ championId: row.championId!, position: row.lane }))
  const mine = allyRows.value.find(row => row.me)
  const weighed = previewed.value ?? (mine && !mine.locked ? mine.championId : null)
  if (weighed !== null) picks.push({ championId: weighed, position: mine?.lane ?? (props.draft.myPosition || null), preview: true })
  return picks
})

/** Theirs, on the lanes the guesser (and the player's corrections) put them. */
const enemyDamagePicks = computed(() => enemyRows.value
  .filter(row => row.championId !== null)
  .map(row => ({ championId: row.championId!, position: row.lane })))

/** Our locked allies, ourselves excluded: what a suggested pick's damage note is read against. */
const lockedAllies = computed(() => allyRows.value
  .filter(row => row.championId !== null && row.locked && !row.me)
  .map(row => ({ championId: row.championId!, position: row.lane })))

const suggested = computed(() => (recommendation.value?.candidates ?? []).filter(candidate => !candidate.thinSample).map(candidate => candidate.championId))
</script>

<template>
  <div class="flex h-full flex-col gap-3 px-4 pb-4 pt-3">
    <DraftTopStrip :ally-bans="draft.allyBans" :enemy-bans="draft.enemyBans" :seconds-left="draft.secondsLeft" :label="label" />

    <div class="grid grid-cols-[minmax(0,1fr)_8.5rem_minmax(0,1fr)] gap-3">
      <div class="flex flex-col gap-1.5">
        <DraftTeam
          team="ally"
          :rows="allyRows"
          :selected-cell="selectedCell.ally"
          :opponent-cell="opponentCell.ally"
          :suggested="suggested"
          @view="view('ally', $event)"
        />
        <DraftDamageBar team="ally" :picks="allyDamagePicks" />
      </div>

      <DraftLaneDuel
        :champion-id="duel.championId"
        :opponent-id="duel.opponentId"
        :position="duel.position"
        :lane="build && build.championId === duel.championId ? build.lane : null"
        :pending="buildPending"
        class="pb-6"
      />

      <div class="flex flex-col gap-1.5">
        <div class="relative">
          <DraftTeam
            team="enemy"
            :rows="enemyRows"
            :selected-cell="selectedCell.enemy"
            :opponent-cell="opponentCell.enemy"
            :correctable="recommendation !== null"
            @view="view('enemy', $event)"
            @swap="swap"
          />
          <UButton
            v-if="hasCorrections"
            size="xs"
            color="neutral"
            variant="ghost"
            icon="i-lucide-rotate-ccw"
            label="Reset lanes"
            class="absolute -bottom-1.5 right-0"
            @click="reset"
          />
        </div>
        <DraftDamageBar team="enemy" :picks="enemyDamagePicks" />
      </div>
    </div>

    <div class="min-h-0 flex-1">
      <DraftSuggestions
        v-if="mode === 'pick'"
        v-model:my-pool="myPool"
        :has-pool="hasPool"
        :pool="candidates"
        :candidates="recommendation?.candidates ?? []"
        :pending="ranking"
        :error="rankError"
        :position="draft.myPosition"
        :allies="lockedAllies"
        @preview="previewed = $event"
      />

      <BuildView
        v-else-if="subject"
        :champion-id="subject.championId"
        :position="subject.request.position"
        :draft="{ build, pending: buildPending, error: buildError, opponentId: duel.opponentId, label: 'This draft' }"
      >
        <template #advice>
          <DraftItemAdvice :subject="subject" />
        </template>
        <template #header>
          <div class="flex items-center gap-2.5">
            <ChampionPortrait :champion-id="subject.championId" size="sm" class="size-10! rounded-lg!" />
            <p class="min-w-0 truncate text-sm font-semibold text-highlighted">{{ nameOf(subject.championId) }}</p>
            <img
              v-if="subject.request.position in LANE_LABELS"
              :src="laneIconUrl(subject.request.position)"
              :alt="LANE_LABELS[subject.request.position as Lane]"
              :title="LANE_LABELS[subject.request.position as Lane]"
              class="size-4 shrink-0"
            >
            <span class="flex-1" />
            <UButton v-if="canGoBack" size="xs" color="neutral" variant="ghost" icon="i-lucide-arrow-left" aria-label="Back" @click="back" />
          </div>
        </template>
      </BuildView>

      <div v-else class="surface flex h-full flex-col items-center justify-center gap-2 rounded-xl text-center">
        <UIcon name="i-lucide-scroll-text" class="size-6 text-dimmed" />
        <p class="text-sm text-muted">The build shows once a champion is picked</p>
      </div>
    </div>
  </div>
</template>
