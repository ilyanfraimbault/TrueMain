<script setup lang="ts">
import type { Lane, TeamRow } from '~/types/draft'
import type { DraftState } from '~/types/lcu'
import type { ViewedPick } from '~/composables/useDraftSubject'
import { LANES, LANE_LABELS } from '~/types/draft'

/**
 * Champion select, laid out like the reference client: each side's bans and
 * the clock over the two teams, our lane duel between them, and under them
 * what the draft is for right now — the picks worth making while ours is
 * open, the build to run once it is locked.
 *
 * The same screen reads a board the player fills by hand (`editable`): every
 * card and ban slot takes a champion, the enemies stand where they were put
 * rather than where the guesser would put them, and a suggestion clicked is
 * placed as our pick. The page owns that board; this screen only says what
 * was asked of it.
 */
const props = withDefaults(defineProps<{
  draft: DraftState
  /** What the strip says the draft is waiting for. */
  label?: string
  /** A board filled by hand rather than a live champion select. */
  editable?: boolean
  /** On such a board, the lane each enemy was placed on: drawn as placed, and pinned for the ranking. */
  placedEnemies?: Record<number, string> | null
}>(), { label: 'Champion select', editable: false, placedEnemies: null })

const emit = defineEmits<{
  /** Put a champion on a lane's card, on either side. */
  place: [team: 'ally' | 'enemy', lane: Lane]
  remove: [team: 'ally' | 'enemy', lane: Lane]
  ban: [team: 'ally' | 'enemy']
  unban: [championId: number]
  /** A suggestion clicked on a board: it becomes our pick. */
  pick: [championId: number]
}>()

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

/** Where the enemies stand: as placed on a board, as the guesser (and the player's drags) resolved them otherwise. */
const enemyLanes = computed(() => (props.placedEnemies
  ? Object.entries(props.placedEnemies).map(([id, position]) => ({ championId: Number(id), position, confidence: 1, pinned: true }))
  : recommendation.value?.enemyLanes ?? []))
const { slots, pinned, hasCorrections, swap, reset } = useLaneAssignment(enemyLanes)
watch([pinned, () => props.placedEnemies], () => (pinnedLanes.value = { ...pinned.value, ...(props.placedEnemies ?? {}) }), { deep: true, immediate: true })

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
  const placed = props.placedEnemies
  if (placed) {
    const onLane = new Map(Object.entries(placed).map(([id, lane]) => [lane, Number(id)]))
    return LANES.map(lane => ({ championId: onLane.get(lane) ?? null, lane, locked: true }))
  }
  if (recommendation.value) {
    return slots.value.map(slot => ({
      championId: slot.championId !== null && props.draft.enemyChampions.includes(slot.championId) ? slot.championId : null,
      lane: slot.lane,
      locked: true,
      confidence: slot.confidence,
      pinned: slot.pinned,
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
const { subject, shownView, duel, whose } = useDraftSubject(draft, enemyLanes, viewed, previewed)
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

const suggested = computed(() => (recommendation.value?.candidates ?? []).filter(candidate => !candidate.thinSample).map(candidate => candidate.championId))
</script>

<template>
  <div class="flex h-full flex-col gap-3 px-4 pb-4 pt-3">
    <DraftTopStrip
      :ally-bans="draft.allyBans"
      :enemy-bans="draft.enemyBans"
      :seconds-left="draft.secondsLeft"
      :label="label"
      :editable="editable"
      @ban="emit('ban', $event)"
      @unban="emit('unban', $event)"
    >
      <template v-if="$slots['strip-center']" #center>
        <slot name="strip-center" />
      </template>
    </DraftTopStrip>

    <div class="grid grid-cols-[minmax(0,1fr)_8.5rem_minmax(0,1fr)] gap-3">
      <DraftTeam
        team="ally"
        :rows="allyRows"
        :selected-cell="selectedCell.ally"
        :opponent-cell="opponentCell.ally"
        :suggested="suggested"
        :editable="editable"
        @view="view('ally', $event)"
        @place="emit('place', 'ally', $event)"
        @remove="emit('remove', 'ally', $event)"
      />

      <DraftLaneDuel
        :champion-id="duel.championId"
        :opponent-id="duel.opponentId"
        :position="duel.position"
        :lane="build && build.championId === duel.championId ? build.lane : null"
        :pending="buildPending"
        class="pb-6"
      />

      <div class="relative">
        <DraftTeam
          team="enemy"
          :rows="enemyRows"
          :selected-cell="selectedCell.enemy"
          :opponent-cell="opponentCell.enemy"
          :correctable="!editable && recommendation !== null"
          :editable="editable"
          @view="view('enemy', $event)"
          @swap="swap"
          @place="emit('place', 'enemy', $event)"
          @remove="emit('remove', 'enemy', $event)"
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
        @preview="editable ? emit('pick', $event) : (previewed = $event)"
      />

      <BuildView
        v-else-if="subject"
        :champion-id="subject.championId"
        :position="subject.request.position"
        :draft="{ build, pending: buildPending, error: buildError, label: duel.opponentId ? `vs ${nameOf(duel.opponentId)}` : 'This draft' }"
      >
        <template #header>
          <div class="flex items-center gap-2.5">
            <ChampionPortrait :champion-id="subject.championId" size="sm" class="size-10! rounded-lg!" />
            <div class="min-w-0 flex-1 leading-tight">
              <p class="truncate text-sm font-semibold text-highlighted">{{ nameOf(subject.championId) }}</p>
              <p class="mt-0.5 truncate text-[11px] text-dimmed">
                {{ LANE_LABELS[subject.request.position as Lane] ?? subject.request.position }} · {{ whose }}
              </p>
            </div>
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
