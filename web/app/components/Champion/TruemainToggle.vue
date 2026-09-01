<script setup lang="ts">
// Population toggle (#1346). On — the default — the numbers beside it are folded
// only from *mains* of the champion: accounts that play it often enough to clear
// the main-analysis threshold. Off widens to every tracked player who has games
// on it.
//
// It carries the TrueMain mark rather than a word, because "truemains" is the
// product's own term and the mark is where the reader has already met it. The
// mark is never the whole control: the label sits next to it, so the button
// still reads without knowing the brand.
const props = withDefaults(defineProps<{
  modelValue: boolean
  /**
   * Locks the toggle on, with `disabledReason` explaining why. Used when a
   * matchup is pinned: that slice comes from an aggregate whose champion side is
   * mains-only, so "everyone" is not an answer it can give — and the API rejects
   * the combination rather than quietly serving mains-only rows under it.
   */
  disabled?: boolean
  disabledReason?: string
}>(), { disabled: false, disabledReason: undefined })

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

// Two-part tooltip — a title naming the population currently shown, then one
// line saying what it is and what flipping does. The same shape the thin-sample
// alert used before it became a header tooltip: the title is the state, the body
// is the consequence, and a reader who only takes the title still learns which
// games they are looking at.
const tooltip = computed<{ title: string, description: string }>(() => {
  if (props.disabled) {
    return {
      title: 'Locked to truemains',
      description: props.disabledReason
        ?? 'This slice is only aggregated over truemains.',
    }
  }
  return props.modelValue
    ? {
        title: 'Truemains only',
        description: 'Folded from players who main this champion. Turn off to include '
          + 'every tracked player with games on it.',
      }
    : {
        title: 'Every tracked player',
        description: 'Folded from every tracked player with games on this champion, '
          + 'mains included. Turn on to keep truemains only.',
      }
})

function toggle() {
  if (props.disabled) return
  emit('update:modelValue', !props.modelValue)
}
</script>

<template>
  <UTooltip
    :delay-duration="150"
    :ui="{ content: 'max-w-xs h-auto items-start' }"
  >
    <template #content>
      <div class="flex flex-col gap-0.5 p-1 text-xs">
        <span class="font-medium text-default">{{ tooltip.title }}</span>
        <span class="text-muted">{{ tooltip.description }}</span>
      </div>
    </template>

    <button
      type="button"
      :disabled="disabled"
      :aria-pressed="modelValue"
      class="inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm ring ring-inset transition-colors
             disabled:cursor-not-allowed disabled:opacity-75
             focus-visible:outline-3 focus-visible:outline-primary/25 focus-visible:ring-primary"
      :class="modelValue
        ? 'bg-primary/10 text-highlighted ring-primary/40 hover:bg-primary/15'
        : 'bg-default text-muted ring-accented hover:bg-elevated'"
      @click="toggle"
    >
      <!-- The M-check mark from AppLogo, drawn at icon size. `currentColor`
           rather than the wordmark's rose-gold ramp: off, the control has to
           read as off, and a gradient that stays lit would keep saying "on".
           Its own gradient would also collide with AppLogo's shared id. -->
      <svg
        viewBox="0 0 64 64"
        class="size-4 shrink-0"
        aria-hidden="true"
      >
        <path
          d="M13 47V21l15 17L51 15"
          fill="none"
          stroke="currentColor"
          stroke-width="8"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
      Truemains
    </button>
  </UTooltip>
</template>
