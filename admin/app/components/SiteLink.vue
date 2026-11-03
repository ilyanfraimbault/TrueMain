<script setup lang="ts">
// "Open on the site" (#2291): one small icon button opening a page of the public
// site in a new tab, so checking what a visitor sees takes one click instead of a
// retyped slug. Hidden when the admin has no site URL (`NUXT_PUBLIC_SITE_URL`) or
// there is no page to open. `@click.stop` so it can sit inside a clickable row.
import { computed } from 'vue'
import { siteHref } from '~~/shared/utils/site-link'

const props = withDefaults(defineProps<{
  /** The site path, e.g. `/players/faker`; null hides the button. */
  path: string | null | undefined
  /** What the tooltip and the accessible name say. */
  label?: string
}>(), {
  label: 'Open on the site',
})

const config = useRuntimeConfig()
const href = computed(() => siteHref(config.public.siteUrl, props.path))
</script>

<template>
  <UTooltip v-if="href" :text="label">
    <UButton
      data-site-link
      :href="href"
      target="_blank"
      rel="noopener noreferrer"
      icon="i-lucide-external-link"
      size="xs"
      color="neutral"
      variant="ghost"
      :aria-label="label"
      @click.stop
    />
  </UTooltip>
</template>
