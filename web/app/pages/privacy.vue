<script setup lang="ts">
useSeoMeta({
  title: 'Privacy Policy',
  description:
    'How TrueMain handles data: the public League of Legends game data it surfaces, privacy-friendly analytics, and your rights.',
})

const lastUpdated = 'October 18, 2026'
const contactEmail = 'contact@truemain.lol'
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Legal"
      title="Privacy Policy"
      :description="`Last updated ${lastUpdated}`"
    />

    <article class="surface space-y-8 rounded-xl p-5 sm:p-8">
      <section>
        <ProseH2>
          Who we are
        </ProseH2>
        <ProseP>
          TrueMain is a free, non-commercial web application that provides League
          of Legends statistics — champion builds, runes, skill orders and
          performance data derived from public match data. This policy explains
          what data we process, why, and the choices you have. It applies to
          <ProseA href="https://truemain.lol">truemain.lol</ProseA> and its subdomains.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Information we process
        </ProseH2>
        <ProseP>
          <ProseStrong>Public game data.</ProseStrong>
          We retrieve publicly available League of Legends data through the
          official Riot Games API — including summoner names, Riot IDs, encrypted
          account identifiers (PUUIDs), match history, ranked standings and
          champion mastery. This data is provided by Riot Games and reflects
          activity that is already public in-game and on Riot's own services. We
          aggregate it to build per-champion and per-player statistics.
        </ProseP>
        <ProseP>
          <ProseStrong>Audience measurement.</ProseStrong>
          We use a self-hosted analytics tool (Umami), for the sole purpose of
          measuring how the site is used: pages viewed, the site you came from,
          your browser, operating system, device type, screen size and
          language, and your country,
          region and city derived from your IP address. It sets no cookie and
          stores no identifier on your device. To count unique visitors, it
          derives a temporary code from your IP address and browser, combined
          with a value specific to this site that changes every month; your IP
          address itself is not stored. We do not record sessions, do not
          follow individual visitors, and do not track you on other sites. The
          data stays on our own servers and only serves to produce statistics.
          You can opt out at any time with the control below.
        </ProseP>
        <AnalyticsOptOut />
        <ProseP>
          <ProseStrong>Accounts.</ProseStrong>
          TrueMain does not currently offer user accounts, logins or passwords. If
          we introduce account sign-in through Riot Sign-On (RSO) in the future,
          we will update this policy to describe the account data we receive and
          how it is handled before that feature goes live.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          How we use data
        </ProseH2>
        <ProseUl>
          <ProseLi>To compute and display champion and player statistics.</ProseLi>
          <ProseLi>To identify high-elo one-trick specialists ("true mains") from public ladder and match data.</ProseLi>
          <ProseLi>To measure aggregate site usage and improve the product.</ProseLi>
          <ProseLi>To operate, secure and maintain the service.</ProseLi>
        </ProseUl>
        <ProseP>
          We do not sell your data, and we do not use it for advertising.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Cookies
        </ProseH2>
        <ProseP>
          We keep cookies to a minimum. We do not use advertising or third-party
          tracking cookies. Any storage we use is strictly necessary to operate
          the site (for example, remembering interface preferences, or your
          choice to opt out of audience measurement).
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Data retention
        </ProseH2>
        <ProseP>
          Aggregated statistics and the public match data they are built from are
          retained for as long as they are useful to the service, and are pruned
          on a rolling basis as patches age. Audience measurement records hold
          no IP address and no identifier stored on your device, and are used
          only to produce aggregate statistics.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Your rights
        </ProseH2>
        <ProseP>
          Depending on your location (including under the EU GDPR), you may have
          the right to access, correct or request deletion of personal data we
          hold about you, and to object to certain processing. Because TrueMain
          surfaces public League of Legends data, you can ask us to remove your
          player profile and associated data from the site. To make a request,
          contact us at the address below.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Third parties
        </ProseH2>
        <ProseP>
          Game data originates from the Riot Games API and is subject to Riot's
          own terms and privacy practices. We host the site and its analytics on
          our own infrastructure. We do not share your data with third parties for
          their own marketing.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Children
        </ProseH2>
        <ProseP>
          TrueMain is not directed at children under the age required to hold a
          Riot Games account in their region, and we do not knowingly collect data
          from them.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Changes to this policy
        </ProseH2>
        <ProseP>
          We may update this policy from time to time. Material changes will be
          reflected by updating the "last updated" date at the top of this page.
        </ProseP>
      </section>

      <section>
        <ProseH2>
          Contact
        </ProseH2>
        <ProseP>
          Questions or requests about this policy or your data can be sent to
          <ProseA :href="`mailto:${contactEmail}`">{{ contactEmail }}</ProseA>.
        </ProseP>
      </section>
    </article>
  </div>
</template>
