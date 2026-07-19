<script setup lang="ts">
useSeoMeta({
  title: 'Terms of Service',
  description:
    'The terms that govern your use of TrueMain, a free, non-commercial League of Legends statistics site.',
})

const lastUpdated = 'July 19, 2026'
const contactEmail = 'contact@truemain.lol'
</script>

<template>
  <main class="mx-auto max-w-3xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Legal"
      title="Terms of Service"
      :description="`Last updated ${lastUpdated}`"
    />

    <article class="glass space-y-8 rounded-xl p-5 text-sm leading-relaxed text-muted sm:p-8">
      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Acceptance of terms
        </h2>
        <p>
          By accessing or using TrueMain (the "Service") at
          <a
            href="https://truemain.lol"
            class="text-primary hover:underline"
          >truemain.lol</a>, you agree to these Terms of Service. If you do not
          agree, please do not use the Service.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          What TrueMain is
        </h2>
        <p>
          TrueMain is a free, non-commercial web application that provides League
          of Legends statistics — champion builds, runes, skill orders, win rates
          and matchups — derived from public game data obtained through the
          official Riot Games API. It is an independent, fan-made tool.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Free access
        </h2>
        <p>
          The Service is offered free of charge. We do not require payment to
          access any feature, and there is always a free tier available to
          players.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Acceptable use
        </h2>
        <p>You agree not to:</p>
        <ul class="list-disc space-y-1 pl-5">
          <li>Use the Service for any unlawful purpose or in violation of Riot Games' terms.</li>
          <li>Attempt to disrupt, overload, scrape at scale, or reverse-engineer the Service.</li>
          <li>Misrepresent the data, or use it to harass, dox or endanger other players.</li>
          <li>Circumvent access controls or rate limits.</li>
        </ul>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Intellectual property
        </h2>
        <p>
          League of Legends and all related game content, names and imagery are
          the property of Riot Games, Inc. TrueMain's own code, design and
          original written content remain the property of their author. Game data
          is used in accordance with the Riot Games API terms of use.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          No warranty
        </h2>
        <p>
          The Service and its statistics are provided "as is", without warranties
          of any kind. Data is aggregated from third-party sources and may be
          incomplete, delayed or inaccurate. We make no guarantee that the Service
          will be uninterrupted, error-free or available at all times.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Limitation of liability
        </h2>
        <p>
          To the maximum extent permitted by law, TrueMain and its author shall
          not be liable for any indirect, incidental or consequential damages
          arising from your use of, or inability to use, the Service.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Changes to the Service and terms
        </h2>
        <p>
          We may modify, suspend or discontinue any part of the Service at any
          time. We may also update these terms; material changes will be reflected
          by updating the "last updated" date at the top of this page. Continued
          use after changes constitutes acceptance of the updated terms.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Governing law
        </h2>
        <p>
          These terms are governed by the laws of France, without regard to
          conflict-of-law principles.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Contact
        </h2>
        <p>
          Questions about these terms can be sent to
          <a
            :href="`mailto:${contactEmail}`"
            class="text-primary hover:underline"
          >{{ contactEmail }}</a>.
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
