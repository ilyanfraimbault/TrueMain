import type { ProfileIdentity } from '#shared/types/profile'
import type { BuildOption, CompositionBuildRequest } from '~/types/build'
import type { Lane } from '~/types/draft'
import { LANE_LABELS } from '~/types/draft'

/**
 * A true main's own build on the champion, for `BuildView`: the main clicked in
 * its list, and the build to show for them — in a draft, their build against it,
 * read from their games alone (#1987), or their usual one when they have no game
 * of it.
 */
export function useMainBuildOption(options: {
  championId: Ref<number>
  position: Ref<string>
  /** The draft's request, when the view is in one. */
  draftRequest: Ref<CompositionBuildRequest | null>
  /** The row on screen: a build's key, or `main` for the true main picked in the list. */
  selected: Ref<string | null>
  /** The true main picked in the list. */
  main: Ref<{ identity: ProfileIdentity } | null>
}) {
  const { championId, position, draftRequest, selected, main } = options

  const truemainBuilds = useTruemainBuild()
  const mainNameTag = computed(() => (main.value ? favoriteNameTag(main.value.identity.gameName, main.value.identity.tagLine) : null))
  const onMain = computed(() => selected.value === 'main' && mainNameTag.value !== null)

  function selectMain(row: { identity: ProfileIdentity }) {
    main.value = row
    selected.value = 'main'
    truemainBuilds.load(mainNameTag.value!, championId.value, position.value)
  }

  /** Their builds on the champion: `undefined` while asked, `null` when they have none. */
  const mainAnswer = computed(() => (onMain.value ? truemainBuilds.answerOf(mainNameTag.value!, championId.value, position.value) : undefined))
  const mainPending = computed(() => onMain.value && truemainBuilds.isPending(mainNameTag.value!, championId.value, position.value))
  const mainFailed = computed(() => onMain.value && truemainBuilds.hasFailed(mainNameTag.value!, championId.value, position.value))

  // In a draft, their build against it (#1987), asked again when the draft moves under them.
  const truemainDraftBuilds = useTruemainDraftBuild()
  watch([onMain, mainNameTag, () => JSON.stringify(draftRequest.value)], () => {
    if (onMain.value && draftRequest.value) truemainDraftBuilds.load(mainNameTag.value!, championId.value, draftRequest.value)
  }, { immediate: true })

  /** Their draft build: `undefined` while asked, `null` outside a draft or when they have none (a failed ask too). */
  const mainDraft = computed(() => {
    const request = draftRequest.value
    if (!onMain.value || !request || truemainDraftBuilds.hasFailed(mainNameTag.value!, championId.value, request)) return null
    return truemainDraftBuilds.answerOf(mainNameTag.value!, championId.value, request)
  })

  /** Their build against this draft, else their most played build, the one their page opens on. */
  const mainOption = computed<BuildOption | null>(() => {
    const draft = mainDraft.value
    if (draft === undefined) return null
    if (draft) {
      return {
        key: 'main',
        core: draft.core,
        firstItemId: draft.firstItemId,
        keystoneId: draft.core.runePage?.primaryKeystoneId ?? null,
        buildTree: draft.buildTree,
        games: draft.games,
        winRate: draft.games > 0 ? draft.wins / draft.games : null,
      }
    }
    const build = mainAnswer.value?.builds[0]
    if (!build) return null
    return {
      key: 'main',
      core: build.core,
      firstItemId: build.firstItemId,
      keystoneId: build.primaryKeystoneId,
      buildTree: build.buildTree,
      games: build.games,
      winRate: build.winRate,
    }
  })

  /** Where their build was read: this draft, or a patch and a lane when it is not the one asked for. */
  const mainScope = computed(() => {
    if (mainDraft.value) return 'against this draft'
    const answer = mainAnswer.value
    if (!answer) return null
    const lane = answer.position && answer.position !== position.value ? LANE_LABELS[answer.position as Lane] ?? answer.position : null
    const elsewhere = draftRequest.value ? 'no game against this draft' : null
    return [answer.patch ? `Patch ${answer.patch}` : null, lane ? `on ${lane}` : null, elsewhere].filter(Boolean).join(' · ')
  })

  return { mainNameTag, onMain, selectMain, mainPending, mainFailed, mainDraft, mainOption, mainScope }
}
