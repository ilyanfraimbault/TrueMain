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
  <div class="mx-auto max-w-3xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="About"
      title="About TrueMain"
      description="Champion data from the players who actually main the champion."
    />

    <article class="surface space-y-8 rounded-xl p-5 sm:p-8">
      <section>
        <ProseH2>
          What TrueMain is
        </ProseH2>
        <ProseP>
          TrueMain is a free League of Legends statistics site. It shows champion
          builds, runes, skill orders, matchups and tier lists — the same
          questions every stats site answers, but computed over a deliberately
          narrower set of games.
        </ProseP>
        <ProseP>
          Most champion statistics average every player who locked the champion
          in: the specialist with six hundred games and the player picking it for
          the first time count the same. Those two are not playing the same
          champion. TrueMain weights the games of players who main it.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          What a "true main" is
        </ProseH2>
        <ProseP>
          A true main is a player whose record on a champion shows genuine
          commitment rather than a good week. TrueMain scores that commitment —
          the <ProseStrong>dedication score</ProseStrong> — from
          public ranked history: how much of their play the champion accounts
          for, over how long, and at what rank. One-tricks score highest, but the
          score is a gradient, not a label.
        </ProseP>
        <ProseP>
          The tracked players are browsable: the
          <ProseA href="/truemains">
            truemains leaderboard
          </ProseA>
          ranks them by LP or by dedication, and each champion page lists its own
          top mains.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Where the data comes from
        </ProseH2>
        <ProseP>
          Everything on TrueMain is derived from public data served by the
          official Riot Games API — ranked ladders, match history and match
          timelines. Nothing is hand-written, estimated or sourced from an
          external stats provider. Aggregates are recomputed as patches roll
          over, and every figure on the site carries the sample it was measured
          on, so a percentage from eleven games is never presented as if it came
          from a thousand.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          What you can do here
        </ProseH2>
        <ProseUl>
          <ProseLi>
            <ProseA href="/champions">
              Champions
            </ProseA>
            — every champion and lane, with its consensus build, runes, win rate
            and pick rate.
          </ProseLi>
          <ProseLi>
            <ProseA href="/champions/tierlist">
              Tier list
            </ProseA>
            — champions ranked within their own lane, on presence as well as win
            rate.
          </ProseLi>
          <ProseLi>
            <ProseA href="/matchup">
              Matchup
            </ProseA>
            — pick your champion, your role and your opponent, and get the build
            from the games where that matchup actually happened.
          </ProseLi>
          <ProseLi>
            <ProseA href="/truemains">
              Truemains
            </ProseA>
            — the tracked specialists, their ranks, their champions and their
            games.
          </ProseLi>
        </ProseUl>
      </section>

      <section>
        <ProseH2>
          Contact
        </ProseH2>
        <ProseP>
          TrueMain is run independently and carries no advertising. Questions,
          corrections and removal requests go to
          <ProseA href="mailto:contact@truemain.lol">contact@truemain.lol</ProseA>. See also the
          <ProseA href="/privacy">
            privacy policy
          </ProseA>
          and the
          <ProseA href="/terms">
            terms
          </ProseA>.
        </ProseP>
      </section>

      <section class="border-t border-default pt-6">
        <ProseP class="text-xs text-dimmed">
          TrueMain isn't endorsed by Riot Games and doesn't reflect the views or
          opinions of Riot Games or anyone officially involved in producing or
          managing Riot Games properties. Riot Games and all associated properties
          are trademarks or registered trademarks of Riot Games, Inc.
        </ProseP>
      </section>
    </article>
  </div>
</template>
