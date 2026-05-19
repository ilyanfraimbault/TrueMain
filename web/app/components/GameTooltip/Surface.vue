<script setup lang="ts">
// Glass-aesthetic shell used as the inner surface of every rich game-entity
// tooltip. UTooltip's default content class is a 24px-tall pill that clips
// our content — wrappers override `:ui="{ content: '...' }"` to neutralise
// that pill and let this Surface own the visuals.
</script>

<template>
  <div class="max-w-xs rounded-lg bg-default/85 p-3 text-sm leading-snug text-default shadow-lg ring-1 ring-default backdrop-blur-md max-h-[70vh] overflow-y-auto">
    <slot />
  </div>
</template>
