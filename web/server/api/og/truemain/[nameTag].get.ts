import type { TruemainOgCard } from '~~/shared/types/og-card'
import type { ProfileResponse } from '~~/shared/types/profile'
import type { ChampionStaticData } from '~~/shared/types/static-data'
import { selectSignatureMain } from '~~/shared/utils/og-card'
import { riotIdLabel } from '~~/shared/utils/truemain-path'
import { RIOT_ID_MAX_LENGTH } from '~~/shared/utils/riot-id'
import { defineCachedRead, readOrAbsent, visitorAddress } from '~~/server/utils/upstream'

/**
 * Card model for `/truemains/{nameTag}`'s share image (#926).
 *
 * Same reasoning as the champion card: the profile page fetches client-only by
 * design (`useTruemainFetch` — no SSR cross-pollination between viewers), so
 * the share image resolves its own data when a crawler renders it.
 *
 * Note the asymmetry with the page: this route is a *public* rendering of a
 * public profile, exactly what the page shows to any visitor. Nothing here is
 * viewer-specific, so serving it from a shared cache is safe.
 */

const EMPTY_CARD: TruemainOgCard = {
  riotId: null,
  platformId: null,
  ranked: null,
  main: null,
  dedicationScore: null,
}

/**
 * An unknown player is the branded card, cached like any answer — the page
 * itself renders "not found" for the same input, so inventing a profile-shaped
 * preview would be worse than a generic one. A failing upstream is never cached
 * (`server/utils/upstream.ts`, #1557): that one render gets the branded card.
 */
const loadTruemainOgCard = defineCachedRead<TruemainOgCard, [string, string?]>(
  async (nameTag: string, forwardedFor?: string): Promise<TruemainOgCard> => {
    const profile = await readOrAbsent<ProfileResponse>(
      `/api/truemains/${encodeURIComponent(nameTag)}/profile`,
      { forwardedFor },
    )

    if (!profile?.identity) return EMPTY_CARD

    const { identity, ranked, dedication } = profile
    const main = selectSignatureMain(profile.mains)

    // Only fetched once a main exists — a profile with no classified champion
    // has no portrait to resolve, and the block is dropped entirely.
    const mainStatic = main
      ? await readOrAbsent<ChampionStaticData>(`/api/static/${main.championId}`, { forwardedFor })
      : null

    return {
      // A row ingested before tag lines were stored has no addressable Riot ID;
      // the bare game name is still the honest label for it.
      riotId: riotIdLabel(identity.gameName, identity.tagLine),
      platformId: identity.platformId || null,
      ranked: ranked
        ? {
            tier: ranked.tier,
            division: ranked.division,
            leaguePoints: ranked.leaguePoints,
            wins: ranked.wins,
            losses: ranked.losses,
            winRate: ranked.winRate,
          }
        : null,
      main: main
        ? {
            championId: main.championId,
            championName: mainStatic?.championName ?? null,
            championIconUrl: mainStatic?.championIconUrl ?? null,
            games: main.games,
            playRate: main.playRate,
            isOtp: main.isOtp,
          }
        : null,
      // Dedication is always *about one champion*. The unfiltered profile score
      // is computed for the player's signature champion, which is the same row
      // `selectSignatureMain` picks — but the card prints the two side by side,
      // so guard the join rather than assume it. A mismatch drops the score
      // instead of captioning it with the wrong champion.
      dedicationScore: dedication && main && dedication.championId === main.championId
        ? dedication.score
        : null,
    }
  },
  {
    maxAge: 60 * 60,
    name: 'og-truemain-card',
    getKey: (nameTag: string) => nameTag,
  },
  () => EMPTY_CARD,
)

export default defineEventHandler(async (event): Promise<TruemainOgCard> => {
  // Decoded once here and encoded once in the read: the raw param is already
  // percent-encoded, so a name with a space used to reach the API encoded twice.
  // Bounded before it becomes a cache key or an upstream call.
  const nameTag = getRouterParam(event, 'nameTag', { decode: true })?.trim() ?? ''
  if (!nameTag || nameTag.length > RIOT_ID_MAX_LENGTH) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid nameTag' })
  }
  return loadTruemainOgCard(nameTag, visitorAddress(event))
})
