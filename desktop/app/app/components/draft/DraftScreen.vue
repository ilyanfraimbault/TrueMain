<script setup lang="ts">
import type { DraftPool, Lane, TeamRow } from '~/types/draft'
import type { DraftState } from '~/types/lcu'
import type { ViewedPick } from '~/composables/useDraftSubject'
import { LANES, LANE_LABELS } from '~/types/draft'

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
  /** The slot the simulator is filling: our lane, or the enemy's next pick. */
  activeSlot?: { team: 'ally' | 'enemy', lane?: Lane | null } | null
}>(), { label: 'Champion select', activeSlot: null })

const { nameOf } = useChampionStatics()
const { laneEntries } = useTierList()

const draft = toRef(props, 'draft')

// ─── The pool, and the answer for it ────────────────────────────────────────

const poolKind = ref<DraftPool>('meta')

/** Champions nobody can pick any more: banned, or locked on either side. */
const unavailable = computed(() => new Set([
  ...props.draft.allyBans,
  ...props.draft.enemyBans,
  ...props.draft.enemyChampions,
  ...props.draft.myTeam.filter(slot => slot.locked && !slot.isMe && slot.championId !== null).map(slot => slot.championId!),
]))

/** The lane's tier list, best first: the forty the endpoint scores at most, or the forty after them. */
const candidates = computed(() => {
  if (!props.draft.myPosition) return []
  const lane = laneEntries(props.draft.myPosition).filter(entry => !unavailable.value.has(entry.championId))
  return (poolKind.value === 'meta' ? lane.slice(0, 40) : lane.slice(40, 80)).map(entry => entry.championId)
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

const activeAllyCell = computed(() => {
  const slot = props.activeSlot
  return slot?.team === 'ally' ? allyRows.value.findIndex(row => row.lane === slot.lane) : null
})
const activeEnemyCell = computed(() => (props.activeSlot?.team === 'enemy' ? enemyRows.value.findIndex(row => row.championId === null) : null))

// ─── Whose build, and which half of the screen ──────────────────────────────

const viewed = ref<ViewedPick | null>(null)
const previewed = ref<number | null>(null)
const { subject, duel, whose } = useDraftSubject(draft, enemyLanes, viewed, previewed)
const { build, pending: buildPending, error: buildError } = useDraftBuild(subject)

/** A click reads that champion's build; a second click on it goes back. */
function view(team: 'ally' | 'enemy', championId: number) {
  const isMe = team === 'ally' && championId === props.draft.myChampion
  if (isMe) {
    // Our own card toggles our build while the pick is open; once it is
    // locked the build is already the default, and the click only comes back to it.
    const onMine = mode.value === 'build' && viewed.value === null && previewed.value === null
    viewed.value = null
    previewed.value = null
    showBuild.value = !props.draft.myChampionLocked && !onMine
    return
  }
  viewed.value = viewed.value?.championId === championId ? null : { team, championId }
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
  if (viewed.value || previewed.value !== null || showBuild.value) return 'build'
  return !props.draft.myChampionLocked && byLane.value ? 'pick' : 'build'
})

const canGoBack = computed(() => viewed.value !== null || previewed.value !== null || showBuild.value)
function back() {
  viewed.value = null
  previewed.value = null
  showBuild.value = false
}

const suggested = computed(() => (recommendation.value?.candidates ?? []).filter(candidate => !candidate.thinSample).map(candidate => candidate.championId))
const opponentId = computed(() => {
  const id = recommendation.value?.laneOpponentChampionId ?? null
  return id !== null && props.draft.enemyChampions.includes(id) ? id : null
})
</script>

<template>
  <div class="flex h-full flex-col gap-3 px-4 pb-4 pt-3">
    <DraftTopStrip :ally-bans="draft.allyBans" :enemy-bans="draft.enemyBans" :seconds-left="draft.secondsLeft" :label="label" />

    <div class="grid grid-cols-[minmax(0,1fr)_8.5rem_minmax(0,1fr)] gap-3">
      <DraftTeam
        team="ally"
        :rows="allyRows"
        :viewed-id="viewed?.team === 'ally' ? viewed.championId : null"
        :active-cell="activeAllyCell"
        :suggested="suggested"
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

      <div class="relative">
        <DraftTeam
          team="enemy"
          :rows="enemyRows"
          :opponent-id="opponentId"
          :viewed-id="viewed?.team === 'enemy' ? viewed.championId : null"
          :active-cell="activeEnemyCell"
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
    </div>

    <div class="min-h-0 flex-1">
      <DraftSuggestions
        v-if="mode === 'pick'"
        v-model:pool-kind="poolKind"
        :pool="candidates"
        :candidates="recommendation?.candidates ?? []"
        :pending="ranking"
        :error="rankError"
        :position="draft.myPosition"
        @preview="previewed = $event"
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
