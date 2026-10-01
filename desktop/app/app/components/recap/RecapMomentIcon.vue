<script setup lang="ts">
import type { MomentKind } from '~/types/recordings'

/**
 * A moment's glyph. The names live in a component, not in a `.ts` map: the
 * build bundles only the icons it finds in components (`icon.clientBundle.scan`).
 */
defineProps<{ kind: MomentKind }>()

const ICONS: Record<MomentKind, string> = {
  kill: 'i-lucide-swords',
  death: 'i-lucide-skull',
  assist: 'i-lucide-handshake',
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
