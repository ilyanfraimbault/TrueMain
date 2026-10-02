<script setup lang="ts">
/** The site's M-check mark alone: an M whose last stroke becomes a checkmark, on the wordmark's rose-gold ramp. */
// Each instance names its own gradient: two marks on one page would otherwise share the first one's.
const gradient = `tm-mark-${useId()}`
</script>

<template>
  <svg viewBox="0 0 64 64" aria-hidden="true">
    <defs>
      <linearGradient :id="gradient" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0" stop-color="#f2cfa6" />
        <stop offset="0.55" stop-color="#e2917e" />
        <stop offset="1" stop-color="#8a3b44" />
      </linearGradient>
    </defs>
    <path
      d="M13 47V21l15 17L51 15"
      fill="none"
      :stroke="`url(#${gradient})`"
      stroke-width="8"
      stroke-linecap="round"
      stroke-linejoin="round"
    />
  </svg>
</template>
