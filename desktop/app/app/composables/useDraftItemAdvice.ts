import type { BuildSubject } from '~/composables/useDraftBuild'
import type { NextItemRequest, NextItemResponse } from '~/utils/next-item'
import type { DraftItemPush } from '~/utils/draft-damage'
import { draftPush } from '~/utils/draft-damage'

/** Picks land in bursts at the end of a phase: wait for the board to settle before asking. */
const SETTLE_MS = 400

/**
 * The items this draft moves the mains toward (#1907): the in-game next-item
 * read (#1749) asked at draft time, with an empty inventory and the draft as
 * the game — the same axes (enemy damage mix, crowd control, sustain…), the
 * same measured model, before the first item is bought. The boots and the
 * first legendary, each only when a situation of this draft moves one up.
 */
export function useDraftItemAdvice(subject: Ref<BuildSubject | null>) {
  const pushes = ref<DraftItemPush[]>([])
  const pending = ref(false)

  let generation = 0
  let settle: ReturnType<typeof setTimeout> | undefined

  async function ask(current: BuildSubject) {
    const mine = ++generation
    const body: NextItemRequest = {
      position: current.request.position,
      items: [],
      allies: current.request.allies,
      enemies: current.request.enemies,
    }
    pending.value = true
    try {
      const answer = await apiPost<NextItemResponse>(`/champions/${current.championId}/next-item`, body, {}, { background: true })
      if (mine !== generation) return
      pushes.value = [draftPush('boots', answer.boots), draftPush('build', answer.build)].filter((push): push is DraftItemPush => push !== null)
    }
    catch {
      // Advice that does not load is advice not shown: the build below stands on its own.
      if (mine === generation) pushes.value = []
    }
    finally {
      if (mine === generation) pending.value = false
    }
  }

  watch(() => (subject.value ? JSON.stringify(subject.value) : ''), (key) => {
    clearTimeout(settle)
    const current = subject.value
    // With nobody else on the board there is no situation to read.
    if (!key || !current || current.request.enemies.length + current.request.allies.length === 0) {
      ++generation
      pushes.value = []
      pending.value = false
      return
    }
    settle = setTimeout(() => ask(current), SETTLE_MS)
  }, { immediate: true })

  onScopeDispose(() => clearTimeout(settle))

  return { pushes, pending }
}
