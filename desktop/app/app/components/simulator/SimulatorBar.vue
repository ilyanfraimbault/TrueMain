<script setup lang="ts">
import type { InputMenuItem } from '@nuxt/ui'
import type { Lane } from '~/types/draft'
import type { Side } from '~/composables/useDraftSimulator'
import { LANES, LANE_LABELS } from '~/types/draft'

/**
 * The simulator's controls, over the draft it drives: which side and lane we
 * play, the champion for the step the draft is on, and the moves a real
 * champion select allows — hover then lock our pick, pass a ban — plus the ones
 * only a rehearsal needs: take a step back, play forward, start over.
 */
const { sim, steps, current, done, isMyTurn, taken, stepLane, choose, lockIn, skip, undo, reset, autofill } = useDraftSimulator()
const { champions, portraitOf } = useChampionStatics()
const { laneEntries } = useTierList()

/** Our pick lands as a hover first, like in the client, until it is locked in. */
const hoverFirst = ref(true)

const sides = [{ label: 'Blue side', value: 'blue' }, { label: 'Red side', value: 'red' }]
const lanes = LANES.map(lane => ({ label: LANE_LABELS[lane], value: lane }))
const cells = [0, 1, 2, 3, 4].map(cell => ({ label: `Pick ${cell + 1}`, value: cell }))

const side = computed({ get: () => sim.value.side, set: (value: Side) => reset({ side: value }) })
const myLane = computed({ get: () => sim.value.myLane, set: (value: Lane) => reset({ myLane: value }) })
const myCell = computed({ get: () => sim.value.myCell, set: (value: number) => reset({ myCell: value }) })

const open = (id: number) => !taken.value.has(id)
const item = (id: number, name: string): InputMenuItem => ({ label: name, value: id, avatar: { src: portraitOf(id) ?? undefined, alt: name } })

/**
 * Every champion still available — and, for one of our picks, the lane's meta
 * first. An enemy pick is not grouped: the draft does not know its lane.
 */
const items = computed<InputMenuItem[] | InputMenuItem[][]>(() => {
  const all = [...champions.value.values()]
    .filter(champion => open(champion.id))
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(champion => item(champion.id, champion.name))
  const lane = stepLane.value
  if (!lane || current.value?.team !== 'ally') return all
  const meta = laneEntries(lane)
    .filter(entry => open(entry.championId) && champions.value.has(entry.championId))
    .slice(0, 12)
    .map(entry => item(entry.championId, champions.value.get(entry.championId)!.name))
  return [
    [{ type: 'label', label: `Meta · ${LANE_LABELS[lane]}` }, ...meta],
    [{ type: 'label', label: 'All champions' }, ...all],
  ]
})

/** The field only takes a name: once chosen, the champion is on the board and the field empties. */
const term = ref('')
function pick(id: unknown) {
  if (typeof id !== 'number') return
  choose(id, !(isMyTurn.value && hoverFirst.value))
  term.value = ''
}

const hovering = computed(() => isMyTurn.value && current.value !== null && sim.value.ally[current.value.cell]?.championId != null && !sim.value.ally[current.value.cell]?.locked)
const placeholder = computed(() => {
  if (done.value) return 'Draft complete'
  if (current.value?.kind === 'ban') return 'Ban a champion…'
  return isMyTurn.value ? 'Your champion…' : 'Pick a champion…'
})
</script>

<template>
  <div class="flex items-center gap-2 bg-default/40 px-4 py-2.5">
    <USelect v-model="side" :items="sides" size="sm" class="w-28" aria-label="Side" />
    <USelect v-model="myLane" :items="lanes" size="sm" class="w-28" aria-label="Your lane" />
    <USelect v-model="myCell" :items="cells" size="sm" class="w-24" aria-label="Your pick order" />

    <USeparator orientation="vertical" class="mx-1 h-6" />

    <UInputMenu
      v-model:search-term="term"
      :model-value="undefined"
      :items="items"
      value-key="value"
      :placeholder="placeholder"
      :disabled="done"
      icon="i-lucide-search"
      size="sm"
      class="w-56"
      autofocus
      @update:model-value="pick"
    />
    <UTooltip v-if="isMyTurn" text="Our pick lands as a hover first, like in the client">
      <USwitch v-model="hoverFirst" label="Hover" size="sm" :ui="{ label: 'text-xs text-muted' }" />
    </UTooltip>
    <UButton v-if="hovering" label="Lock in" icon="i-lucide-lock" size="sm" @click="lockIn" />
    <UButton v-if="current?.kind === 'ban'" label="No ban" color="neutral" variant="ghost" size="sm" @click="skip" />

    <span class="ml-auto stat-label tabular-nums">Step {{ Math.min(sim.step + 1, steps.length) }}/{{ steps.length }}</span>
    <UButton icon="i-lucide-undo-2" color="neutral" variant="ghost" size="sm" aria-label="Undo" :disabled="sim.step === 0 && !hovering" @click="undo" />
    <UButton
      :label="isMyTurn ? 'Fill to end' : 'Auto-fill'"
      icon="i-lucide-fast-forward"
      color="neutral"
      variant="subtle"
      size="sm"
      :disabled="done"
      @click="autofill"
    />
    <UButton icon="i-lucide-rotate-ccw" color="neutral" variant="ghost" size="sm" aria-label="Start over" @click="reset()" />
  </div>
</template>
