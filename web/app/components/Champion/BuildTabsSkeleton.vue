<script setup lang="ts">
import {
  PLACEHOLDER_BUILDS,
  PLACEHOLDER_CHAMPION_STATIC,
  PLACEHOLDER_ITEMS_MAP,
  PLACEHOLDER_RUNE_TREE,
  PLACEHOLDER_SUMMONERS_MAP,
} from '~/utils/build-placeholder'

// The loading state of the build section — `ChampionBuildTabs` itself, in
// `pending` mode, over a placeholder aggregate.
//
// It used to be a hand-drawn stack of grey blocks mirroring the real card's
// measured heights. That reserved the space but it did not look like the page:
// the panels' own loading state (real layout, every DDragon icon still pulsing
// in its slot) is a second, completely different picture, so a cold load
// visibly rebuilt itself once the API answered. Rendering the real components
// with unresolvable ids collapses the two into one continuous state — the only
// thing that changes when the data lands is that the icons and numbers fill in.
//
// It also cannot drift: move a section and the skeleton moves with it.
</script>

<template>
  <!-- `inert`, not just `aria-hidden`: the scaffolding renders real tab
       triggers, so hiding it from assistive tech while leaving it focusable and
       clickable would put a keyboard user inside a fake tablist. -->
  <div
    inert
    aria-hidden="true"
  >
    <ChampionBuildTabs
      pending
      :builds="PLACEHOLDER_BUILDS"
      :champion-static="PLACEHOLDER_CHAMPION_STATIC"
      :items-map="PLACEHOLDER_ITEMS_MAP"
      :summoners-map="PLACEHOLDER_SUMMONERS_MAP"
      summoners-pending
      :rune-tree="PLACEHOLDER_RUNE_TREE"
    />
  </div>
</template>
