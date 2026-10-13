<script setup lang="ts">
// The one-trick pony marker beside a player or their champion: the truemains
// table, the champion page's truemains and the profile's main champions.
defineProps<{ noop?: never }>()
</script>

<template>
  <span
    class="inline-flex shrink-0 items-center rounded-full bg-amber-400/25 px-1.5 py-px text-[9px] font-bold uppercase leading-none tracking-wide text-amber-200 ring-1 ring-amber-400/50"
    title="One-trick pony"
  >
    OTP
  </span>
</template>
