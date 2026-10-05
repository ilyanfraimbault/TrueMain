<!--
  The dashboard's goals (#1913): each active goal as its sentence, its games
  as slots filling up — met and missed on the data axis (rosegold, ink), still
  to play a hairline, the loading board's bar idiom — and where it stands against
  its target. A goal decided by the latest game stays here with its outcome
  until the next one, then moves under "Past goals". With none running, the
  card offers up to three targets taken from the player's own averages.
-->
<script setup lang="ts">
import type { Goal, GoalDraft, GoalEvaluation, GoalResult } from '~/utils/goals'
import { formatThreshold, goalSentence, meets, metricOf } from '~/utils/goals'

const props = defineProps<{
  goals: Goal[]
  evaluations: Map<string, GoalEvaluation>
  suggestions: GoalDraft[]
  canAdd: boolean
  /** The newest game read: a goal it decided is still shown with the running ones. */
  latestGameId: number | null
}>()

const emit = defineEmits<{ edit: [draft: GoalDraft | null], abandon: [id: string] }>()

const { nameOf } = useChampionStatics()

const sentence = (goal: Goal | GoalDraft) =>
  goalSentence(goal, goal.scope.championId ? nameOf(goal.scope.championId) : undefined)

const justDecided = (goal: Goal) =>
  (goal.status === 'done' || goal.status === 'missed') && goal.results.at(-1)?.gameId === props.latestGameId
const current = computed(() => props.goals.filter(goal => goal.status === 'active' || justDecided(goal)))
const running = computed(() => current.value.some(goal => goal.status === 'active'))
const past = computed(() => props.goals.filter(goal => !current.value.includes(goal)).reverse())

const resultsOf = (goal: Goal) => props.evaluations.get(goal.id)?.results ?? goal.results
const statusOf = (goal: Goal) => props.evaluations.get(goal.id)?.status ?? goal.status
const slotsOf = (goal: Goal) => Array.from({ length: goal.games }, (_, index) => resultsOf(goal)[index] ?? null)

const formatValue = (goal: Goal, value: number) => metricOf(goal.metric)?.format(value) ?? String(value)
const when = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' })
const slotTip = (goal: Goal, result: GoalResult | null, index: number) =>
  result ? `${formatValue(goal, result.value)} · ${when.format(result.playedAt)}` : `Game ${index + 1} to play`

function standing(goal: Goal): string {
  const evaluation = props.evaluations.get(goal.id)
  const counted = evaluation?.counted ?? goal.results.length
  const played = `${counted}/${goal.games} games`
  if (goal.mode === 'eachGame') return `${evaluation?.hits ?? 0} of ${goal.hitsNeeded} met · ${played}`
  const value = evaluation?.value
  return value === null || value === undefined
    ? `Target ${formatThreshold(goal.metric, goal.threshold)} · ${played}`
    : `${formatValue(goal, value)} now · target ${formatThreshold(goal.metric, goal.threshold)} · ${played}`
}

const STATUS = {
  active: { icon: 'i-lucide-circle-dashed', class: 'text-dimmed', label: 'Running' },
  done: { icon: 'i-lucide-circle-check', class: 'text-data-good', label: 'Done' },
  missed: { icon: 'i-lucide-circle-x', class: 'text-data-bad-dim', label: 'Missed' },
  abandoned: { icon: 'i-lucide-circle-minus', class: 'text-dimmed', label: 'Abandoned' },
} as const

const pastDetail = (goal: Goal) =>
  goal.results.length ? goal.results.map(result => formatValue(goal, result.value)).join(' · ') : 'No game counted'

function suggestionLabel(draft: GoalDraft): string {
  const metric = metricOf(draft.metric)
  const verb = draft.comparator === 'atLeast' ? 'Raise' : 'Keep'
  const value = formatThreshold(draft.metric, draft.threshold)
  return draft.comparator === 'atLeast'
    ? `${verb} ${metric?.label ?? draft.metric} to ${value}, your average`
    : `${verb} ${metric?.label ?? draft.metric} at ${value} or less, your average`
}
</script>

<template>
  <section class="surface flex flex-col gap-3 rounded-lg px-4 py-3">
    <div class="flex items-center justify-between">
      <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">Goals</h2>
      <UButton
        v-if="canAdd && running"
        icon="i-lucide-plus"
        color="neutral"
        variant="ghost"
        size="xs"
        aria-label="New goal"
        @click="emit('edit', null)"
      />
    </div>

    <ul v-if="current.length" class="flex flex-col gap-3">
      <li v-for="goal in current" :key="goal.id" class="flex flex-col gap-1.5">
        <div class="flex items-start gap-2">
          <UIcon v-if="statusOf(goal) !== 'active'" :name="STATUS[statusOf(goal)].icon" class="mt-0.5 size-4 shrink-0" :class="STATUS[statusOf(goal)].class" />
          <p class="min-w-0 flex-1 text-sm leading-snug text-highlighted">{{ sentence(goal) }}</p>
          <UDropdownMenu
            v-if="goal.status === 'active'"
            :items="[[{ label: 'Abandon', icon: 'i-lucide-x', onSelect: () => emit('abandon', goal.id) }]]"
            :content="{ align: 'end' }"
          >
            <UButton icon="i-lucide-ellipsis" color="neutral" variant="ghost" size="xs" aria-label="Goal options" class="-my-1" />
          </UDropdownMenu>
        </div>
        <div class="flex h-1.5 gap-1" :aria-label="standing(goal)">
          <UTooltip v-for="(result, index) in slotsOf(goal)" :key="index" :text="slotTip(goal, result, index)" :delay-duration="0">
            <span
              class="flex-1 rounded-full"
              :class="result === null ? 'ring-1 ring-inset ring-default' : meets(goal, result.value) ? 'bg-data-good' : 'bg-data-bad'"
            />
          </UTooltip>
        </div>
        <p class="text-[11px] tabular-nums text-muted">
          <span v-if="statusOf(goal) !== 'active'" :class="STATUS[statusOf(goal)].class">{{ STATUS[statusOf(goal)].label }} · </span>{{ standing(goal) }}
        </p>
      </li>
    </ul>

    <div v-if="!running" class="flex flex-col gap-2">
      <p v-if="!current.length" class="text-xs text-muted">Set a target on one of your stats over your next games.</p>
      <button
        v-for="draft in suggestions"
        :key="draft.metric"
        type="button"
        class="rounded-md px-2.5 py-1.5 text-left text-xs text-default ring-1 ring-default transition-colors hover:bg-accented"
        @click="emit('edit', draft)"
      >
        {{ suggestionLabel(draft) }}
      </button>
      <UButton label="New goal" icon="i-lucide-plus" color="neutral" variant="outline" size="xs" class="self-start" @click="emit('edit', null)" />
    </div>

    <UCollapsible v-if="past.length" class="flex flex-col gap-2">
      <UButton
        :label="`Past goals (${past.length})`"
        trailing-icon="i-lucide-chevron-down"
        color="neutral"
        variant="link"
        size="xs"
        class="-mx-2.5 self-start text-muted"
        :ui="{ trailingIcon: 'group-data-[state=open]:rotate-180 transition-transform' }"
      />
      <template #content>
        <ul class="flex flex-col gap-1.5">
          <li v-for="goal in past" :key="goal.id">
            <UTooltip :text="pastDetail(goal)" :delay-duration="150">
              <p class="flex items-start gap-1.5 text-xs leading-snug text-muted">
                <UIcon :name="STATUS[goal.status].icon" class="mt-px size-3.5 shrink-0" :class="STATUS[goal.status].class" />
                <span>{{ sentence(goal) }}</span>
              </p>
            </UTooltip>
          </li>
        </ul>
      </template>
    </UCollapsible>
  </section>
</template>
