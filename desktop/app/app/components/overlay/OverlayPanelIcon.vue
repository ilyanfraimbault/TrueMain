<script setup lang="ts">
import type { OverlayPanel } from '~/types/overlay'

/** Each overlay panel's icon. Named here, in a component, so the icon bundle's scan finds them. */
defineProps<{ panel: OverlayPanel }>()

const ICONS: Record<OverlayPanel, string> = {
  'loading': 'i-lucide-users',
  'next-item': 'i-lucide-shopping-bag',
  'win-probability': 'i-lucide-scale',
  'stats': 'i-lucide-activity',
  'item-value': 'i-lucide-coins',
}
</script>

<template>
  <UIcon :name="ICONS[panel]" />
</template>
