<script setup lang="ts">
// The desktop app's own icon, drawn as the site draws it wherever the app is
// offered (home banner, /download): the M-check mark on the app's ink tile, as
// `desktop/app-icon.svg` ships it, with a rose-gold halo behind it. The tile's
// rounding stands in for the squircle macOS masks the real icon into.
//
// Its gradient id is shared between instances, like `AppLogo`'s: identical defs
// resolve to the same paint.
withDefaults(defineProps<{
  /** Draw the soft halo behind the tile. */
  glow?: boolean
}>(), { glow: true })
</script>

<template>
  <span class="relative inline-flex shrink-0">
    <span
      v-if="glow"
      aria-hidden="true"
      class="pointer-events-none absolute -inset-[45%] rounded-full bg-primary/25 blur-2xl"
    />
    <svg
      viewBox="0 0 64 64"
      class="relative size-full rounded-[23%] shadow-[0_10px_30px_-8px_rgb(0_0_0/0.7)] ring-1 ring-white/10"
      aria-hidden="true"
    >
      <defs>
        <linearGradient
          id="tm-app-icon-grad"
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
      <rect
        width="64"
        height="64"
        fill="#0b0b0d"
      />
      <path
        d="M16 48V22l14 16L48 16"
        fill="none"
        stroke="url(#tm-app-icon-grad)"
        stroke-width="7.5"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
    </svg>
  </span>
</template>
