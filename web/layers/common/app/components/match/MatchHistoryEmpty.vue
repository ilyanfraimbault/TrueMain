<!--
  The empty state of a match list (`MatchHistoryList`): a player TrueMain does
  not know, a filter that left nothing, or no game at all. The app's dashboard
  lists the viewer's own games, read from their client, so it speaks to them
  (`own`) rather than about a tracked player.
-->
<script setup lang="ts">
const props = defineProps<{
  /**
   * The player wasn't found at all (404 from the backend) rather than just
   * having no games — different copy, so the reader can tell whether to check
   * the spelling or wait for games.
   */
  notFound?: boolean
  /**
   * A filter is active (role, champion, queue): the empty result is likely the
   * filter combination — e.g. one carried over in the URL from another player —
   * so point at clearing it rather than implying an empty history.
   */
  filtered?: boolean
  /** The viewer's own history (the app's dashboard). */
  own?: boolean
}>()

const state = computed(() => {
  if (props.notFound) return { icon: 'i-lucide-user-round-search', title: 'Player not found', description: null }
  if (props.filtered) {
    return {
      icon: 'i-lucide-filter-x',
      title: 'No games for these filters',
      description: props.own ? 'None of your latest games match them.' : 'No tracked games for the selected filters — try clearing them.',
    }
  }
  return {
    icon: 'i-lucide-swords',
    title: 'No games yet',
    description: props.own ? 'Your next game shows up here.' : 'Their tracked match history is empty. Play a ranked game and check back later.',
  }
})
</script>

<template>
  <UEmpty :icon="state.icon" :title="state.title">
    <template #description>
      <template v-if="state.description">
        {{ state.description }}
      </template>
      <template v-else>
        Check the spelling — the Riot ID should be <code class="whitespace-nowrap">GameName-TagLine</code>.
      </template>
    </template>
  </UEmpty>
</template>
