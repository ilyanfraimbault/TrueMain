<script setup lang="ts">
// The brand's own page (#1122). Two jobs, both SEO: it is the one place that
// states in prose what TrueMain is — the text a search engine reads to resolve
// the brand name — and it is a hub linking every section of the site.
//
// Server-rendered like the rest of the static pages: no fetch, so the whole
// page is in the HTML a crawler receives on the first request.
useSeoMeta({
  title: 'About TrueMain',
  description:
    'TrueMain builds League of Legends champion statistics from the games of players who actually main the champion — one-tricks and specialists — instead of averaging everyone who ever picked it.',
})
</script>

<template>
  <main class="mx-auto max-w-3xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="About"
      title="About TrueMain"
      description="Champion data from the players who actually main the champion."
    />

    <article class="surface space-y-8 rounded-xl p-5 text-sm leading-relaxed text-muted sm:p-8">
      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          What TrueMain is
        </h2>
        <p>
          TrueMain is a free League of Legends statistics site. It shows champion
          builds, runes, skill orders, matchups and tier lists — the same
          questions every stats site answers, but computed over a deliberately
          narrower set of games.
        </p>
        <p>
          Most champion statistics average every player who locked the champion
          in: the specialist with six hundred games and the player picking it for
          the first time count the same. Those two are not playing the same
          champion. TrueMain weights the games of players who main it.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          What a "true main" is
        </h2>
        <p>
          A true main is a player whose record on a champion shows genuine
          commitment rather than a good week. TrueMain scores that commitment —
          the <span class="font-medium text-default">dedication score</span> — from
          public ranked history: how much of their play the champion accounts
          for, over how long, and at what rank. One-tricks score highest, but the
          score is a gradient, not a label.
        </p>
        <p>
          The tracked players are browsable: the
          <NuxtLink
            to="/truemains"
            class="text-primary hover:underline"
          >
            truemains leaderboard
          </NuxtLink>
          ranks them by LP or by dedication, and each champion page lists its own
          top mains.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Where the data comes from
        </h2>
        <p>
          Everything on TrueMain is derived from public data served by the
          official Riot Games API — ranked ladders, match history and match
          timelines. Nothing is hand-written, estimated or sourced from an
          external stats provider. Aggregates are recomputed as patches roll
          over, and every figure on the site carries the sample it was measured
          on, so a percentage from eleven games is never presented as if it came
          from a thousand.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          What you can do here
        </h2>
        <ul class="list-disc space-y-1 pl-5">
          <li>
            <NuxtLink
              to="/champions"
              class="text-primary hover:underline"
            >
              Champions
            </NuxtLink>
            — every champion and lane, with its consensus build, runes, win rate
            and pick rate.
          </li>
          <li>
            <NuxtLink
              to="/champions/tierlist"
              class="text-primary hover:underline"
            >
              Tier list
            </NuxtLink>
            — champions ranked within their own lane, on presence as well as win
            rate.
          </li>
          <li>
            <NuxtLink
              to="/matchup"
              class="text-primary hover:underline"
            >
              Matchup
            </NuxtLink>
            — pick your champion, your role and your opponent, and get the build
            from the games where that matchup actually happened.
          </li>
          <li>
            <NuxtLink
              to="/truemains"
              class="text-primary hover:underline"
            >
              Truemains
            </NuxtLink>
            — the tracked specialists, their ranks, their champions and their
            games.
          </li>
        </ul>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Contact
        </h2>
        <p>
          TrueMain is run independently and carries no advertising. Questions,
          corrections and removal requests go to
          <a
            href="mailto:contact@truemain.lol"
            class="text-primary hover:underline"
          >contact@truemain.lol</a>. See also the
          <NuxtLink
            to="/privacy"
            class="text-primary hover:underline"
          >
            privacy policy
          </NuxtLink>
          and the
          <NuxtLink
            to="/terms"
            class="text-primary hover:underline"
          >
            terms
          </NuxtLink>.
        </p>
      </section>

      <section class="space-y-3 border-t border-default pt-6 text-xs text-dimmed">
        <p>
          TrueMain isn't endorsed by Riot Games and doesn't reflect the views or
          opinions of Riot Games or anyone officially involved in producing or
          managing Riot Games properties. Riot Games and all associated properties
          are trademarks or registered trademarks of Riot Games, Inc.
        </p>
      </section>
    </article>
  </main>
</template>
