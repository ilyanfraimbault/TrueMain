import type { InjectionKey, Ref } from 'vue'
import type { MatchDetailResponse } from '#shared/types/match-detail'

/** What an open match row reads its detail from. */
export interface MatchDetailState {
  data: Readonly<Ref<MatchDetailResponse | null | undefined>>
  isLoading: Readonly<Ref<boolean>>
  notFound: Readonly<Ref<boolean>>
}

export type MatchDetailSource = (nameTag: () => string, matchId: () => string) => MatchDetailState

/**
 * Where `MatchDetailPanel` reads a game from. Unset, it asks TrueMain
 * (`useMatchDetail`, `/truemains/{nameTag}/matches/{matchId}`); the desktop
 * dashboard provides its own reader, which tries TrueMain's copy and falls back
 * to the player's own client — the one game history TrueMain may never have
 * ingested. Provided by the page that owns the rows, so every row under it
 * reads the same way.
 */
export const MATCH_DETAIL_SOURCE: InjectionKey<MatchDetailSource> = Symbol('MatchDetailSource')
