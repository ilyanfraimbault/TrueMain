<script setup lang="ts">
import type { ObjectiveMarkerKind } from '#common/utils/timeline-objectives'

/**
 * An objective's glyph on the match detail's time axis (#2102) — the same
 * glyphs as the desktop recap's moments (`RecapMomentIcon`). The names live in
 * a component, not in a `.ts` map: the app's build bundles only the icons it
 * finds in components (`icon.clientBundle.scan`).
 */
defineProps<{ kind: ObjectiveMarkerKind }>()

const ICONS: Record<ObjectiveMarkerKind, string> = {
  dragon: 'i-lucide-flame',
  elder: 'i-lucide-flame-kindling',
  baron: 'i-lucide-crown',
  herald: 'i-lucide-eye',
  grubs: 'i-lucide-bug',
  atakhan: 'i-lucide-shield-half',
  tower: 'i-lucide-castle',
  inhibitor: 'i-lucide-gem',
}
</script>

<template>
  <UIcon :name="ICONS[kind]" />
</template>
