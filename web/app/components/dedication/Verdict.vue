<script setup lang="ts">
import type { TruemainDedication } from '~~/shared/types/dedication'
import { dedicationVerdict } from '~/utils/dedication'

// The Truemain score's one-word verdict as a pill. OTP wears the exact amber of
// the leaderboard's OTP badge — both read the same `isOtp` flag, so they must
// also look like the same thing; "Main" stays neutral.
const props = defineProps<{
  dedication: TruemainDedication
}>()

const verdict = computed(() => dedicationVerdict(props.dedication))
</script>

<template>
  <span
    class="shrink-0 rounded-full px-1.5 py-0.5 text-[9px] font-bold uppercase leading-none tracking-wide ring-1"
    :class="verdict === 'OTP'
      ? 'bg-amber-400/25 text-amber-200 ring-amber-400/50'
      : 'bg-elevated/60 text-muted ring-default'"
  >
    {{ verdict }}
  </span>
</template>
