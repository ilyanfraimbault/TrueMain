<script setup lang="ts">
// The page itself is shared with the desktop app (`layers/common`); the head,
// the structured data and the answer a crawler gets are the site's. Server
// rendered: the page is public and is what a search for the person should land on.
const { slug, persona, status, error, notFound, mergedTarget, ready } = useKnownPlayer()
await ready

// A merged player's old slug, for a crawler: a permanent redirect. In the browser
// `useKnownPlayer` follows it.
if (import.meta.server && mergedTarget.value) {
  await navigateTo(mergedTarget.value, { redirectCode: 301, replace: true })
}

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
  <PageKnownPlayer
    :slug="slug"
    :persona="persona"
    :loading="status === 'pending'"
    :not-found="notFound"
    :error="error"
  />
</template>
