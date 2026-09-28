<script setup lang="ts">
/**
 * The strip over the draft: each side's bans at its own edge, and between them
 * where the draft stands and the phase timer running down.
 *
 * The reference apps put a win probability here. We do not have a model that
 * produces one, and a number that comes from neither an endpoint nor a
 * measurement is not one this app shows (#1671) — so the middle is the clock.
 */
const props = defineProps<{
  allyBans: number[]
  enemyBans: number[]
  secondsLeft: number
  /** What the draft is waiting for, in the player's words. */
  label: string
}>()

const { nameOf, portraitOf } = useChampionStatics()

const pad = (bans: number[]) => Array.from({ length: 5 }, (_, index) => bans[index] ?? null)

/**
 * The client sends the time left, not the phase's length. The longest value
 * seen since the timer last jumped up is the phase's length, near enough for
 * a bar — a new phase always starts from a higher number than the last ended on.
 */
const phaseLength = ref(0)
watch(() => props.secondsLeft, (next, previous) => {
  if (previous === undefined || next > previous || next > phaseLength.value) phaseLength.value = Math.max(next, 1)
}, { immediate: true })

const progress = computed(() => (phaseLength.value > 0 ? Math.min(1, props.secondsLeft / phaseLength.value) : 0))
/** The last ten seconds are when a pick is decided; the timer says so. */
const urgent = computed(() => props.secondsLeft > 0 && props.secondsLeft <= 10)
</script>

<template>
  <div class="grid grid-cols-[1fr_minmax(0,18rem)_1fr] items-center gap-4">
    <div v-for="(side, sideIndex) in [pad(allyBans), pad(enemyBans)]" :key="sideIndex" class="flex items-center gap-1" :class="sideIndex === 1 && 'order-3 flex-row-reverse'">
      <template v-for="(champion, index) in side" :key="index">
        <img
          v-if="champion !== null && portraitOf(champion)"
          :src="portraitOf(champion)!"
          :alt="`${nameOf(champion)} banned`"
          :title="`${nameOf(champion)} banned`"
          class="size-8 rounded-md object-cover opacity-60 grayscale"
        >
        <span v-else class="flex size-8 items-center justify-center rounded-md bg-elevated ring-1 ring-inset ring-default">
          <UIcon name="i-lucide-ban" class="size-3.5 text-ink-700" />
        </span>
      </template>
    </div>

    <div class="order-2 flex flex-col gap-1.5">
      <div class="flex items-baseline justify-between">
        <span class="stat-label">{{ label }}</span>
        <span
          v-if="secondsLeft > 0"
          class="stat-value text-xl leading-none transition-colors"
          :class="urgent && 'text-primary! drop-shadow-[0_0_10px_var(--color-rosegold-500)]'"
        >{{ secondsLeft }}</span>
      </div>
      <div class="h-1 overflow-hidden rounded-full bg-accented">
        <div
          class="h-full rounded-full transition-[width] duration-1000 ease-linear"
          :class="urgent ? 'bg-primary' : 'bg-ink-300'"
          :style="{ width: `${progress * 100}%` }"
        />
      </div>
    </div>
  </div>
</template>
