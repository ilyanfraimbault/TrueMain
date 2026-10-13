<script setup lang="ts">
// The one-trick pony marker beside a player's champion.
defineProps<{ noop?: never }>()
</script>

<template>
  <span
    class="inline-flex items-center rounded-full bg-amber-400/25 px-1.5 py-0.5 text-[9px] font-bold uppercase tracking-wide text-amber-200 ring-1 ring-amber-400/50"
    title="One-trick pony"
  >
    OTP
  </span>
</template>
