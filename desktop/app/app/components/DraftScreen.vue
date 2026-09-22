<script setup lang="ts">
import type { DraftState } from '~/types/lcu'

const props = defineProps<{ draft: DraftState }>()

const { nameOf } = useChampionStatics()

/**
 * The enemy side is padded to five so the panel keeps its shape as picks land.
 * A row that grows from one slot to five reflows under the reader's eyes during
 * the one phase where they have no time to re-find anything.
 */
const enemySlots = computed<(number | null)[]>(() => {
  const slots: (number | null)[] = [...props.draft.enemyChampions]
  while (slots.length < 5) slots.push(null)
  return slots.slice(0, 5)
})

const allySlots = computed<(number | null)[]>(() => {
  const slots: (number | null)[] = [...props.draft.allyChampions]
  while (slots.length < 4) slots.push(null)
  return slots.slice(0, 4)
})

const positionLabel = computed(() => props.draft.myPosition || 'No assigned lane')
</script>

<template>
  <div class="flex h-full flex-col gap-5 p-6">
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

    <section class="space-y-2">
      <h2 class="text-xs font-medium uppercase tracking-wide text-dimmed">Enemy team</h2>
      <div class="flex gap-2">
        <ChampionPortrait v-for="(champion, index) in enemySlots" :key="`enemy-${index}`" :champion-id="champion" />
      </div>
      <!--
        The lane opponent is a guess: champion select does not tell us which
        enemy holds which lane. Until the guesser and its editable assignment
        panel land, the panel says so rather than naming one.
      -->
      <p class="text-xs text-muted">
        Lane assignment is not resolved yet — champion select does not expose enemy roles.
      </p>
    </section>

    <section class="space-y-2">
      <h2 class="text-xs font-medium uppercase tracking-wide text-dimmed">Your team</h2>
      <div class="flex gap-2">
        <ChampionPortrait v-for="(champion, index) in allySlots" :key="`ally-${index}`" :champion-id="champion" size="sm" />
      </div>
    </section>

    <section v-if="draft.bans.length" class="space-y-2">
      <h2 class="text-xs font-medium uppercase tracking-wide text-dimmed">Bans</h2>
      <div class="flex flex-wrap gap-1.5">
        <ChampionPortrait v-for="champion in draft.bans" :key="`ban-${champion}`" :champion-id="champion" size="sm" dimmed />
      </div>
    </section>
  </div>
</template>
