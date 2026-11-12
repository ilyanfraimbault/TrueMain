<script setup lang="ts">
import type { DesignHomeData } from '~/composables/useDesign'
import { championDisplayName } from '~~/shared/utils/champion-name'

/**
 * Design exploration, variant C — "Cinematic". The home page as a short film
 * told on the scroll: an opening shot on the eclipse, a manifesto that lights
 * up word by word, credits rolling the champions' names, then three chapters
 * (the patch, the mains, the desktop app) and a closing line.
 */
const props = defineProps<DesignHomeData>()

// The marquee's cast: the patch's most present champions, then the mains'
// champions, each once — real names, in a real order.
const cast = computed(() => {
  const ids: number[] = []
  for (const row of props.overview?.topRows ?? []) if (!ids.includes(row.championId)) ids.push(row.championId)
  for (const row of props.truemainRows) {
    const id = row.topChampions[0]?.championId
    if (id !== undefined && !ids.includes(id)) ids.push(id)
  }
  if (props.championsById.size === 0) return []
  return ids.map(id => ({
    id,
    name: championDisplayName(id, props.championsById.get(id)),
    iconUrl: props.championsById.get(id)?.iconUrl ?? '',
  }))
})
</script>

<template>
  <div class="c-home">
    <DesignCHero
      :patch-version="overview?.patchVersion ?? null"
      :overview-pending="overviewPending"
      :games-analyzed="gamesAnalyzed"
      :tracked-truemains="trackedTruemains"
    />

    <DesignCManifesto />

    <DesignCMarquee :champions="cast" />

    <DesignCGallery
      :top-rows="overview?.topRows ?? []"
      :champions-by-id="championsById"
      :pending="tierlistPending"
      :patch-version="overview?.patchVersion ?? null"
    />

    <DesignCMains
      :rows="truemainRows"
      :champions-by-id="championsById"
      :loading="truemainsInitialLoading"
      :patch="ddragonPatch"
      :tracked="trackedTruemains"
    />

    <DesignCDesktop />

    <DesignCFinale />

    <DesignCCursor />
  </div>
</template>
