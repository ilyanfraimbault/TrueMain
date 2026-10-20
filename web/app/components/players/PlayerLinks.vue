<script setup lang="ts">
import type { PersonaLink, PersonaLinkKind } from '~~/shared/types/players'

// A known player's channels (#2016): Twitch, YouTube and X only, as square
// buttons carrying the brand's coloured logo and no label — the logo says which
// platform; the name stays in the tooltip and the accessible label. The other
// kinds the intake stores (wikis, stat sites, smaller platforms) are not shown.
// One button per platform (#2177): two identical logos would tell the viewer
// nothing, and a source often lists one channel twice (old and new URL forms,
// a renamed account). The API sorts each kind newest first, so the first wins.
// The order is the API's (by kind), so a page never reshuffles.
const props = defineProps<{ links: PersonaLink[] }>()

// Literal class strings, for Tailwind's scan. X's mark is black on white, so it
// takes the text colour and stays visible in both themes.
const SHOWN: Partial<Record<PersonaLinkKind, { label: string, icon: string, color: string }>> = {
  twitch: { label: 'Twitch', icon: 'i-simple-icons-twitch', color: 'text-[#9146FF]' },
  youtube: { label: 'YouTube', icon: 'i-simple-icons-youtube', color: 'text-[#FF0000]' },
  x: { label: 'X', icon: 'i-simple-icons-x', color: 'text-highlighted' },
}

const shown = computed(() => {
  const firstOfKind = new Map<PersonaLinkKind, string>()
  for (const link of props.links) {
    if (SHOWN[link.kind] && !firstOfKind.has(link.kind)) {
      firstOfKind.set(link.kind, link.url)
    }
  }
  return [...firstOfKind].map(([kind, url]) => ({ ...SHOWN[kind]!, url }))
})
</script>

<template>
  <ul
    v-if="shown.length"
    class="flex flex-wrap items-center gap-2"
  >
    <li
      v-for="link in shown"
      :key="link.url"
    >
      <UTooltip :text="link.label">
        <UButton
          :to="link.url"
          target="_blank"
          rel="noopener nofollow"
          :icon="link.icon"
          :aria-label="link.label"
          color="neutral"
          variant="soft"
          size="md"
          square
          :ui="{ leadingIcon: ['size-5', link.color] }"
        />
      </UTooltip>
    </li>
  </ul>
</template>
