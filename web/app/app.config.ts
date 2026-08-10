export default defineAppConfig({
  ui: {
    colors: {
      // Custom palettes defined in assets/css/main.css @theme. `rosegold` is
      // the brand accent, reserved for brand and interaction only; `ink` is the
      // near-neutral, faintly cool charcoal every surface is built from. The
      // two are deliberately unrelated in hue — that separation is what lets a
      // scarce warm accent register at all.
      primary: 'rosegold',
      neutral: 'ink',
    },
    // Give every UCard the app-wide `surface` material (opaque fill, neutral
    // hairline — see main.css) and trim the default padding a notch.
    //
    // Nuxt UI *appends* per-variant `root` classes rather than replacing them,
    // and a plain utility out-cascades a `@utility` declaration. That is why
    // `soft`'s stock `bg-elevated/50` used to have to be overridden here: it
    // won over the glass background and rendered every card at 50%. The same
    // trap applies to `surface`, so the variant fill is neutralised to
    // `bg-transparent` and the material owns the background outright.
    card: {
      slots: {
        root: 'surface rounded-xl',
        header: 'p-3 sm:px-4 sm:py-3.5',
        body: 'p-3 sm:p-4',
        footer: 'p-3 sm:px-4',
      },
      variants: {
        variant: {
          soft: {
            root: 'bg-transparent divide-y divide-default',
          },
        },
      },
      defaultVariants: {
        variant: 'soft',
      },
    },
    // `subtle` ships a translucent `bg-{color}/10` fill + a matching
    // `ring ring-inset ring-{color}/25`, which is the right shape for a badge:
    // it tints without becoming a surface of its own, and keeps the per-color
    // (pick vs. win) distinction those variants carry. The global
    // `backdrop-blur-sm` that used to sit on top is gone with the rest of the
    // glass — there is nothing behind a badge left to blur.
    badge: {
      defaultVariants: {
        variant: 'subtle',
      },
    },
  },
})
