<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import type { RegionSlug } from '~~/shared/types/leaderboard'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { platformIdToRegion } from '~~/shared/utils/region'
import { truemainNameTag, truemainProfilePath } from '~~/shared/utils/truemain-path'

/**
 * A truemain as the site shows one (#1734): profile icon, name, #tag and region
 * flag — the leaderboard table, the champion-page sidebar row, the homepage
 * panel, the favorites cards, the profile header and the builder's games drawer
 * all draw the player through this, at the size their surface wants. The
 * search palette is the one exception: it re-fills the same row DOM on every
 * keystroke and keeps `SkeletonImage`, which blanks between two pictures.
 *
 * Built on `UUser`: its link is an overlay inside its own `relative` root, so
 * the whole identity is one target without wrapping it in an <a>, and it works
 * inside a table row (a row-wide overlay does not, in Safari). `to` defaults to
 * the player's profile; `false` renders no link, for a parent that already is
 * one (a sidebar row's overlay, the homepage row, the profile page itself).
 *
 * The icon is a plain <img> through `useCanonicalIcon` (`as: { img: 'img' }`):
 * UAvatar would otherwise render NuxtImg with a srcset, a second cache entry
 * for an asset the rest of the site already fetches (#1000). It is keyed on its
 * URL, so a re-used row never shows the previous player's picture while the
 * next one decodes, and it requests nothing for an id the account never had
 * (`profileIconId` 0 or missing) — the user glyph stands in, as it does for a
 * picture that fails to load.
 */
export interface AccountIdentity {
  gameName: string
  tagLine: string | null
  profileIconId?: number | null
  platformId?: string | null
}

type AccountSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl'

defineOptions({ inheritAttrs: false })

const props = withDefaults(defineProps<{
  identity: AccountIdentity
  /** Overrides the region read from `identity.platformId`; `null` hides the flag. */
  region?: RegionSlug | null
  /** Data Dragon patch for the icon; null until the version list has loaded. */
  patch: string | null
  size?: AccountSize
  /** `inline` is one line — `Name#Tag`, no flag — for a chip above a match row. */
  layout?: 'stacked' | 'inline'
  /** Where the identity links to: the profile by default, nowhere with `false`. */
  to?: RouteLocationRaw | false
  loading?: 'lazy' | 'eager'
  ui?: { root?: string, avatar?: string, body?: string, name?: string, tag?: string, subline?: string }
}>(), {
  region: undefined,
  size: 'md',
  layout: 'stacked',
  to: undefined,
  loading: undefined,
  ui: () => ({}),
})

// Literal class strings per size, for Tailwind's scan. Avatars are rounded
// squares — profile icons are square art — at the radius each size reads at.
const SIZES: Record<AccountSize, { root: string, avatar: string, name: string, tag: string, flag: number }> = {
  xs: { root: 'gap-1.5', avatar: 'size-[18px] rounded-sm text-[10px]', name: 'text-xs font-medium', tag: 'text-xs text-muted', flag: 14 },
  sm: { root: 'gap-2', avatar: 'size-7 rounded-md text-xs', name: 'text-sm font-bold', tag: 'text-[11px] text-muted', flag: 16 },
  md: { root: 'gap-2.5', avatar: 'size-9 rounded-md text-sm', name: 'text-sm font-semibold', tag: 'text-xs text-muted', flag: 16 },
  lg: { root: 'gap-2.5', avatar: 'size-10 rounded-md text-sm', name: 'text-sm font-bold', tag: 'text-[11px] text-muted', flag: 18 },
  xl: { root: 'gap-4', avatar: 'size-20 rounded-lg text-2xl', name: 'text-2xl font-semibold', tag: 'text-2xl font-semibold', flag: 18 },
}

const sized = computed(() => SIZES[props.size])

const canonicalIcon = useCanonicalIcon()
const hasIcon = computed(() => (props.identity.profileIconId ?? 0) > 0)
const src = computed(() => (hasIcon.value
  ? canonicalIcon(getProfileIconUrl(props.identity.profileIconId!, props.patch))
  : undefined))

// A picture that fails to load (an icon id Data Dragon does not serve) falls
// back to the user glyph, like an account with no icon at all — not to an
// empty square that reads as still loading.
const failed = ref(false)
watch(src, () => {
  failed.value = false
})

const region = computed(() => (props.region !== undefined
  ? props.region
  : platformIdToRegion(props.identity.platformId)))

const href = computed(() => (props.to === false
  ? undefined
  : props.to ?? truemainProfilePath(truemainNameTag(props.identity.gameName, props.identity.tagLine))))

// The link's accessible name: the overlay has no text of its own.
const riotId = computed(() => (props.identity.tagLine
  ? `${props.identity.gameName} #${props.identity.tagLine}`
  : props.identity.gameName))
</script>

<template>
  <UUser
    v-bind="$attrs"
    :to="href"
    :name="riotId"
    :ui="{
      root: ['min-w-0', sized.root, ui.root],
      wrapper: ['min-w-0 flex-1', ui.body],
      // UUser grows a linked avatar on hover; a row of players should not pulse.
      avatar: 'shrink-0 transform-none group-hover/user:scale-100 group-has-focus-visible/user:scale-100',
    }"
  >
    <template #avatar>
      <UAvatar
        :key="src ?? 'none'"
        :src="src"
        alt=""
        :as="{ img: 'img' }"
        :loading="loading"
        :icon="hasIcon && !failed ? undefined : 'i-lucide-user'"
        :ui="{ root: ['bg-accented', sized.avatar, ui.avatar], icon: 'size-1/2 text-dimmed' }"
        @error="failed = true"
      />
    </template>

    <span v-if="layout === 'inline'" class="flex min-w-0 items-baseline" :class="ui.name">
      <span class="truncate transition-colors" :class="[sized.name, href ? 'group-hover/user:text-primary' : undefined]">{{ identity.gameName }}</span>
      <span v-if="identity.tagLine" class="shrink-0" :class="sized.tag">#{{ identity.tagLine }}</span>
    </span>

    <template v-else>
      <slot name="name">
        <div class="flex min-w-0 items-baseline gap-1">
          <span
            class="truncate text-default transition-colors"
            :class="[sized.name, href ? 'group-hover/user:text-highlighted' : undefined, ui.name]"
          >{{ identity.gameName }}</span>
          <span v-if="identity.tagLine" class="shrink-0" :class="[sized.tag, ui.tag]">#{{ identity.tagLine }}</span>
          <slot name="badge" />
        </div>
      </slot>
      <!-- `ui.subline` replaces the default spacing rather than adding to it:
           two margins or two gaps on one element leave the winner to
           stylesheet order. -->
      <div v-if="region || $slots.subline" class="flex min-w-0 items-center" :class="ui.subline ?? 'mt-0.5 gap-1.5'">
        <LeaderboardRegionFlag v-if="region" :region="region" :width="sized.flag" />
        <slot name="subline" />
      </div>
    </template>
  </UUser>
</template>
