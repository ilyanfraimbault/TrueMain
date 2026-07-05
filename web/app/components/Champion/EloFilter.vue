<script setup lang="ts">
import {
  ELO_TIERS,
  ELO_BRACKET_ALL,
  tierOnly,
  tierPlus,
  hasPlus,
  eloBracketLabel,
} from '~/utils/elo-brackets'

// Rank-emblem filter for the champion build page (issue #526). Each ranked
// tier is offered twice — the bare emblem ("that rank only") and the emblem
// with a "+" badge ("that rank and above") — so you can scope builds to a
// single rank or to a rank floor. Leads with an "All" chip (the default).
// Emblems come from the shared RankIcon (Community Dragon), and the neutral
// segmented look mirrors RolePicker so the filters read as one control.
const props = defineProps<{
  modelValue: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

function select(value: string) {
  emit('update:modelValue', value)
}

function isActive(value: string) {
  return props.modelValue === value
}
</script>

<template>
  <div class="flex items-center gap-1.5 overflow-x-auto py-0.5">
    <UButton
      :variant="isActive(ELO_BRACKET_ALL) ? 'soft' : 'ghost'"
      color="neutral"
      size="sm"
      :aria-pressed="isActive(ELO_BRACKET_ALL)"
      class="shrink-0"
      @click="select(ELO_BRACKET_ALL)"
    >
      All
    </UButton>

    <div
      v-for="tier in ELO_TIERS"
      :key="tier"
      class="flex shrink-0 items-center"
    >
      <UButton
        :variant="isActive(tierOnly(tier)) ? 'soft' : 'ghost'"
        color="neutral"
        square
        size="sm"
        :aria-label="`${eloBracketLabel(tier)} only`"
        :aria-pressed="isActive(tierOnly(tier))"
        :title="`${eloBracketLabel(tier)} only`"
        @click="select(tierOnly(tier))"
      >
        <RankIcon :tier="tier" :size="22" />
      </UButton>

      <UButton
        v-if="hasPlus(tier)"
        :variant="isActive(tierPlus(tier)) ? 'soft' : 'ghost'"
        color="neutral"
        square
        size="sm"
        :aria-label="`${eloBracketLabel(tier)} and above`"
        :aria-pressed="isActive(tierPlus(tier))"
        :title="`${eloBracketLabel(tier)} and above`"
        @click="select(tierPlus(tier))"
      >
        <span class="relative inline-flex">
          <RankIcon :tier="tier" :size="22" />
          <span
            class="absolute -right-1.5 -top-1 text-xs font-bold leading-none text-primary"
            aria-hidden="true"
          >+</span>
        </span>
      </UButton>
    </div>
  </div>
</template>
