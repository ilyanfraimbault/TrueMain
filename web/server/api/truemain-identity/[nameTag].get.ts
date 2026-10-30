import type { ProfileResponse } from '~~/shared/types/profile'
import type { TruemainIdentity } from '~~/shared/types/truemain-identity'
import { truemainNameTag } from '~~/shared/utils/truemain-path'

/**
 * Who a `/truemains/{nameTag}` URL names, resolved on the server (#2335, #2336).
 *
 * The profile page fetches everything client-only (`useTruemainFetch`, #862), so
 * its server render knew nothing about the account. Two things a crawler reads
 * from that render need the answer:
 *
 * - **the status code** — an unknown Riot ID was served `200` with a "not found"
 *   state drawn in the browser, which Google files as a soft 404. This route says
 *   `404` for it, and the page forwards that status;
 * - **the canonical URL** — `nuxt-seo-utils` lowercases canonicals by default, so
 *   `/truemains/Known%20Ghoster-False` declared `/truemains/known%20ghoster-false`,
 *   a spelling no link uses and Google refused. The canonical is now the slug of
 *   the Riot ID as TrueMain stores it, the one every profile link is built from.
 *
 * Only the slug leaves this route: the profile itself stays client-only. It is a
 * public fact about a public profile — the OG card (`/api/og/truemain`) already
 * resolves the whole profile server-side — so a shared cache keyed on the slug is
 * safe.
 *
 * **Only an answer is cached.** Found and unknown are both the account's state;
 * a 429, a 5xx or an unreachable API is the moment's, so it fails the loader,
 * which stores nothing, and this route answers `502`. The page reads that as "no
 * verdict": it keeps a `200` and the slug from the URL, never a `404` — an outage
 * must not tell a crawler that every profile is gone.
 */

/**
 * Short enough that an account the seeder just added stops answering `404` within
 * minutes, long enough to absorb a crawler walking one profile's sub-pages.
 */
const IDENTITY_CACHE_SECONDS = 10 * 60

// Same bound as the OG card's route, for the same reason: this route is public,
// and each distinct slug is a cache entry plus a backend call.
const MAX_NAME_TAG_LENGTH = 80

type IdentityLookup = { found: true, identity: TruemainIdentity } | { found: false }

/** The status of a failed upstream call, when it got as far as an HTTP answer. */
function upstreamStatus(error: unknown): number | undefined {
  const failure = error as { statusCode?: unknown, response?: { status?: unknown } } | null
  const status = failure?.statusCode ?? failure?.response?.status
  return typeof status === 'number' ? status : undefined
}

const lookupTruemainIdentity = defineCachedFunction<IdentityLookup, [string, string?]>(
  async (nameTag: string, forwardedFor?: string): Promise<IdentityLookup> => {
    const headers = forwardedFor ? { 'x-forwarded-for': forwardedFor } : undefined
    try {
      const profile = await $fetch<ProfileResponse>(`/api/truemains/${encodeURIComponent(nameTag)}/profile`, { headers })
      if (!profile?.identity?.gameName) return { found: false }
      return {
        found: true,
        identity: { nameTag: truemainNameTag(profile.identity.gameName, profile.identity.tagLine) },
      }
    }
    catch (error) {
      if (upstreamStatus(error) === 404) return { found: false }
      throw error
    }
  },
  {
    maxAge: IDENTITY_CACHE_SECONDS,
    name: 'truemain-identity',
    getKey: (nameTag: string) => nameTag,
  },
)

export default defineEventHandler(async (event): Promise<TruemainIdentity> => {
  const nameTag = getRouterParam(event, 'nameTag', { decode: true })?.trim() ?? ''
  if (!nameTag || nameTag.length > MAX_NAME_TAG_LENGTH) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid nameTag' })
  }

  let lookup: IdentityLookup
  try {
    lookup = await lookupTruemainIdentity(nameTag, getRequestHeader(event, 'x-forwarded-for'))
  }
  catch {
    throw createError({ statusCode: 502, statusMessage: 'Profile lookup unavailable' })
  }

  if (!lookup.found) {
    throw createError({ statusCode: 404, statusMessage: 'Unknown truemain' })
  }
  return lookup.identity
})
