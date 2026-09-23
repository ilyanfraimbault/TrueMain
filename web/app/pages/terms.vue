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
  <div class="mx-auto max-w-3xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Legal"
      title="Terms of Service"
      :description="`Last updated ${lastUpdated}`"
    />

    <article class="surface space-y-8 rounded-xl p-5 sm:p-8">
      <section>
        <ProseH2>
          Acceptance of terms
        </ProseH2>
        <ProseP>
          By accessing or using TrueMain (the "Service") at
          <ProseA href="https://truemain.lol">truemain.lol</ProseA>, you agree to these Terms of Service. If you do not
          agree, please do not use the Service.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          What TrueMain is
        </ProseH2>
        <ProseP>
          TrueMain is a free, non-commercial web application that provides League
          of Legends statistics — champion builds, runes, skill orders, win rates
          and matchups — derived from public game data obtained through the
          official Riot Games API. It is an independent, fan-made tool.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Free access
        </ProseH2>
        <ProseP>
          The Service is offered free of charge. We do not require payment to
          access any feature, and there is always a free tier available to
          players.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Acceptable use
        </ProseH2>
        <ProseP>You agree not to:</ProseP>
        <ProseUl>
          <ProseLi>Use the Service for any unlawful purpose or in violation of Riot Games' terms.</ProseLi>
          <ProseLi>Attempt to disrupt, overload, scrape at scale, or reverse-engineer the Service.</ProseLi>
          <ProseLi>Misrepresent the data, or use it to harass, dox or endanger other players.</ProseLi>
          <ProseLi>Circumvent access controls or rate limits.</ProseLi>
        </ProseUl>
      </section>

      <section>
        <ProseH2>
          Intellectual property
        </ProseH2>
        <ProseP>
          League of Legends and all related game content, names and imagery are
          the property of Riot Games, Inc. TrueMain's own code, design and
          original written content remain the property of their author. Game data
          is used in accordance with the Riot Games API terms of use.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          No warranty
        </ProseH2>
        <ProseP>
          The Service and its statistics are provided "as is", without warranties
          of any kind. Data is aggregated from third-party sources and may be
          incomplete, delayed or inaccurate. We make no guarantee that the Service
          will be uninterrupted, error-free or available at all times.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Limitation of liability
        </ProseH2>
        <ProseP>
          To the maximum extent permitted by law, TrueMain and its author shall
          not be liable for any indirect, incidental or consequential damages
          arising from your use of, or inability to use, the Service.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Changes to the Service and terms
        </ProseH2>
        <ProseP>
          We may modify, suspend or discontinue any part of the Service at any
          time. We may also update these terms; material changes will be reflected
          by updating the "last updated" date at the top of this page. Continued
          use after changes constitutes acceptance of the updated terms.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Governing law
        </ProseH2>
        <ProseP>
          These terms are governed by the laws of France, without regard to
          conflict-of-law principles.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Contact
        </ProseH2>
        <ProseP>
          Questions about these terms can be sent to
          <ProseA :href="`mailto:${contactEmail}`">{{ contactEmail }}</ProseA>.
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
