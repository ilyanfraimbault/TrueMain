<script setup lang="ts">
import type { PersonaLink, PersonaLinkKind } from '~~/shared/types/players'

// A known player's public channels and pages (#2016), as brand-coloured icons
// that open off-site — the logo says which platform, so no label is written;
// the name stays in the accessible label and the tooltip. The order is the
// API's (by kind), so a page never reshuffles.
defineProps<{ links: PersonaLink[] }>()

// Literal class strings, for Tailwind's scan. A brand whose mark is black or
// white takes the text colour, so it stays visible in both themes.
const KINDS: Record<PersonaLinkKind, { label: string, icon: string, color: string }> = {
  twitch: { label: 'Twitch', icon: 'i-simple-icons-twitch', color: 'text-[#9146FF]' },
  youtube: { label: 'YouTube', icon: 'i-simple-icons-youtube', color: 'text-[#FF0000]' },
  x: { label: 'X', icon: 'i-simple-icons-x', color: 'text-highlighted' },
  kick: { label: 'Kick', icon: 'i-simple-icons-kick', color: 'text-[#53FC18]' },
  tiktok: { label: 'TikTok', icon: 'i-simple-icons-tiktok', color: 'text-highlighted' },
  instagram: { label: 'Instagram', icon: 'i-simple-icons-instagram', color: 'text-[#E4405F]' },
  soop: { label: 'SOOP', icon: 'i-lucide-tv', color: 'text-[#2D7CFF]' },
  chzzk: { label: 'CHZZK', icon: 'i-lucide-tv', color: 'text-[#00FFA3]' },
  leaguepedia: { label: 'Leaguepedia', icon: 'i-lucide-book-open', color: 'text-muted' },
  lolpros: { label: 'LoLPros', icon: 'i-lucide-trophy', color: 'text-muted' },
  deeplol: { label: 'DeepLoL', icon: 'i-lucide-bar-chart-3', color: 'text-muted' },
  website: { label: 'Website', icon: 'i-lucide-globe', color: 'text-muted' },
}

const kindOf = (link: PersonaLink) => KINDS[link.kind] ?? KINDS.website
</script>

<template>
  <ul
    v-if="links.length"
    class="flex flex-wrap items-center gap-1"
  >
    <li
      v-for="link in links"
      :key="link.url"
    >
      <UTooltip :text="kindOf(link).label">
        <UButton
          :to="link.url"
          target="_blank"
          rel="noopener nofollow"
          :icon="kindOf(link).icon"
          :aria-label="kindOf(link).label"
          color="neutral"
          variant="ghost"
          size="lg"
          square
          :ui="{ leadingIcon: kindOf(link).color }"
        />
      </UTooltip>
    </li>
  </ul>
</template>
