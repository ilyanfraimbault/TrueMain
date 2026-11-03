<script setup lang="ts">
import { DEFAULT_FAVICON, pageTitle, titleTemplate } from '~/utils/page-title'

const colorMode = useColorMode()

const color = computed(() => (colorMode.value === 'dark' ? '#18181b' : 'white'))

useHead({
  meta: [
    { charset: 'utf-8' },
    { name: 'viewport', content: 'width=device-width, initial-scale=1' },
    { key: 'theme-color', name: 'theme-color', content: color },
  ],
  // Keyed so the health pages' verdict variant (`useVerdictTitle`) replaces it.
  link: [{ key: 'icon', rel: 'icon', href: DEFAULT_FAVICON }],
  htmlAttrs: {
    lang: 'en',
  },
})

// One title per page and per tab (#2296), named from the sidebar's own table.
const route = useRoute()
useHead({
  titleTemplate,
  title: computed(() => pageTitle(route.path, route.query)),
})

useSeoMeta({
  description: 'Operations dashboard for the TrueMain platform.',
})
</script>

<template>
  <UApp>
    <NuxtLoadingIndicator />

    <NuxtLayout>
      <NuxtPage />
    </NuxtLayout>
  </UApp>
</template>
