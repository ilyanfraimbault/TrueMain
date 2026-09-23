<script setup lang="ts">
import type { DraftState } from '~/types/lcu'
import type { CompositionSlot } from '~/types/build'
import type { Lane } from '~/types/draft'
import type { BuildSubject } from '~/composables/useDraftBuild'
import { LANES, LANE_LABELS } from '~/types/draft'
import { backdropAlias } from '~/composables/useChampionStatics'

const props = defineProps<{ draft: DraftState }>()

const { nameOf } = useChampionStatics()

/** Before a pick there is no champion to set the mood, so the day's backdrop does. */
const backdrop = backdropAlias()

const draft = toRef(props, 'draft')
const pinnedLanes = ref<Record<number, string>>({})
const { recommendation, error } = useDraftRecommendation(draft, pinnedLanes)

const enemyLanes = computed(() => recommendation.value?.enemyLanes ?? [])
const { slots, pinned, selected, hasCorrections, swap, reset } = useLaneAssignment(enemyLanes)
watch(pinned, value => (pinnedLanes.value = value), { deep: true })

/** Rows follow the lanes when the queue assigns them, so row n is a matchup. */
const byLane = computed(() => props.draft.myPosition !== '')

const allyRows = computed(() => {
  const team = props.draft.myTeam
  if (!byLane.value) return Array.from({ length: 5 }, (_, index) => team[index] ?? null)
  return LANES.map(lane => team.find(slot => slot.position === lane) ?? null)
})

/**
 * With a lane answer the enemy column follows the lanes and can be corrected
 * by drag; without one — early draft, or a queue with no lanes — the enemies
 * are listed in the order they picked.
 */
const enemyRows = computed(() => {
  if (recommendation.value) return slots.value
  // Nobody picked yet: every lane is simply open, so the rows can carry them.
  // Once a pick lands its lane is a guess, and only the answer may place it.
  const open = byLane.value && props.draft.enemyChampions.length === 0
  return Array.from({ length: 5 }, (_, index) => ({
    lane: (open ? LANES[index] ?? null : null) as Lane | null,
    championId: props.draft.enemyChampions[index] ?? null,
    confidence: 0,
    pinned: false,
  }))
})

const opponent = computed(() => recommendation.value?.laneOpponentChampionId ?? null)

// ─── Whose build is on screen ───────────────────────────────────────────────

/** A champion clicked to read its build; null is us. */
const viewed = ref<{ team: 'ally' | 'enemy', championId: number } | null>(null)

/** Locked picks of our side, on their lanes. */
const allyPicks = computed<CompositionSlot[]>(() => props.draft.myTeam
  .filter(slot => slot.locked && slot.championId !== null && slot.position)
  .map(slot => ({ championId: slot.championId!, position: slot.position })))

/** Their picks, on the lanes the guesser (and the user's corrections) put them. */
const enemyPicks = computed<CompositionSlot[]>(() => enemyLanes.value
  .filter(lane => props.draft.enemyChampions.includes(lane.championId))
  .map(lane => ({ championId: lane.championId, position: lane.position })))

/**
 * The build request for whoever is viewed, from that champion's side of the
 * draft: its team as allies, the other as enemies. Us by default — our pick
 * counts even while hovered, since that is the build being weighed. A viewed
 * champion that has left the draft (a lane re-solved, a pick changed) falls
 * back to us rather than to nothing.
 */
const subject = computed<BuildSubject | null>(() => {
  const target = viewed.value
  if (target) {
    const own = target.team === 'ally' ? allyPicks.value : enemyPicks.value
    const other = target.team === 'ally' ? enemyPicks.value : allyPicks.value
    const self = own.find(pick => pick.championId === target.championId)
    if (self) {
      return {
        championId: self.championId,
        request: {
          position: self.position,
          allies: own.filter(pick => pick.championId !== self.championId),
          enemies: other,
        },
      }
    }
  }
  const me = props.draft.myChampion
  if (me === null || !props.draft.myPosition) return null
  return {
    championId: me,
    request: {
      position: props.draft.myPosition,
      allies: allyPicks.value.filter(pick => pick.position !== props.draft.myPosition),
      enemies: enemyPicks.value,
    },
  }
})

/** The champion whose build is on screen. */
const shown = computed(() => subject.value?.championId ?? null)
const viewingOther = computed(() => shown.value !== null && shown.value !== props.draft.myChampion)

/** A click reads that champion's build; a second click on it goes back to ours. */
function view(team: 'ally' | 'enemy', championId: number | null) {
  if (championId === null) return
  const isMe = team === 'ally' && championId === props.draft.myChampion
  viewed.value = isMe || shown.value === championId ? null : { team, championId }
}

const dragging = ref<Lane | null>(null)

function onDrop(lane: Lane | null) {
  if (dragging.value && lane) swap(dragging.value, lane)
  dragging.value = null
}
</script>

<template>
  <div class="relative h-full overflow-hidden">
    <!--
      The backdrop: the shown champion's splash, softened, with each side's
      colour bleeding in from its edge. It is what gives the glass panels
      something to sit on.
    -->
    <ChampionArt v-if="shown" :champion-id="shown" fade="none" position="60% 20%" class="scale-105 opacity-60 blur-[2px]" />
    <ChampionArt v-else :alias="backdrop" fade="none" position="60% 20%" class="scale-105 opacity-25 blur-[3px]" />
    <div class="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_45%_70%_at_0%_60%,color-mix(in_oklch,var(--color-ally)_16%,transparent),transparent),radial-gradient(ellipse_45%_70%_at_100%_60%,color-mix(in_oklch,var(--color-enemy)_14%,transparent),transparent),linear-gradient(to_bottom,color-mix(in_oklch,var(--ui-bg)_20%,transparent),var(--ui-bg)_90%)]" />

    <div class="relative grid h-full grid-cols-[clamp(11rem,18vw,19rem)_minmax(0,1fr)_clamp(11rem,18vw,19rem)] gap-4 p-4">
      <!-- Our team: bans on top, then the lanes the client assigned. -->
      <section class="glass relative flex flex-col gap-2.5 self-start rounded-2xl px-4 py-3">
        <span class="absolute inset-x-0 top-0 h-0.5 rounded-t-2xl bg-gradient-to-r from-ally to-ally/0" />
        <DraftBans :bans="draft.allyBans" class="mb-1" />
        <button
          v-for="(slot, index) in allyRows"
          :key="`ally-${index}`"
          type="button"
          class="block aspect-[2.4/1] w-full rounded-xl outline-none focus-visible:ring-2 focus-visible:ring-primary"
          :disabled="!slot?.championId"
          :aria-label="slot?.championId ? `Build for ${nameOf(slot.championId)}` : undefined"
          @click="view('ally', slot?.championId ?? null)"
        >
          <PickRow
            team="ally"
            :champion-id="slot?.championId ?? null"
            :lane="slot?.position || (byLane ? LANES[index] : null)"
            :primary="slot?.isMe"
            :viewed="viewingOther && slot?.championId === shown"
            :tentative="slot !== null && slot.championId !== null && !slot.locked"
          />
        </button>
      </section>

      <BuildPanel
        :subject="subject"
        :viewing-other="viewingOther"
        :seconds-left="draft.secondsLeft"
        @back="viewed = null"
      />

      <!-- Theirs: the lanes are a guess, corrected by dragging one onto another. -->
      <section class="glass relative flex flex-col gap-2.5 self-start rounded-2xl px-4 py-3">
        <span class="absolute inset-x-0 top-0 h-0.5 rounded-t-2xl bg-gradient-to-l from-enemy to-enemy/0" />
        <DraftBans :bans="draft.enemyBans" mirrored class="mb-1" />
        <div
          v-for="(slot, index) in enemyRows"
          :key="`enemy-${index}`"
          class="aspect-[2.4/1] w-full"
          @dragover.prevent
          @drop.prevent="onDrop(slot.lane)"
        >
          <button
            type="button"
            class="block size-full rounded-xl outline-none focus-visible:ring-2 focus-visible:ring-primary"
            :disabled="slot.championId === null"
            :draggable="recommendation !== null && slot.championId !== null"
            :aria-label="slot.championId ? `Build for ${nameOf(slot.championId)}${slot.lane ? `, ${LANE_LABELS[slot.lane]}` : ''}` : undefined"
            @click="view('enemy', slot.championId)"
            @dragstart="dragging = slot.lane"
            @dragend="dragging = null"
          >
            <PickRow
              team="enemy"
              :champion-id="slot.championId"
              :lane="slot.lane"
              :gold="slot.championId !== null && slot.championId === opponent"
              :viewed="viewingOther && slot.championId === shown"
              :selected="slot.lane !== null && selected === slot.lane"
              :drop-target="dragging !== null && slot.lane !== null && dragging !== slot.lane"
            >
              <!-- A lane we are not sure of says so on the card. -->
              <span
                v-if="slot.pinned || (recommendation && slot.confidence < 0.85)"
                class="absolute left-1.5 top-1.5 flex size-4 items-center justify-center rounded-full bg-ink-950/85 text-[10px] font-bold"
                :class="slot.pinned ? 'text-primary' : 'text-gold'"
                :title="slot.pinned ? 'Set by you' : 'Uncertain lane'"
              >
                <UIcon v-if="slot.pinned" name="i-lucide-pin" class="size-2.5" />
                <template v-else>?</template>
              </span>
            </PickRow>
          </button>
        </div>
        <div v-if="error || hasCorrections" class="flex shrink-0 items-center justify-end gap-1">
          <UIcon v-if="error" name="i-lucide-wifi-off" class="size-4 text-dimmed" :title="error" />
          <UButton
            v-if="hasCorrections"
            size="xs"
            variant="ghost"
            color="neutral"
            icon="i-lucide-rotate-ccw"
            aria-label="Reset lanes"
            @click="reset"
          />
        </div>
      </section>
    </div>
  </div>
</template>
