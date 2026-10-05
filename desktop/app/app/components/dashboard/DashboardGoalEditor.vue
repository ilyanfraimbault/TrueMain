<!--
  Sets a goal (#1913): metric, comparator, threshold, how many games, how they
  are read (the average over them, or game by game), and where (queue,
  optionally a champion and a lane). The threshold starts at the player's own
  average on that metric in that scope, and the goal is written out in full
  before it is set.
-->
<script setup lang="ts">
import type { GoalDraft, GoalMode } from '~/utils/goals'
import type { PlayerGame } from '~/types/record'
import type { QueueFilter } from '~/utils/player-form'
import { LANES, LANE_LABELS } from '~/types/draft'
import { MAX_GOAL_GAMES, defaultComparatorOf, defaultModeOf, draftGoal, goalSentence, inScope, isPercent, roundToStep, stepOf } from '~/utils/goals'
import { METRICS, QUEUE_FILTERS, championLines, counted } from '~/utils/player-form'

const props = defineProps<{
  /** Newest first: what the threshold's starting value is measured on. */
  games: PlayerGame[]
  /** The suggestion it opens on, or null for a blank goal. */
  initial: GoalDraft | null
}>()

const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ save: [draft: GoalDraft] }>()

const { nameOf } = useChampionStatics()

const ANY = 0
const ANY_LANE = 'ANY'

const draft = ref<GoalDraft>(draftGoal('cs', 0))
const champion = ref(ANY)
const lane = ref<string>(ANY_LANE)

const scoped = computed(() => ({
  ...draft.value.scope,
  championId: champion.value || null,
  position: lane.value === ANY_LANE ? null : lane.value,
}))

/** The player's average on the metric over the games in scope, at the metric's precision. */
function averageInScope(metricKey: string): number | null {
  const metric = METRICS.find(entry => entry.key === metricKey)
  const games = counted(props.games.filter(game => inScope(game, scoped.value)))
  const value = metric?.over(games) ?? null
  return value === null ? null : roundToStep(metricKey, value)
}

function prefill() {
  draft.value.threshold = averageInScope(draft.value.metric) ?? draft.value.threshold
}

watch(open, (isOpen) => {
  if (!isOpen) return
  const start = props.initial ?? draftGoal('cs', 0)
  draft.value = JSON.parse(JSON.stringify(start))
  champion.value = start.scope.championId ?? ANY
  lane.value = start.scope.position ?? ANY_LANE
  if (!props.initial) prefill()
})

function setMetric(key: string) {
  draft.value.metric = key
  draft.value.comparator = defaultComparatorOf(key)
  draft.value.mode = defaultModeOf(key)
  prefill()
}

watch([() => draft.value.scope.queue, champion, lane], () => {
  if (open.value) prefill()
})
// K follows N while it asks for every game, and never exceeds it.
watch(() => draft.value.games, (games, previous) => {
  draft.value.hitsNeeded = draft.value.hitsNeeded === previous ? games : Math.min(draft.value.hitsNeeded, games)
})

/** The threshold as typed: percentages are entered as whole numbers. */
const shownThreshold = computed({
  get: () => (isPercent(draft.value.metric) ? Math.round(draft.value.threshold * 100) : draft.value.threshold),
  set: (value: number | null) => {
    const typed = value ?? 0
    draft.value.threshold = isPercent(draft.value.metric) ? typed / 100 : typed
  },
})
const thresholdStep = computed(() => (isPercent(draft.value.metric) ? 1 : stepOf(draft.value.metric)))

const metricItems = METRICS.map(metric => ({ label: metric.label, value: metric.key }))
const comparatorItems = [{ label: 'At least', value: 'atLeast' }, { label: 'At most', value: 'atMost' }]
const modeItems: { label: string, value: GoalMode }[] = [
  { label: 'Average', value: 'average' },
  { label: 'Each game', value: 'eachGame' },
]
const queueItems = QUEUE_FILTERS.map(option => ({ label: option.label, value: option.value as QueueFilter }))
const championItems = computed(() => [
  { label: 'Any champion', value: ANY },
  ...championLines(props.games).map(line => ({ label: nameOf(line.championId), value: line.championId })),
])
const laneItems = [{ label: 'Any lane', value: ANY_LANE }, ...LANES.map(entry => ({ label: LANE_LABELS[entry], value: entry as string }))]

const result = computed<GoalDraft>(() => ({ ...draft.value, scope: scoped.value }))
const preview = computed(() => goalSentence(result.value, champion.value ? nameOf(champion.value) : undefined))
const average = computed(() => averageInScope(draft.value.metric))

function save() {
  emit('save', JSON.parse(JSON.stringify(result.value)))
  open.value = false
}
</script>

<template>
  <UModal v-model:open="open" title="New goal" description="Measured on your next games, from your client. Kept on this computer." :ui="{ content: 'max-w-md', footer: 'justify-end' }">
    <template #body>
      <div class="grid grid-cols-2 gap-3">
        <UFormField label="Stat" class="col-span-2">
          <USelect :model-value="draft.metric" :items="metricItems" class="w-full" @update:model-value="setMetric($event as string)" />
        </UFormField>
        <UFormField label="Target">
          <USelect v-model="draft.comparator" :items="comparatorItems" class="w-full" />
        </UFormField>
        <UFormField :label="isPercent(draft.metric) ? 'Value (%)' : 'Value'" :hint="average === null ? undefined : `Avg ${isPercent(draft.metric) ? Math.round(average * 100) : average}`">
          <UInputNumber v-model="shownThreshold" :min="0" :step="thresholdStep" :format-options="{ maximumFractionDigits: 2 }" class="w-full" />
        </UFormField>
        <UFormField label="Next games">
          <UInputNumber v-model="draft.games" :min="1" :max="MAX_GOAL_GAMES" class="w-full" />
        </UFormField>
        <UFormField label="Read as">
          <USelect v-model="draft.mode" :items="modeItems" class="w-full" />
        </UFormField>
        <UFormField v-if="draft.mode === 'eachGame'" label="Games that must meet it" class="col-span-2">
          <UInputNumber v-model="draft.hitsNeeded" :min="1" :max="draft.games" class="w-full" />
        </UFormField>
        <UFormField label="Queue" class="col-span-2">
          <USelect v-model="draft.scope.queue" :items="queueItems" class="w-full" />
        </UFormField>
        <UFormField label="Champion">
          <USelect v-model="champion" :items="championItems" class="w-full" />
        </UFormField>
        <UFormField label="Lane">
          <USelect v-model="lane" :items="laneItems" class="w-full" />
        </UFormField>
      </div>
      <p class="mt-4 rounded-md bg-elevated px-3 py-2 text-sm text-highlighted ring-1 ring-default">{{ preview }}</p>
    </template>
    <template #footer>
      <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
      <UButton label="Set goal" icon="i-lucide-target" @click="save" />
    </template>
  </UModal>
</template>
