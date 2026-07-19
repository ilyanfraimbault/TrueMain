export default defineAppConfig({
  ui: {
    colors: {
      // Custom palettes defined in assets/css/main.css @theme. `rosegold` is
      // the brand accent (warm rose with a gold read); `mauve` is a warm
      // near-neutral charcoal replacing the old cold zinc.
      primary: 'rosegold',
      neutral: 'mauve',
    },
    // Give every UCard the app-wide glass material (translucent, blurred
    // surface — see the `glass` utility in main.css) and trim the default
    // padding a notch. Nuxt UI *appends* per-variant `root` classes rather
    // than replacing them, so the `outline` default would keep its opaque
    // `bg-default` + `ring` and bury the glass. `soft` is the base we build on
    // (single translucent fill + divider, no ring), but its stock
    // `bg-elevated/50` is *overridden* below: as a plain utility it wins the
    // cascade over the `glass` background-color, so the glass opacity never
    // reached the card — cards rendered at 50% regardless. We raise it to /75
    // here so the panels actually separate from the animated backdrop.
    card: {
      slots: {
        root: 'glass rounded-2xl',
        header: 'p-3 sm:px-4 sm:py-3.5',
        body: 'p-3 sm:p-4',
        footer: 'p-3 sm:px-4',
      },
      variants: {
        variant: {
          soft: {
            root: 'bg-elevated/75 divide-y divide-default',
          },
        },
      },
      defaultVariants: {
        variant: 'soft',
      },
    },
  },
})
