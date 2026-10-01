<!--
  Twin of `web/app/components/FavoriteToggle.vue`: same props, same star, same
  ceiling. App-specific: the list is `useFavoriteTruemains` from
  `composables/useFavorites.ts` (this machine's storage), and there is no
  hydration to wait for, so the star reads the list straight away.
-->
<script setup lang="ts">
import type { RegionSlug } from '#shared/types/leaderboard'

const props = withDefaults(defineProps<{
  gameName: string
  tagLine: string | null
  region?: RegionSlug | null
  profileIconId?: number | null
}>(), {
  region: null,
  profileIconId: null,
})

const { isFavorite, toggle, atLimit } = useFavoriteTruemains()

const nameTag = computed(() => favoriteNameTag(props.gameName, props.tagLine))
const active = computed(() => isFavorite(nameTag.value))
const isFull = computed(() => !active.value && atLimit.value)

const title = computed(() => {
  if (isFull.value) return `Favorites are full (${FAVORITES_LIMIT}) — remove one first`
  return active.value ? `Unfollow ${nameTag.value}` : `Follow ${nameTag.value}`
})

function onClick() {
  if (isFull.value) return
  toggle({
    gameName: props.gameName,
    tagLine: props.tagLine,
    region: props.region,
    profileIconId: props.profileIconId,
  })
}
</script>

<template>
  <!-- `relative z-10` lifts the button above the leaderboard row's stretched profile overlay. -->
  <span class="relative z-10 inline-flex shrink-0">
    <button
      type="button"
      :aria-pressed="active"
      :aria-label="title"
      :title="title"
      :disabled="isFull"
      class="inline-flex size-7 shrink-0 items-center justify-center rounded-md transition-colors hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed disabled:opacity-50"
      :class="active ? 'text-primary' : 'text-muted hover:text-primary'"
      @click.stop.prevent="onClick"
    >
      <!-- A filled star when followed: `mode="svg"` inlines the glyph so its path can be filled. -->
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
