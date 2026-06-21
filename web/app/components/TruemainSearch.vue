<script setup lang="ts">
import { isNavigationFailure } from 'vue-router'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import type { SearchResult } from '~~/shared/types/search'
import { formatTier } from '~/utils/tiers'
// Explicit (over Nuxt auto-import) so the template's {{ SEARCH_MIN_LENGTH }}
// has a visible source.
import { SEARCH_MIN_LENGTH } from '~/composables/useTruemainSearch'

// Truemain name/tag search. A trigger (a search-field look on the page, a
// compact icon button in the header) opens a modal with a debounced lookup
// that links straight to a player's profile. The heavy part (the query, the
// results list) lives inside the modal so it never participates in SSR —
// matching the home ChampionSearch pattern.
const props = withDefaults(defineProps<{
  /** `field` = full search-bar trigger (page hero); `button` = compact icon (header). */
  variant?: 'field' | 'button'
}>(), {
  variant: 'field',
})

const open = ref(false)
const term = ref('')
const router = useRouter()

const { results, status, tooShort } = useTruemainSearch(term)

// Only nudge "type more" once the user has actually started typing — an empty
// box (modal just opened) shows nothing under the input, the placeholder
// already says what to enter.
const hasTyped = computed(() => term.value.trim().length > 0)

const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

// `{gameName}-{tagLine}` (or just the name when untagged) — the same slug the
// leaderboard rows use to reach a profile.
function profilePath(result: SearchResult): string {
  const { gameName, tagLine } = result.identity
  const slug = tagLine ? `${gameName}-${tagLine}` : gameName
  return `/truemains/${encodeURIComponent(slug)}`
}

function go(path: string) {
  open.value = false
  // Swallow only aborted/redirected navigations; let real errors surface.
  router.push(path).catch((error) => {
    if (!isNavigationFailure(error)) throw error
  })
}

// Enter jumps to the first result — the fast path for "type the name, hit
// enter". No-op while the list is empty.
function onEnter() {
  const first = results.value[0]
  if (first) go(profilePath(first))
}

// Start each open from a clean slate so a stale term never flashes old results.
watch(open, (isOpen) => {
  if (!isOpen) term.value = ''
})

// Resolve each result's profile-icon URL once, up front, so the template
// doesn't call the resolver twice per row (v-if + :src).
const displayResults = computed(() => results.value.map(result => ({
  result,
  iconUrl: getProfileIconUrl(result.identity.profileIconId, latestPatch.value),
})))
</script>

<template>
  <div>
    <!-- Field trigger: looks like a search bar (page hero). -->
    <button
      v-if="props.variant === 'field'"
      type="button"
      class="group flex h-12 w-full items-center gap-3 rounded-xl border border-default bg-default/60 px-4 text-left backdrop-blur-md transition-colors hover:border-primary/50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      aria-label="Search a truemain"
      @click="open = true"
    >
      <UIcon
        name="i-lucide-search"
        class="size-5 shrink-0 text-dimmed transition-colors group-hover:text-primary"
      />
      <span class="flex-1 truncate text-sm text-dimmed">
        Search a truemain by name…
      </span>
    </button>

    <!-- Compact trigger: icon button (header). -->
    <UButton
      v-else
      icon="i-lucide-search"
      color="neutral"
      variant="ghost"
      aria-label="Search a truemain"
      @click="open = true"
    />

    <UModal
      v-model:open="open"
      title="Search a truemain"
      description="Look up a player by Riot id (Name#TAG) or a partial name"
      :ui="{ content: 'sm:max-w-lg' }"
    >
      <template #content>
        <div class="flex flex-col gap-3 p-4">
          <UInput
            v-model="term"
            autofocus
            size="lg"
            icon="i-lucide-search"
            placeholder="Name or Name#TAG…"
            :loading="status === 'pending'"
            @keydown.enter="onEnter"
          />

          <!-- Hint once typing starts but the query is still too short. -->
          <p
            v-if="hasTyped && tooShort"
            class="px-1 py-6 text-center text-sm text-muted"
          >
            Type at least {{ SEARCH_MIN_LENGTH }} characters to search.
          </p>

          <!-- Request failed. -->
          <p
            v-else-if="status === 'error'"
            class="px-1 py-6 text-center text-sm text-error"
          >
            Search failed — try again.
          </p>

          <!-- Searched, nothing matched. -->
          <p
            v-else-if="status === 'success' && results.length === 0"
            class="px-1 py-6 text-center text-sm text-muted"
          >
            No truemain matches that name.
          </p>

          <!-- Results. -->
          <ul v-else-if="displayResults.length > 0" class="flex max-h-80 flex-col gap-1 overflow-y-auto">
            <li v-for="{ result, iconUrl } in displayResults" :key="`${result.identity.gameName}|${result.identity.tagLine}|${result.identity.platformId}`">
              <NuxtLink
                :to="profilePath(result)"
                class="glass-hover flex items-center gap-3 rounded-md px-2 py-2 transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                @click="open = false"
              >
                <SkeletonImage
                  v-if="iconUrl"
                  :src="iconUrl"
                  :alt="result.identity.gameName"
                  class="size-9 shrink-0 rounded"
                  width="36"
                  height="36"
                />
                <div v-else class="size-9 shrink-0 rounded bg-elevated/60" aria-hidden="true" />

                <div class="min-w-0 flex-1">
                  <div class="flex items-baseline gap-1 truncate">
                    <span class="truncate font-semibold text-default">{{ result.identity.gameName }}</span>
                    <span v-if="result.identity.tagLine" class="shrink-0 text-xs text-muted">#{{ result.identity.tagLine }}</span>
                  </div>
                  <LeaderboardRegionFlag
                    v-if="result.region"
                    :region="result.region"
                    :width="16"
                    class="mt-0.5"
                  />
                </div>

                <div v-if="result.ranked" class="flex shrink-0 items-center gap-1.5 text-sm text-muted">
                  <RankIcon :tier="result.ranked.tier" :size="22" />
                  <span class="tabular-nums">{{ formatTier(result.ranked.tier, result.ranked.division) }}</span>
                </div>
              </NuxtLink>
            </li>
          </ul>
        </div>
      </template>
    </UModal>
  </div>
</template>
