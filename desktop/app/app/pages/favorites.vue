<!-- The site's favorites page, the same component (`web/layers/common`,
     #1732): only the frame is the app's (`SharedPage`), and the app's own
     kind, the favourite clips (#2080), in the page's `extra` slot. -->
<script setup lang="ts">
import { favouriteClips } from '~/utils/recording-library'

const { library } = useRecordings()
const clipCount = computed(() => favouriteClips(library.value).length)
</script>

<template>
  <SharedPage eyebrow="Truemains" title="Favorites">
    <PageFavorites :extra-count="clipCount">
      <template #extra>
        <FavoritesClips />
      </template>
    </PageFavorites>
  </SharedPage>
</template>
