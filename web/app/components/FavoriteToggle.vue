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
}>(), {
  region: null,
  profileIconId: null,
})

const { isFavorite, toggle, atLimit } = useFavoriteTruemains()

const nameTag = computed(() => favoriteNameTag(props.gameName, props.tagLine))

/**
 * Hydration guard, scoped to this component instance.
 *
 * The stored list is client-only, so this button's *first* render must not
 * depend on it — otherwise the markup Vue reconciles against the server HTML
 * differs and we are back to #838 / #840. `useFavoriteTruemains` already seeds
 * an empty list for SSR, but that alone is not enough here: the control also
 * renders inside `LeaderboardRow`, which the champion page mounts through
 * `<LazyChampionTruemains hydrate-on-visible>`. That subtree hydrates long
 * after the mount-time storage read has filled the shared state, so a
 * state-derived first render would reconcile against stale DOM.
 *
 * A local mounted flag makes the guard hold whenever this instance happens to
 * hydrate: its own first render is always the neutral "Follow" state, and the
 * real state arrives as an ordinary post-hydration update. The markup itself is
 * identical either way — only `aria-pressed`, the colours and the title change.
 *
 * (`<ClientOnly>` works in isolation but not here: on the profile page, whose
 * SSR/client divergence predates this feature, mismatch recovery higher up the
 * tree left the swap unperformed and the control stayed invisible.)
 */
const mounted = ref(false)
onMounted(() => {
  mounted.value = true
})

const active = computed(() => mounted.value && isFavorite(nameTag.value))

// The store itself evicts the oldest entry past the cap (one cap policy, shared
// by reads and writes — see `add`). Declining the click here layers a courtesy
// on top of that: a user's own click should never silently drop someone they
// followed earlier. Removing is always allowed. Mount-gated like `active`,
// since the cap is a property of the stored list.
const isFull = computed(() => mounted.value && !active.value && atLimit.value)

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
      <!-- Followed reads as a *filled* star, not as a tinted button: the
           control is the star, and a pill of background behind it competed
           with every other chip on the row.

           Two things are load-bearing here. `mode="svg"` inlines the glyph
           instead of painting it as a CSS mask — a masked icon is a flat
           silhouette with no interior to fill. And the fill has to target the
           `<path>`, not the `<svg>`: lucide writes `fill="none"` on the path
           itself, which a rule on the root element never overrides. -->
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
