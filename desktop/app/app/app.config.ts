export default defineAppConfig({
  ui: {
    colors: {
      // The site's palette, declared in assets/css/main.css: `rosegold` is the
      // one accent, `ink` the charcoal every surface is built from.
      primary: 'rosegold',
      neutral: 'ink',
    },
    // The site's card material and padding (`web/app/app.config.ts`): `soft`
    // restated as the opaque `bg-elevated`, or its stock `/50` out-cascades
    // `surface` and every card renders half transparent.
    card: {
      slots: {
        root: 'surface rounded-xl',
        header: 'px-4 py-3',
        body: 'p-4',
        footer: 'px-4 py-3',
      },
      variants: {
        variant: {
          soft: {
            root: 'bg-elevated divide-y divide-default',
          },
        },
      },
      defaultVariants: {
        variant: 'soft',
      },
    },
    // Same reason as the site: `bg-elevated` is the card fill, so a skeleton
    // inside a card needs a step it can be seen against.
    skeleton: {
      base: 'bg-ink-700',
    },
    badge: {
      defaultVariants: {
        variant: 'subtle',
      },
    },
  },
})
