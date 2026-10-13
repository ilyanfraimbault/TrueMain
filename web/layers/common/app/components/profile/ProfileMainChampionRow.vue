<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import { getPositionIconUrl } from '#shared/utils/ddragon'

// One champion in `ProfileMainChampions`: portrait, name and its markers, the
// lane and a line of figures under it, and one figure on the right (the default
// slot). The whole row is the link target — so the portrait is a plain image
// rather than <ChampionLink>, whose own <a> would nest inside this one.
defineProps<{
  to: RouteLocationRaw
  iconUrl: string | null
  name: string
  /** Riot team position; empty when the champion has no dominant lane. */
  position: string | null
  isOtp?: boolean
  /** Why the figures are dated, when the games behind them have aged out (#1216). */
  retiredNote?: string | null
}>()

defineSlots<{
  meta?: () => unknown
  default?: () => unknown
}>()
</script>

<template>
  <NuxtLink
    :to="to"
    class="surface-hover flex items-center gap-3 px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
  >
    <SkeletonImage
      :src="iconUrl"
      :alt="name"
      :title="name"
      class="size-9 shrink-0 rounded"
    />
    <div class="flex min-w-0 flex-1 flex-col">
      <div class="flex items-center gap-1.5">
        <span class="truncate text-sm font-medium">
          {{ name }}
        </span>
        <OtpBadge v-if="isOtp" />
        <!-- Same idiom as the champion header's thin-sample qualifier: the
             explanation lives in the tooltip so it never crowds the row. -->
        <WarningDot v-if="retiredNote" :text="retiredNote" />
      </div>
      <div class="flex items-center gap-1 text-[11px] text-muted tabular-nums">
        <SkeletonImage
          v-if="position"
          :src="getPositionIconUrl(position)"
          :alt="position"
          class="size-3"
          transparent
        />
        <span><slot name="meta" /></span>
      </div>
    </div>
    <div class="shrink-0 text-sm font-semibold tabular-nums text-default">
      <slot />
    </div>
  </NuxtLink>
</template>
