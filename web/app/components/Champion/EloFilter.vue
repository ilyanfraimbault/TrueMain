<script setup lang="ts">
import {
  ELO_TIERS,
  ELO_BRACKET_ALL,
  ELO_PLUS_SUFFIX,
  tierOnly,
  tierPlus,
  hasPlus,
  eloBracketLabel,
  normalizeEloBracket,
} from '~/utils/elo-brackets'

// Rank filter for the champion build page (issue #526). A single dropdown that
// scopes builds by elo: "All ranks", each ranked tier ("that rank only") and
// each tier's "+" floor ("that rank and above") — Challenger has no "+" as it
// tops the ladder. Every option carries its rank emblem (shared RankIcon,
// Community Dragon) in both the trigger and the option rows.
const props = defineProps<{
  modelValue: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

interface EloItem {
  label: string
  value: string
  // `null` for the "All ranks" entry, which has no single emblem.
  tier: string | null
}

const items = computed<EloItem[]>(() => {
  const options: EloItem[] = [
    { label: eloBracketLabel(ELO_BRACKET_ALL), value: ELO_BRACKET_ALL, tier: null },
  ]
  for (const tier of ELO_TIERS) {
    options.push({ label: eloBracketLabel(tierOnly(tier)), value: tierOnly(tier), tier })
    if (hasPlus(tier)) {
      options.push({ label: eloBracketLabel(tierPlus(tier)), value: tierPlus(tier), tier })
    }
  }
  return options
})

// Emblem for the trigger reflects the selected bracket; the "+" forms share
// their base tier's crest, and "All ranks" shows no crest.
const selectedTier = computed<string | null>(() => {
  const value = normalizeEloBracket(props.modelValue)
  if (value === ELO_BRACKET_ALL) return null
  return value.endsWith(ELO_PLUS_SUFFIX) ? value.slice(0, -ELO_PLUS_SUFFIX.length) : value
})

function onChange(value: string) {
  emit('update:modelValue', value)
}
</script>

<template>
  <USelect
    :model-value="modelValue"
    :items="items"
    value-key="value"
    label-key="label"
    aria-label="Rank"
    class="w-44"
    @update:model-value="onChange"
  >
    <template #leading>
      <RankIcon v-if="selectedTier" :tier="selectedTier" :size="20" />
    </template>
    <template #item-leading="{ item }">
      <RankIcon v-if="(item as EloItem).tier" :tier="(item as EloItem).tier" :size="20" />
    </template>
  </USelect>
</template>
