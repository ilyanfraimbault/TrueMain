<script setup lang="ts">
import type { RegionSlug } from '~~/shared/types/leaderboard'
import { FAVORITES_LIMIT, favoriteNameTag } from '~/utils/favorites'

// Follow / unfollow a truemain (#531). Used on the leaderboard rows and on the
// player profile; the list itself lives in `localStorage` behind
// `useFavoriteTruemains`.
const props = withDefaults(defineProps<{
  gameName: string
  tagLine: string | null
  /** Region slug for the stored entry (drives the flag on the favorites view). */
  region?: RegionSlug | null
  /** Profile icon id, so the favorites view can draw an avatar before its own fetch lands. */
  profileIconId?: number | null
  /** Labelled pill ("Follow" / "Following") instead of the compact icon toggle. */
  withLabel?: boolean
}>(), {
  region: null,
  profileIconId: null,
  withLabel: false,
})

const { isFavorite, toggle, atLimit } = useFavoriteTruemains()

const nameTag = computed(() => favoriteNameTag(props.gameName, props.tagLine))
const active = computed(() => isFavorite(nameTag.value))

// Refuse rather than silently evicting somebody else's entry once the cap is
// reached. Removing is always allowed.
const isFull = computed(() => !active.value && atLimit.value)

const label = computed(() => (active.value ? 'Following' : 'Follow'))

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
  <!-- `relative z-10` lifts the button above the leaderboard row's stretched
       profile-link overlay, so clicking the star follows the player instead of
       navigating to their page. -->
  <span class="relative z-10 inline-flex shrink-0">
    <!--
      Client-only by construction. `useFavoriteTruemains` already guarantees an
      empty list during hydration, but this component also renders inside rows
      that a page may later choose to hydrate lazily — and a deferred hydration
      runs *after* the mount-time storage read, which would reconcile against
      stale DOM. Rendering the control only on the client removes that whole
      failure mode; the fallback reserves the exact same box, so nothing shifts.
    -->
    <ClientOnly>
      <button
        type="button"
        :aria-pressed="active"
        :aria-label="title"
        :title="title"
        :disabled="isFull"
        class="inline-flex shrink-0 items-center justify-center gap-1.5 rounded-md ring-1 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed disabled:opacity-50"
        :class="[
          withLabel ? 'h-8 px-2.5 text-xs font-semibold' : 'size-7',
          active
            ? 'bg-primary/15 text-primary ring-primary/40'
            : 'text-muted ring-transparent hover:bg-primary/10 hover:text-primary',
        ]"
        @click.stop.prevent="onClick"
      >
        <UIcon
          name="i-lucide-star"
          class="size-4 shrink-0"
          aria-hidden="true"
        />
        <span v-if="withLabel">{{ label }}</span>
      </button>

      <template #fallback>
        <span
          class="inline-flex shrink-0"
          :class="withLabel ? 'h-8 w-24' : 'size-7'"
          aria-hidden="true"
        />
      </template>
    </ClientOnly>
  </span>
</template>
