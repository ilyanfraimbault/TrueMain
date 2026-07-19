<script setup lang="ts">
useSeoMeta({
  title: 'Privacy Policy',
  description:
    'How TrueMain handles data: the public League of Legends game data it surfaces, privacy-friendly analytics, and your rights.',
})

const lastUpdated = 'July 19, 2026'
const contactEmail = 'contact@truemain.lol'
</script>

<template>
  <main class="mx-auto max-w-3xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Legal"
      title="Privacy Policy"
      :description="`Last updated ${lastUpdated}`"
    />

    <article class="glass space-y-8 rounded-xl p-5 text-sm leading-relaxed text-muted sm:p-8">
      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Who we are
        </h2>
        <p>
          TrueMain is a free, non-commercial web application that provides League
          of Legends statistics — champion builds, runes, skill orders and
          performance data derived from public match data. This policy explains
          what data we process, why, and the choices you have. It applies to
          <a
            href="https://truemain.lol"
            class="text-primary hover:underline"
          >truemain.lol</a> and its subdomains.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Information we process
        </h2>
        <p>
          <span class="font-medium text-default">Public game data.</span>
          We retrieve publicly available League of Legends data through the
          official Riot Games API — including summoner names, Riot IDs, encrypted
          account identifiers (PUUIDs), match history, ranked standings and
          champion mastery. This data is provided by Riot Games and reflects
          activity that is already public in-game and on Riot's own services. We
          aggregate it to build per-champion and per-player statistics.
        </p>
        <p>
          <span class="font-medium text-default">Analytics.</span>
          We use a self-hosted, privacy-friendly analytics tool (Umami) to
          understand aggregate site usage. It does not use tracking cookies, does
          not collect personally identifiable information, and does not build
          cross-site profiles. Data is aggregated and anonymous.
        </p>
        <p>
          <span class="font-medium text-default">Accounts.</span>
          TrueMain does not currently offer user accounts, logins or passwords. If
          we introduce account sign-in through Riot Sign-On (RSO) in the future,
          we will update this policy to describe the account data we receive and
          how it is handled before that feature goes live.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          How we use data
        </h2>
        <ul class="list-disc space-y-1 pl-5">
          <li>To compute and display champion and player statistics.</li>
          <li>To identify high-elo one-trick specialists ("true mains") from public ladder and match data.</li>
          <li>To measure aggregate site usage and improve the product.</li>
          <li>To operate, secure and maintain the service.</li>
        </ul>
        <p>
          We do not sell your data, and we do not use it for advertising.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Cookies
        </h2>
        <p>
          We keep cookies to a minimum. We do not use advertising or third-party
          tracking cookies. Any storage we use is strictly necessary to operate
          the site (for example, remembering interface preferences).
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Data retention
        </h2>
        <p>
          Aggregated statistics and the public match data they are built from are
          retained for as long as they are useful to the service, and are pruned
          on a rolling basis as patches age. Analytics data is retained in
          aggregate form only.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Your rights
        </h2>
        <p>
          Depending on your location (including under the EU GDPR), you may have
          the right to access, correct or request deletion of personal data we
          hold about you, and to object to certain processing. Because TrueMain
          surfaces public League of Legends data, you can ask us to remove your
          player profile and associated data from the site. To make a request,
          contact us at the address below.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Third parties
        </h2>
        <p>
          Game data originates from the Riot Games API and is subject to Riot's
          own terms and privacy practices. We host the site and its analytics on
          our own infrastructure. We do not share your data with third parties for
          their own marketing.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Children
        </h2>
        <p>
          TrueMain is not directed at children under the age required to hold a
          Riot Games account in their region, and we do not knowingly collect data
          from them.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Changes to this policy
        </h2>
        <p>
          We may update this policy from time to time. Material changes will be
          reflected by updating the "last updated" date at the top of this page.
        </p>
      </section>

      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-highlighted">
          Contact
        </h2>
        <p>
          Questions or requests about this policy or your data can be sent to
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
