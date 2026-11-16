<script setup lang="ts">
/**
 * A known player's page, in the app: the site's `/players/{slug}` on the same
 * shared page (`PageKnownPlayer`) and the same read, so a known player picked
 * anywhere in the app opens here rather than in the browser. Only the frame is
 * the app's (`SharedPage`).
 */
const { slug, persona, status, error, notFound } = useKnownPlayer()
</script>

<template>
  <SharedPage eyebrow="Player" :title="persona?.displayName ?? slug">
    <PageKnownPlayer
      :slug="slug"
      :persona="persona"
      :loading="status === 'pending'"
      :not-found="notFound"
      :error="error"
    />
  </SharedPage>
</template>
