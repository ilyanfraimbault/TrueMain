import type { Ref } from 'vue'
import type { BuildItemSet, ChampionBuildResponse, CompositionBuildResponse } from '~/types/build'
import type { GameState } from '~/types/game'
import { compositionStarter, starterBought, starterRequest } from '~/utils/next-item'

/**
 * The starter to buy in the running game, until one is bought: the basket the
 * draft's build opens with (`POST /champions/{id}/composition-build`, the
 * endpoint behind the draft's build panel) for the ten champions on their
 * lanes, or the lane's standard build's when the matchup was never recorded.
 *
 * Asked once per game — the starter does not move with the game — and never
 * once anything but potions and the trinket is held: the app opened mid-game
 * asks for nothing.
 */
export function useStarter(game: Ref<GameState>) {
  const { champions } = useChampionStatics()
  const { items: statics } = useStaticData()

  const idByAlias = computed(() => {
    const map = new Map<string, number>()
    for (const champion of champions.value.values()) map.set(champion.alias.toLowerCase(), champion.id)
    return map
  })

  const held = computed(() => game.value.players.find(player => player.isMe)?.items.map(item => item.itemId) ?? [])
  const bought = computed(() => starterBought(held.value, statics.value))
  const request = computed(() => starterRequest(game.value, alias => idByAlias.value.get(alias.toLowerCase()) ?? null))

  const answer = ref<BuildItemSet | null>(null)

  async function laneStarter(championId: number, position: string) {
    const build = insideTauri()
      ? await (await import('@tauri-apps/api/core')).invoke<ChampionBuildResponse>('champion_build', { championId, position, opponentChampionId: null })
      : await $fetch<ChampionBuildResponse>(`/api/champions/${championId}`, { query: { position } })
    return build.builds[0]?.core.starterItems ?? null
  }

  let generation = 0
  async function ask(current: NonNullable<typeof request.value>) {
    const mine = ++generation
    try {
      const composition = await apiPost<CompositionBuildResponse>(`/champions/${current.championId}/composition-build`, current.body, {}, { background: true })
      const starter = compositionStarter(composition) ?? await laneStarter(current.championId, current.body.position)
      if (mine === generation) answer.value = starter?.itemIds.length ? starter : null
    }
    catch {
      // A starter that does not load is a starter not shown: the next item stands on its own.
      if (mine === generation) answer.value = null
    }
  }

  // Keyed on the draft, not the game state: items and gold move every reading.
  watch(
    () => (bought.value || !request.value ? '' : JSON.stringify(request.value)),
    (key, previous) => {
      if (key === previous) return
      // A starter read for another game, or another draft, must not stand in
      // while this one's is asked.
      answer.value = null
      ++generation
      if (key) ask(request.value!)
    },
    { immediate: true },
  )

  const starter = computed(() => (bought.value ? null : answer.value))

  return { starter }
}
