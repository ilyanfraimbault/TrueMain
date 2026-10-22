<script setup lang="ts">
// The favorite star itself: `FavoriteToggle` drives it from the stored lists,
// and a host's own favorites — the app's clips (#2080) — drive it from theirs,
// so every star on the favorites page reads the same.
defineProps<{
  active: boolean
  /** The action it takes, as the label and tooltip: `Follow …`, `Remove … from favorites`. */
  label: string
  disabled?: boolean
}>()

defineEmits<{ toggle: [] }>()
</script>

<template>
  <!-- `relative z-10` lifts the button above the leaderboard row's stretched
       profile-link overlay, so clicking the star follows the player instead of
       navigating to their page. -->
  <span class="relative z-10 inline-flex shrink-0">
    <button
      type="button"
      :aria-pressed="active"
      :aria-label="label"
      :title="label"
      :disabled="disabled"
      class="inline-flex size-7 shrink-0 items-center justify-center rounded-md transition-colors hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed disabled:opacity-50"
      :class="active ? 'text-primary' : 'text-muted hover:text-primary'"
      @click.stop.prevent="$emit('toggle')"
    >
      <!-- Followed reads as a *filled* star, not as a tinted button: the
           control is the star, and a pill of background behind it competed
           with every other chip on the row.

           Two things are load-bearing here. `mode="svg"` inlines the glyph
           instead of painting it as a CSS mask — a masked icon is a flat
           silhouette with no interior to fill. And the fill has to target the
           `<path>`, not the `<svg>`: lucide writes `fill="none"` on the path
           itself, which a rule on the root element never overrides. -->
      <UIcon
        mode="svg"
        name="i-lucide-star"
        class="size-4 shrink-0"
        :class="active ? '[&_path]:fill-current' : undefined"
        aria-hidden="true"
      />
    </button>
  </span>
</template>
