<script setup lang="ts">
import type { PersonaResponse } from '~~/shared/types/players'
import { parseRouteParam } from '#common/utils/route-params'

// A known player's page (#2016): the person — a pro, a streamer, a creator or
// any notable player — their public channels, and every account of theirs
// TrueMain tracks, the one played most recently first, each leading back to the
// regular profile. Accounts sources list but TrueMain does not track are not
// shown: there is no profile to open. Server rendered: the page is public and
// is what a search for the person should land on.
const route = useRoute()
const slug = computed(() => parseRouteParam(route.params.slug).toLowerCase())

const apiFetch = useApiFetch()
const { data: persona, status, error } = await useAsyncData<PersonaResponse | null>(
  () => `player-${slug.value}`,
  async (_nuxtApp, { signal }) => {
    const response = await apiFetch<PersonaResponse | null>(
      `/players/${encodeURIComponent(slug.value)}`,
      { ignoreResponseError: true, signal },
    )
    // A 404 resolves to an empty body: anything without a name is "unknown".
    return response && typeof response === 'object' && response.displayName ? response : null
  },
  { watch: [slug] },
)

const notFound = computed(() => status.value === 'success' && !persona.value)

// A known player an operator merged into another one (#2253) answers under the kept
// player's slug: send the visitor there, and a crawler with a permanent redirect.
const mergedTarget = computed(() => mergedPlayerPath(slug.value, persona.value))
if (mergedTarget.value) {
  await navigateTo(mergedTarget.value, { redirectCode: 301, replace: true })
}
watch(mergedTarget, (target) => {
  if (target) {
    navigateTo(target, { replace: true })
  }
})

const { data: versions } = useDDragonVersions()
const patch = computed(() => versions.value?.[0] ?? null)
const { data: championsData } = useChampionStaticList()
const championsById = computed(() => new Map((championsData.value ?? []).map(c => [c.championId, c])))

const breadcrumbItems = computed(() => [
  { label: 'Truemains', to: '/truemains' },
  { label: persona.value?.displayName ?? slug.value },
])

useSeoMeta({
  title: () => persona.value ? `${persona.value.displayName} · Accounts` : 'Player',
  description: () => persona.value
    ? `Every League of Legends account of ${persona.value.displayName} — Riot IDs, ranks and mains.`
    : 'A known League of Legends player on TrueMain.',
})

// What the page is, for a crawler (#2263): a profile of one person, below the
// leaderboard. The person carries their public channels as `sameAs`, so the
// page reads as a deep profile of someone, not as a section of the site. Its
// `@id` is explicit: the module's default for a `Person` is the site identity's
// id, which would collide with the TrueMain `Organization` node.
// Whole nodes as computeds: the definers' getters only take scalar values, and
// `sameAs` is a list.
const personId = computed(() => `#/schema/person/${slug.value}`)
useSchemaOrg([
  definePerson(computed(() => ({
    '@id': personId.value,
    'name': persona.value?.displayName ?? slug.value,
    'sameAs': persona.value?.links.map(link => link.url) ?? [],
  }))),
  defineWebPage(computed(() => ({
    '@type': ['WebPage', 'ProfilePage'] as Array<'WebPage' | 'ProfilePage'>,
    'name': persona.value?.displayName ?? 'Player',
    'mainEntity': { '@id': personId.value },
  }))),
  defineBreadcrumb({
    itemListElement: [
      { name: 'Truemains', item: '/truemains' },
      { name: () => persona.value?.displayName ?? slug.value },
    ],
  }),
])

if (import.meta.server && notFound.value) {
  setResponseStatus(useRequestEvent()!, 404)
}
</script>

<template>
  <div class="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 md:p-6">
    <UBreadcrumb :items="breadcrumbItems" />

    <FetchErrorAlert
      :error="error"
      title="This player could not be loaded"
    />

    <UEmpty
      v-if="notFound"
      icon="i-lucide-user-x"
      title="Unknown player"
      description="No known player answers to this name."
      :actions="[{ label: 'Back to the truemains', to: '/truemains', color: 'neutral', variant: 'subtle' }]"
    />

    <template v-else-if="persona">
      <!-- The person first: their picture, their name, and their channels on
           the name's line. The picture is a placeholder slot until persona
           images are collected (#2064). -->
      <header class="flex items-center gap-4">
        <UAvatar
          :alt="persona.displayName"
          icon="i-lucide-user-round"
          :ui="{ root: 'size-20 shrink-0 rounded-xl bg-elevated ring-1 ring-default text-2xl', icon: 'size-9 text-dimmed' }"
        />
        <div class="min-w-0 flex-1">
          <p class="eyebrow">
            Player
          </p>
          <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
            <h1 class="truncate text-2xl font-semibold tracking-tight text-highlighted sm:text-3xl">
              {{ persona.displayName }}
            </h1>
            <PlayersPlayerLinks :links="persona.links" />
          </div>
        </div>
      </header>

      <section class="flex flex-col gap-2">
        <h2 class="eyebrow">
          Accounts
        </h2>
        <ul class="flex flex-col gap-1">
          <PlayersPlayerAccountRow
            v-for="account in persona.accounts"
            :key="`${account.platformId}:${account.gameName}#${account.tagLine}`"
            :account="account"
            :champions-by-id="championsById"
            :patch="patch"
          />
        </ul>
      </section>
    </template>

    <div
      v-else-if="status === 'pending'"
      class="flex flex-col gap-2"
    >
      <USkeleton class="h-9 w-64" />
      <USkeleton
        v-for="index in 3"
        :key="index"
        class="h-14 rounded-lg"
      />
    </div>
  </div>
</template>
