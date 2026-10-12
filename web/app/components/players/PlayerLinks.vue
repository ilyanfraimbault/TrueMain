<script setup lang="ts">
import type { PersonaLink, PersonaLinkKind } from '~~/shared/types/players'

// A known player's public channels and pages (#2016), as compact buttons that
// open off-site. The order is the API's (by kind), so a page never reshuffles.
defineProps<{ links: PersonaLink[] }>()

const KINDS: Record<PersonaLinkKind, { label: string, icon: string }> = {
  twitch: { label: 'Twitch', icon: 'i-simple-icons-twitch' },
  youtube: { label: 'YouTube', icon: 'i-simple-icons-youtube' },
  x: { label: 'X', icon: 'i-simple-icons-x' },
  kick: { label: 'Kick', icon: 'i-simple-icons-kick' },
  tiktok: { label: 'TikTok', icon: 'i-simple-icons-tiktok' },
  instagram: { label: 'Instagram', icon: 'i-simple-icons-instagram' },
  leaguepedia: { label: 'Leaguepedia', icon: 'i-lucide-book-open' },
  lolpros: { label: 'LoLPros', icon: 'i-lucide-trophy' },
  website: { label: 'Website', icon: 'i-lucide-globe' },
}

const kindOf = (link: PersonaLink) => KINDS[link.kind] ?? KINDS.website
</script>

<template>
  <ul
    v-if="links.length"
    class="flex flex-wrap gap-2"
  >
    <li
      v-for="link in links"
      :key="link.url"
    >
      <UButton
        :to="link.url"
        target="_blank"
        rel="noopener nofollow"
        :icon="kindOf(link).icon"
        :label="kindOf(link).label"
        color="neutral"
        variant="soft"
        size="sm"
      />
    </li>
  </ul>
</template>
