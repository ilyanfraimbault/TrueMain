<script setup lang="ts">
/**
 * Riot's legal notice, in the wording its General Policies ask every product
 * on the API to carry where players can readily see it. Held here once: the
 * site's footer draws it under every page, the desktop app at the foot of its
 * pages (#2090), smaller there (`compact`), since a window has no footer.
 */
defineProps<{ compact?: boolean }>()
</script>

<template>
  <p :class="compact ? 'text-[10px] leading-snug text-dimmed' : 'text-xs leading-relaxed text-muted'">
    TrueMain isn't endorsed by Riot Games and doesn't reflect the views or
    opinions of Riot Games or anyone officially involved in producing or
    managing Riot Games properties. Riot Games and all associated properties
    are trademarks or registered trademarks of Riot Games, Inc.
  </p>
</template>
