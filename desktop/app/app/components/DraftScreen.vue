<script setup lang="ts">
import type { DraftState } from '~/types/lcu'

const props = defineProps<{ draft: DraftState }>()

const { nameOf } = useChampionStatics()

const draft = toRef(props, 'draft')
const pinnedLanes = ref<Record<number, string>>({})
const { recommendation, pending, error } = useDraftRecommendation(draft, pinnedLanes)

/** Allies are padded so the row keeps its shape as picks land. */
const allySlots = computed<(number | null)[]>(() => {
  const slots: (number | null)[] = [...props.draft.allyChampions]
  while (slots.length < 4) slots.push(null)
  return slots.slice(0, 4)
})

const positionLabel = computed(() => props.draft.myPosition || 'No assigned lane')

const opponent = computed(() => recommendation.value?.laneOpponentChampionId ?? null)

/**
 * The lane opponent is the one answer the panel exists to give, and it rests on
 * a guess. Saying how sure we are is not decoration: the matchup numbers below
 * are only as good as this.
 */
const opponentCaption = computed(() => {
  const answer = recommendation.value
  if (!answer || opponent.value === null) return null
  const pinned = answer.enemyLanes.find(l => l.championId === opponent.value)?.pinned
  if (pinned) return 'You set this'
  const confidence = answer.laneOpponentConfidence
  if (confidence >= 0.85) return 'Likely your lane'
  if (confidence >= 0.6) return 'Uncertain — check the lanes'
  return 'Coin flip — check the lanes'
})
</script>

<template>
  <div class="flex h-full flex-col gap-5 overflow-y-auto p-6">
    <header class="flex items-center justify-between gap-4">
      <div class="flex items-center gap-3">
        <ChampionPortrait :champion-id="draft.myChampion" size="lg" />
        <div>
          <p class="text-xs uppercase tracking-wide text-dimmed">{{ positionLabel }}</p>
          <h1 class="text-lg font-semibold text-highlighted">
            {{ draft.myChampion ? nameOf(draft.myChampion) : 'Pick a champion' }}
          </h1>
          <p v-if="draft.myChampion && !draft.myChampionLocked" class="text-xs text-muted">
            Hovering — not locked in
          </p>
        </div>
      </div>
      <div v-if="draft.secondsLeft > 0" class="text-right">
        <p class="text-xs uppercase tracking-wide text-dimmed">Time left</p>
        <p class="text-2xl font-semibold tabular-nums text-highlighted">{{ draft.secondsLeft }}s</p>
      </div>
    </header>

    <section v-if="opponent !== null" class="flex items-center gap-3 rounded-md bg-elevated p-3">
      <ChampionPortrait :champion-id="opponent" />
      <div class="min-w-0">
        <p class="text-xs uppercase tracking-wide text-dimmed">Your lane opponent</p>
        <p class="truncate text-sm font-medium text-highlighted">{{ nameOf(opponent) }}</p>
        <p class="text-[11px] text-muted">{{ opponentCaption }}</p>
      </div>
      <UIcon v-if="pending" name="i-lucide-loader-circle" class="ml-auto size-4 animate-spin text-dimmed" />
    </section>

    <LaneAssignmentPanel
      v-if="recommendation"
      :lanes="recommendation.enemyLanes"
      :my-position="draft.myPosition"
      @update:pinned="pinnedLanes = $event"
    />

    <!--
      No answer yet is the normal early-draft state — nobody has picked. It is
      not an error and must not read as one.
    -->
    <section v-else class="space-y-2">
      <h2 class="text-xs font-medium uppercase tracking-wide text-dimmed">Enemy team</h2>
      <div class="flex gap-2">
        <ChampionPortrait
          v-for="index in 5"
          :key="`enemy-${index}`"
          :champion-id="draft.enemyChampions[index - 1] ?? null"
        />
      </div>
    </section>

    <UAlert
      v-if="error"
      icon="i-lucide-wifi-off"
      color="neutral"
      variant="subtle"
      title="Lane assignment unavailable"
      :description="error"
    />

    <section class="space-y-2">
      <h2 class="text-xs font-medium uppercase tracking-wide text-dimmed">Your team</h2>
      <div class="flex gap-2">
        <ChampionPortrait
          v-for="(champion, index) in allySlots"
          :key="`ally-${index}`"
          :champion-id="champion"
          size="sm"
        />
      </div>
    </section>

    <section v-if="draft.bans.length" class="space-y-2">
      <h2 class="text-xs font-medium uppercase tracking-wide text-dimmed">Bans</h2>
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
</template>
