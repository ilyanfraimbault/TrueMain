<script setup lang="ts">
import type { DraftSuggestionItem } from '~/types/draft'
import type { DraftState } from '~/types/lcu'
import type { ViewedPick } from '~/composables/useDraftSubject'

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

const draft = toRef(props, 'draft')

// Hover, lock and ban, on the player's click (#1909): one instance for every control under this screen.
const { turnLabel } = provideChampSelectActions(draft)

// ─── The pool, and the answer for it ────────────────────────────────────────

const { myPool, hasPool, lanePool, candidates } = useDraftCandidates(draft)

const pinnedLanes = ref<Record<number, string>>({})
const { recommendation, pending: ranking, error: rankError } = useDraftRecommendation(draft, pinnedLanes, candidates)

/**
 * Bans are the question on our ban turn, and in the planning phase once the
 * player has declared a pick — the one the bans then protect (#1906).
 */
const banTime = computed(() => {
  const action = props.draft.myAction
  if (action?.kind === 'ban' && action.inProgress) return true
  return props.draft.timerPhase === 'PLANNING' && props.draft.myChampion !== null && !props.draft.myChampionLocked
})
const { bans, pending: banPending, error: banError } = useDraftBans(draft, lanePool, banTime)

const pickSuggestions = computed<DraftSuggestionItem[]>(() => (recommendation.value?.candidates ?? []).map(candidate => ({
  championId: candidate.championId,
  reasons: candidate.reasons ?? [],
  thin: candidate.thinSample,
})))

const banSuggestions = computed<DraftSuggestionItem[]>(() => (bans.value?.candidates ?? []).map(candidate => ({
  championId: candidate.championId,
  reasons: candidate.reasons,
  thin: false,
})))

const enemyLanes = computed(() => recommendation.value?.enemyLanes ?? [])
const { slots, pinned, hasCorrections, swap, reset } = useLaneAssignment(enemyLanes)
watch(pinned, value => (pinnedLanes.value = value), { deep: true })

// ─── The two teams ──────────────────────────────────────────────────────────

const { byLane, allyRows, enemyRows } = useDraftTeamRows(draft, recommendation, slots)

// ─── Whose build, and which half of the screen ──────────────────────────────

const viewed = ref<ViewedPick | null>(null)
const previewed = ref<number | null>(null)
const { subject, shownView, duel } = useDraftSubject(draft, enemyLanes, viewed, previewed)
const { build, pending: buildPending, error: buildError } = useDraftBuild(subject)

// ─── The player's own kits (#2185, #2205) ───────────────────────────────────

/** Our pick and lane, while our own build is the one on screen. */
const ownPick = computed(() => {
  const current = subject.value
  if (!current || shownView.value !== null || current.championId !== props.draft.myChampion) return null
  return { championId: current.championId, position: current.request.position }
})
const { kits, kitId, pickKit } = useDraftKits(ownPick)

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

/** Bans on our ban turn, picks while ours is open, with a lane to rank for; the build otherwise. */
const mode = computed<'pick' | 'ban' | 'build'>(() => {
  if (shownView.value || previewed.value !== null || showBuild.value) return 'build'
  if (!byLane.value) return 'build'
  if (banTime.value) return 'ban'
  return !props.draft.myChampionLocked ? 'pick' : 'build'
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

const { allyDamagePicks, enemyDamagePicks, lockedAllies } = useDraftDamagePicks(draft, allyRows, enemyRows, previewed)

const suggested = computed(() => (recommendation.value?.candidates ?? []).filter(candidate => !candidate.thinSample).map(candidate => candidate.championId))
</script>

<template>
  <div class="flex h-full flex-col gap-3 px-4 pb-4 pt-3">
    <DraftTopStrip :ally-bans="draft.allyBans" :enemy-bans="draft.enemyBans" :seconds-left="draft.secondsLeft" :label="turnLabel ?? label">
      <template #action>
        <DraftLockButton />
      </template>
    </DraftTopStrip>

    <div class="grid grid-cols-[minmax(0,1fr)_8.5rem_minmax(0,1fr)] gap-3">
      <DraftTeamColumn
        team="ally"
        :rows="allyRows"
        :selected-cell="selectedCell.ally"
        :opponent-cell="opponentCell.ally"
        :suggested="suggested"
        :damage-picks="allyDamagePicks"
        @view="view('ally', $event)"
      />

      <DraftLaneDuel
        :champion-id="duel.championId"
        :opponent-id="duel.opponentId"
        :position="duel.position"
        :lane="build && build.championId === duel.championId ? build.lane : null"
        :pending="buildPending"
        class="pb-6"
      />

      <DraftTeamColumn
        team="enemy"
        :rows="enemyRows"
        :selected-cell="selectedCell.enemy"
        :opponent-cell="opponentCell.enemy"
        :correctable="recommendation !== null"
        :has-corrections="hasCorrections"
        :damage-picks="enemyDamagePicks"
        @view="view('enemy', $event)"
        @swap="swap"
        @reset="reset"
      />
    </div>

    <div class="min-h-0 flex-1">
      <DraftSuggestions
        v-if="mode === 'pick' || mode === 'ban'"
        v-model:my-pool="myPool"
        :mode="mode"
        :has-pool="hasPool"
        :suggestions="mode === 'ban' ? banSuggestions : pickSuggestions"
        :target="mode === 'ban' && bans ? { kind: bans.target, championIds: bans.targetChampionIds } : null"
        :pending="mode === 'ban' ? banPending : ranking"
        :error="mode === 'ban' ? banError : rankError"
        :position="draft.myPosition"
        :allies="lockedAllies"
        @preview="previewed = $event"
      />

      <BuildView
        v-else-if="subject"
        :kit-id="kitId"
        :champion-id="subject.championId"
        :position="subject.request.position"
        :draft="{ build, pending: buildPending, error: buildError, opponentId: duel.opponentId, label: 'This draft', request: subject.request }"
        :kits="kits"
        @update:kit-id="pickKit"
      >
        <template #advice>
          <DraftItemAdvice :subject="subject" />
        </template>
        <template #header>
          <DraftBuildHeader
            :champion-id="subject.championId"
            :position="subject.request.position"
            :can-go-back="canGoBack"
            @back="back"
          />
        </template>
      </BuildView>

      <div v-else class="surface flex h-full flex-col items-center justify-center gap-2 rounded-xl text-center">
        <UIcon name="i-lucide-scroll-text" class="size-6 text-dimmed" />
        <p class="text-sm text-muted">The build shows once a champion is picked</p>
      </div>
    </div>
  </div>
</template>
