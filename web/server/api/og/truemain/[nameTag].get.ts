import type { TruemainOgCard } from '~~/shared/types/og-card'
import type { ProfileResponse } from '~~/shared/types/profile'
import type { ChampionStaticData } from '~~/shared/types/static-data'
import { selectSignatureMain } from '~~/shared/utils/og-card'

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

// The `[nameTag]` route param is the `{gameName}-{tagLine}` slug the app links
// with, passed opaque to the backend (which resolves it). A Riot ID is at most
// 16 + 1 + 5 characters, so this is deliberate headroom for percent-encoding
// rather than the Riot limit itself. The point is only to bound the input
// before it can become a cache key or an upstream call, this route being
// publicly reachable.
const MAX_NAME_TAG_LENGTH = 80

const EMPTY_CARD: TruemainOgCard = {
  riotId: null,
  platformId: null,
  ranked: null,
  main: null,
  dedicationScore: null,
}

const loadTruemainOgCard = defineCachedFunction(
  async (nameTag: string): Promise<TruemainOgCard> => {
    // `ignoreResponseError` mirrors useTruemainProfile: an unknown player is a
    // 404 with a null-ish body, not an exception. Either way we fall through to
    // the branded card — the page itself renders "not found" for the same input,
    // so inventing a profile-shaped preview would be worse than a generic one.
    const profile = await $fetch<ProfileResponse | null>(
      `/api/truemains/${encodeURIComponent(nameTag)}/profile`,
      { ignoreResponseError: true },
    ).catch(() => null)

    if (!profile?.identity) return EMPTY_CARD

    const { identity, ranked, dedication } = profile
    const main = selectSignatureMain(profile.mains)

    // Only fetched once a main exists — a profile with no classified champion
    // has no portrait to resolve, and the block is dropped entirely.
    const mainStatic = main
      ? await $fetch<ChampionStaticData>(`/api/static/${main.championId}`).catch(() => null)
      : null

    return {
      // A row ingested before tag lines were stored has no addressable Riot ID;
      // the bare game name is still the honest label for it.
      riotId: identity.tagLine ? `${identity.gameName}#${identity.tagLine}` : identity.gameName,
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
)

export default defineEventHandler(async (event): Promise<TruemainOgCard> => {
  const nameTag = getRouterParam(event, 'nameTag') ?? ''
  if (!nameTag || nameTag.length > MAX_NAME_TAG_LENGTH) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid nameTag' })
  }
  return loadTruemainOgCard(nameTag)
})
