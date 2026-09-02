# SEO, share cards and OG images

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**Share cards degrade in three steps and never print a number the API did not return.**
An OG image is read as a screenshot of the page, so a filler `0%` is indistinguishable from a
measurement — the "no fabricated numbers" rule that governs the pages binds harder here, because the
reader has no page to check it against. Champion card: full stats when a `GET /champions` row exists →
portrait + name and *no numbers at all* when it doesn't → plain branded card when even DDragon is
unreachable. A null `banRate` (a patch before #920's ingestion) drops that one tile rather than zeroing
it, matching the em dash the directory prints. Truemain card: a missing ranked snapshot reads
"Unranked" (a real answer, unlike 0 LP), absent `wins`/`losses` drop the record line *and* the win-rate
tile, no classified main drops the champion block, and an unknown player falls all the way back to the
branded card — the page renders "not found" for the same input, so a profile-shaped preview would be a
lie. The dedication score is only printed when its `championId` matches the main shown beside it — #926.

**Share cards resolve their own data server-side instead of receiving it from the page.**
`nuxt-og-image` encodes the template props into the (signed) image URL, which is minted during SSR — but
both pages fetch `server: false` (the #149 hydration fix on champions, the deliberate no-cross-viewer-SSR
rule on profiles), so at that moment the page holds no numbers to hand over. The URL therefore carries
*identifiers only* and the templates resolve the real slice through `server/api/og/**` when a crawler
renders the image. The alternative — adding an SSR-enabled fetch to the pages purely to feed the card —
would put a backend round-trip on every human page view to serve the unfurl path. Accepted consequence:
the card's numbers are resolved at share time, so they can differ from a page the visitor left open —
both are real, an hour apart at most (the cache TTL) — #926.

**The champion link graph was server-rendered, then removed — the pages are back to zero internal champion links.**
#1123 gave the champion pages content a crawler could read. It did not give them links a crawler could
*follow*: counted in the HTML prod actually served, `/` held 0 `/champions/{slug}` anchors, `/champions` 0,
`/champions/tierlist` 0 and a champion page 0 — the only `/champions/*` anchor anywhere on the site was
`/champions/tierlist`. So the 174 build pages were reachable only from `sitemap.xml`, which on a site with no
backlinks is the textbook "Discovered – currently not indexed" profile, and it showed: `truemain katarina`
returned nothing, and `/` outranked `/champions` for `truemain champions`. #1209 fixed it with three blocks
of plain text links fed by `/api/champion-index` — deliberately *not* by SSR-ing the grids, which each need
the ~20 kB static champion list (names *and* CDN icon URLs) plus, on the directory, the ~373 KiB item map,
and whose rows are a `role="button"`, not an anchor, on purpose (#147).
**Reverted in #1275, on presentation grounds.** The link graph worked — 174 anchors on `/champions`, 173 on
the tier list, 12 on the homepage, no hydration message — but the blocks were a bare `flex-wrap` of 174
muted names pinned to the bottom of four pages, including every champion page, and no work had gone into
how they read. The product owner rejected them on sight. The endpoint, the composable, the pure assembly
helpers and their tests went with the components: an endpoint with no caller is worse than no endpoint.
What this costs, recorded so it does not quietly come back as a surprise: the SEO problem #1209 measured is
**open again**, and the champion pages are once more indexable-but-unlinked. #1209's technique is sound and
is the thing to reach for when it is retried — what needs solving first is the *presentation*, not the
plumbing: an A→Z index grouped under letter headings reads as a directory, a 174-item wrap reads as
boilerplate. Anything cheaper is worse, not better — `sr-only` links are cloaking (#1123), and the
contextual cross-links the matchups and synergies panels would give were already declined in #1209 because
they are backend reads and the champion page's SSR round-trip budget is spent on the build summary.
One piece of #1255 survives the revert because it was never part of the link graph: the homepage's ⌘K hint
stays `<ClientOnly>` — see the entry below — #1209, #1275.

**A platform-dependent `UKbd` cannot be server-rendered.**
The homepage's ⌘K hint (`<UKbd value="meta">`) resolves its modifier from the platform — `⌘` on macOS, `Ctrl`
everywhere else — which the server cannot know, so it rendered an empty key against the client's `Ctrl`:
"Hydration completed but contains mismatches" on every non-Mac visit to `/`, predating #1209 and found while
verifying its acceptance. It is `<ClientOnly>` now, with **no fallback** on purpose: the hint advertises a
shortcut that does not work until the handler is mounted, so showing it earlier is a promise the page cannot
keep — #1209.

**OG image rendering is on, pinned to Satori + resvg, and deliberately reaches exactly two pages.**
It shipped disabled in #551 for one stated reason — "no dedicated share artwork yet, so the toolchain
would be build weight for no benefit". #926 supplies the artwork, which flips the benefit half but not
the cost half, so the setup stays narrow: the `.satori.vue` suffix pins the renderer (no `.browser.vue`
component exists, so playwright and a headless Chromium never enter the image), only `/champions/:id`
and `/truemains/:nameTag` call `defineOgImageComponent()`, and every render is cached for 1 h. That
matters because the renderer runs inside the web container on a VPS that has already been taken down by
one process's memory (#600) — the crawler-only traffic pattern is what keeps it cold. The **takumi**
renderer was rejected as still beta; a hand-rolled SVG→PNG route through the already-present `sharp` was
rejected because `node:*-alpine` ships no system fonts, so text would not render — Satori takes font
buffers directly, which is exactly the problem it solves — #926.

**OG image URLs are signed with a secret regenerated at every build, and that is left as the default.**
Without a secret, the encoded URL params are attacker-controllable, including the module's `html`
option — an arbitrary-HTML renderer on our own origin. Pinning a stable `NUXT_OG_IMAGE_SECRET` would
have to happen at *build* time (the module reads it in `setup()`), i.e. a Docker build arg plus a CI
secret. Accepted consequence instead: URLs minted by a previous build return 403 after a redeploy, which
only bites a re-unfurl of an old message — Discord and X have long since cached the bytes, and any fresh
unfurl re-reads the page and gets the current URL. Revisit if broken previews on old links are ever
reported — #926.

## The sitemap advertises champions, not players (2026-09-01)

**Player profiles are not in the sitemap, because their server-rendered document is empty.**
`/truemains/{nameTag}` fetches its profile client-only (`useTruemainFetch`, the #862 decision that keeps SSR
from cross-pollinating viewers), so what a crawler receives on the first pass is a skeleton: 685
`animate-pulse` elements, the same generic `TrueMain player profile.` description on every page, and the
player's name appearing exactly once, in the title. Advertising 5,000 of those hands Google 5,000
near-duplicate empty documents on a domain it knows 5 URLs of, and buries the 174 champion pages — the only
fully server-rendered content, and the only content this site can realistically rank on. They stay reachable:
`/truemains` is in the sitemap and links profiles, so a crawler can descend if it judges them worth it. A
sitemap is a priority signal, not an access gate — #1337.

**The family was specified in #551 and never once worked, so nothing was withdrawn from Google.** #551's own
verification note reads "Truemain profile URLs populate when the backend is running; it was off locally, so
that list was empty"; the page-size bug fixed in #1336 then kept the list empty in production too. Reopen the
question only if the profile page starts rendering its content server-side, not because the code once
intended to enumerate it.

**A route family that contributes no URLs warns.** Each family is fetched defensively so one upstream outage
cannot fail the whole sitemap — that part is right, and stays. What was wrong was that an empty family still
produced a valid, well-formed sitemap and said nothing, which is why the missing profiles sat in production
from the SEO foundation until someone counted the URLs — #1334.

**If a dynamic slug family ever returns: the `loc` carries the raw value, and @nuxtjs/sitemap owns the
encoding.** The app's own `to`/`href` builders correctly `encodeURIComponent` a nameTag; copying that into the
sitemap source encodes it twice, and `Álec Lightwood-Jace` gets advertised as `%25C3%2581lec%2520Lightwood-Jace`,
which the route hands to the backend as literal text — a 404. Riot IDs are full Unicode, so this hit 2,334 of
the first 5,000 profiles before the family was dropped. Encoding is per-consumer, and a `loc` is not an href.
