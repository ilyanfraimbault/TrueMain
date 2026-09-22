<script setup lang="ts">
import type { DraftState } from '~/types/lcu'
import { backdropAlias } from '~/composables/useChampionStatics'

const props = defineProps<{ draft: DraftState }>()

/** Before a pick there is no champion to draw, and a bare band reads as broken. */
const backdrop = backdropAlias()

const { nameOf } = useChampionStatics()

const draft = toRef(props, 'draft')
const pinnedLanes = ref<Record<number, string>>({})
const { recommendation, pending, error } = useDraftRecommendation(draft, pinnedLanes)

/** Both rows are padded so the strip keeps its shape as picks land. */
function padded(champions: number[], length: number): (number | null)[] {
  const slots: (number | null)[] = [...champions]
  while (slots.length < length) slots.push(null)
  return slots.slice(0, length)
}
const allySlots = computed(() => padded(props.draft.allyChampions, 4))
const enemySlots = computed(() => padded(props.draft.enemyChampions, 5))

const positionLabel = computed(() => props.draft.myPosition || 'No assigned lane')

const opponent = computed(() => recommendation.value?.laneOpponentChampionId ?? null)

/**
 * The lane opponent is the one answer this screen exists to give, and it rests
 * on a guess. Saying how sure we are is not decoration: the matchup numbers
 * are only as good as this.
 */
const opponentCaption = computed(() => {
  const answer = recommendation.value
  if (!answer || opponent.value === null) return null
  const pinned = answer.enemyLanes.find(l => l.championId === opponent.value)?.pinned
  if (pinned) return 'you set this'
  const confidence = answer.laneOpponentConfidence
  if (confidence >= 0.85) return 'likely'
  if (confidence >= 0.6) return 'uncertain — check the lanes'
  return 'coin flip — check the lanes'
})

/** The last ten seconds are when a pick is decided; the timer says so. */
const urgent = computed(() => props.draft.secondsLeft > 0 && props.draft.secondsLeft <= 10)
</script>

<template>
  <div class="flex h-full flex-col overflow-y-auto">
    <header class="relative h-40 shrink-0 overflow-hidden">
      <ChampionArt v-if="draft.myChampion" :champion-id="draft.myChampion" fade="x" />
      <ChampionArt v-else :alias="backdrop" fade="x" class="opacity-50" />
      <div class="relative flex h-full items-end justify-between gap-6 p-6">
        <div class="flex items-end gap-4">
          <ChampionPortrait :champion-id="draft.myChampion" size="lg" class="ring-2 ring-primary/70" />
          <div class="min-w-0 pb-0.5">
            <p class="text-[11px] font-medium uppercase tracking-[0.14em] text-dimmed">{{ positionLabel }}</p>
            <h1 class="mt-1 text-3xl font-semibold tracking-tight text-highlighted">
              {{ draft.myChampion ? nameOf(draft.myChampion) : 'Pick a champion' }}
            </h1>
            <p class="mt-1 flex items-center gap-2 text-sm">
              <template v-if="opponent !== null">
                <span class="text-muted">vs</span>
                <span class="font-medium text-default">{{ nameOf(opponent) }}</span>
                <span class="text-dimmed">· {{ opponentCaption }}</span>
              </template>
              <span v-else-if="draft.myChampion && !draft.myChampionLocked" class="text-muted">
                Hovering — not locked in
              </span>
              <span v-else-if="draft.myChampion" class="text-muted">Locked in</span>
              <UIcon v-if="pending" name="i-lucide-loader-circle" class="size-3.5 animate-spin text-dimmed" />
            </p>
          </div>
        </div>

        <div v-if="draft.secondsLeft > 0" class="shrink-0 text-right">
          <p class="text-[11px] font-medium uppercase tracking-[0.14em] text-dimmed">Time left</p>
          <p
            class="text-5xl font-semibold leading-none tabular-nums tracking-tight transition-colors"
            :class="urgent ? 'text-primary' : 'text-highlighted'"
          >
            {{ draft.secondsLeft }}
          </p>
        </div>
      </div>
    </header>

    <div class="flex flex-1 flex-col gap-6 p-6 pt-4">
      <!-- The two compositions, face to face. -->
      <section class="surface flex items-center justify-between gap-4 rounded-xl px-4 py-3">
        <div class="flex items-center gap-2">
          <ChampionPortrait :champion-id="draft.myChampion" class="ring-2 ring-primary" />
          <ChampionPortrait v-for="(champion, index) in allySlots" :key="`ally-${index}`" :champion-id="champion" />
        </div>
        <span class="text-[11px] font-bold uppercase tracking-[0.2em] text-dimmed">vs</span>
        <div class="flex items-center gap-2">
          <ChampionPortrait
            v-for="(champion, index) in enemySlots"
            :key="`enemy-${index}`"
            :champion-id="champion"
            :class="champion !== null && champion === opponent && 'ring-2 ring-gold'"
          />
        </div>
      </section>

      <LaneAssignmentPanel
        v-if="recommendation"
        :lanes="recommendation.enemyLanes"
        :my-position="draft.myPosition"
        @update:pinned="pinnedLanes = $event"
      />

      <!--
        No answer yet is the normal early-draft state — nobody has picked, or the
        queue assigns no lanes. It is not an error and must not read as one: the
        enemy picks are shown in the order they landed.
      -->
      <section v-else class="space-y-3">
        <header class="flex items-baseline gap-3">
          <h2 class="text-[11px] font-medium uppercase tracking-[0.14em] text-dimmed">Enemy team</h2>
          <p class="text-xs text-muted">
            {{ draft.myPosition ? 'Lanes resolve as picks land.' : 'This queue assigns no lanes, so there is nothing to resolve.' }}
          </p>
        </header>
        <ul class="grid grid-cols-5 gap-3">
          <li v-for="(champion, index) in enemySlots" :key="`enemy-card-${index}`">
            <ChampionCard :champion-id="champion" :name="champion ? nameOf(champion) : 'Not picked'" />
          </li>
        </ul>
      </section>

      <UAlert
        v-if="error"
        icon="i-lucide-wifi-off"
        color="neutral"
        variant="subtle"
        title="Lane assignment unavailable"
        :description="error"
      />

      <section v-if="draft.bans.length" class="flex items-center gap-3">
        <h2 class="text-[11px] font-medium uppercase tracking-[0.14em] text-dimmed">Bans</h2>
        <div class="flex flex-wrap gap-1.5">
          <ChampionPortrait
            v-for="champion in draft.bans"
            :key="`ban-${champion}`"
            :champion-id="champion"
            size="sm"
            dimmed
          />
        </div>
      </section>
    </div>
  </div>
</template>
