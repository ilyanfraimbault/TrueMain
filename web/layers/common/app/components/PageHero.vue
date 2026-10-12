<script setup lang="ts">
/**
 * The banner a detail page opens on — the desktop dashboard's (#1682), shared:
 * a splash art under two scrims (left for the title, bottom for the tiles), the
 * subject's portrait and name, actions in the top-right corner, and a row of
 * `KpiTile`s in the default slot, four abreast (two on a phone).
 *
 * The host resolves `splashUrl` (the site through its image proxy, the app
 * straight from Data Dragon), so this component never asks which app it runs in.
 */
withDefaults(defineProps<{
  splashUrl?: string | null
  /** CSS `object-position` of the splash: where the art's subject sits. */
  splashPosition?: string
}>(), { splashUrl: null, splashPosition: '68% 22%' })
</script>

<template>
  <section class="relative overflow-hidden rounded-lg border border-default bg-ink-950">
    <img
      v-if="splashUrl"
      :src="splashUrl"
      alt=""
      aria-hidden="true"
      decoding="async"
      class="absolute inset-0 size-full object-cover"
      :style="{ objectPosition: splashPosition }"
    >
    <div class="pointer-events-none absolute inset-0 bg-linear-to-r from-ink-950/85 via-ink-950/30 to-transparent" />
    <div class="pointer-events-none absolute inset-0 bg-linear-to-t from-ink-950 via-ink-950/40 to-transparent" />

    <div class="relative flex flex-col gap-5 p-4 pt-5">
      <div class="flex items-center gap-4">
        <slot name="portrait" />
        <div class="min-w-0 [text-shadow:0_1px_12px_rgb(0_0_0/0.6)]">
          <slot name="title" />
          <div class="mt-1 flex flex-wrap items-center gap-2 text-sm text-muted">
            <slot name="subtitle" />
          </div>
        </div>
        <div
          v-if="$slots.actions"
          class="ml-auto flex items-center gap-2 self-start"
        >
          <slot name="actions" />
        </div>
      </div>
      <div
        v-if="$slots.default"
        class="grid grid-cols-2 gap-2 sm:grid-cols-4"
      >
        <slot />
      </div>
    </div>
  </section>
</template>
