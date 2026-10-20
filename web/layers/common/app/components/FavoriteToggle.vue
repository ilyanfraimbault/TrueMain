<script setup lang="ts">
import type { RegionSlug } from '#shared/types/leaderboard'
import type { FavoritePickInput } from '#common/utils/favorites'
import { FAVORITE_PICKS_LIMIT_PER_KIND, FAVORITES_LIMIT, favoriteNameTag } from '#common/utils/favorites'

// Follow / unfollow a truemain (#531) — the leaderboard rows and the player
// profile — or star / unstar a champion or a matchup (#2080) when `pick` is
// given. The lists live in `localStorage` behind `useFavoriteTruemains` and
// `useFavoritePicks`.
const props = withDefaults(defineProps<{
  gameName?: string
  tagLine?: string | null
  /** Region slug for the stored entry (drives the flag on the favorites view). */
  region?: RegionSlug | null
  /** Profile icon id, so the favorites view can draw an avatar before its own fetch lands. */
  profileIconId?: number | null
  /** A champion or a matchup to star instead of a player. */
  pick?: FavoritePickInput | null
  /** How the pick reads in the button's label: `Ahri`, `Ahri vs Zed`. */
  pickLabel?: string
}>(), {
  gameName: '',
  tagLine: null,
  region: null,
  profileIconId: null,
  pick: null,
  pickLabel: '',
})

const { isFavorite, toggle, atLimit } = useFavoriteTruemains()
const { isFavoritePick, isPickKindFull, togglePick } = useFavoritePicks()

const nameTag = computed(() => favoriteNameTag(props.gameName, props.tagLine))
const subject = computed(() => props.pick ? (props.pickLabel || 'this page') : nameTag.value)

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

const active = computed(() => mounted.value
  && (props.pick ? isFavoritePick(props.pick) : isFavorite(nameTag.value)))

// The store itself evicts the oldest entry past the cap (one cap policy, shared
// by reads and writes — see `add`). Declining the click here layers a courtesy
// on top of that: a user's own click should never silently drop someone they
// followed earlier. Removing is always allowed. Mount-gated like `active`,
// since the cap is a property of the stored list.
const isFull = computed(() => mounted.value && !active.value
  && (props.pick ? isPickKindFull(props.pick.kind) : atLimit.value))

const title = computed(() => {
  if (isFull.value) {
    const limit = props.pick ? FAVORITE_PICKS_LIMIT_PER_KIND : FAVORITES_LIMIT
    return `Favorites are full (${limit}) — remove one first`
  }
  // A player is followed; a page is starred.
  if (props.pick) return active.value ? `Remove ${subject.value} from favorites` : `Add ${subject.value} to favorites`
  return active.value ? `Unfollow ${subject.value}` : `Follow ${subject.value}`
})

function onClick() {
  if (isFull.value) return
  if (props.pick) {
    togglePick(props.pick)
    return
  }
  toggle({
    gameName: props.gameName,
    tagLine: props.tagLine,
    region: props.region,
    profileIconId: props.profileIconId,
  })
}
</script>

<template>
  <FavoriteStar :active="active" :label="title" :disabled="isFull" @toggle="onClick" />
</template>
