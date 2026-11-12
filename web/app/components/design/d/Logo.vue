<script setup lang="ts">
// `AppLogo`, with a gradient id of its own per instance. The shell draws the
// logo up to three times (the sidebar, the mobile top bar, the drawer) and hides
// the ones the viewport doesn't use; `AppLogo`'s shared id then resolves to the
// first definition in the document, inside a `display: none` subtree, which
// Chromium does not paint — the mark vanished.
const gradientId = `d-mark-${useId()}`
</script>

<template>
  <span class="inline-flex items-center gap-1.5 font-bold tracking-tight">
    <svg
      viewBox="0 0 64 64"
      class="h-[1.15em] w-[1.15em] shrink-0"
      aria-hidden="true"
    >
      <defs>
        <linearGradient
          :id="gradientId"
          x1="0"
          y1="0"
          x2="0"
          y2="1"
        >
          <stop
            offset="0"
            stop-color="#f2cfa6"
          />
          <stop
            offset="0.55"
            stop-color="#e2917e"
          />
          <stop
            offset="1"
            stop-color="#8a3b44"
          />
        </linearGradient>
      </defs>
      <path
        d="M13 47V21l15 17L51 15"
        fill="none"
        :stroke="`url(#${gradientId})`"
        stroke-width="8"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
    </svg>
    <span class="inline-flex items-baseline">
      <span
        class="bg-gradient-to-r from-rosegold-400 via-rosegold-300 to-gold bg-clip-text text-transparent [forced-color-adjust:none]"
      >True</span><span class="text-default">Main</span>
    </span>
  </span>
</template>
