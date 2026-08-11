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
    // `soft`'s stock `bg-elevated/50` has to be overridden here: left alone it
    // wins over `surface`'s background-color and every card renders at 50%.
    //
    // The override is the *opaque* `bg-elevated`, not `bg-transparent` — the
    // cascade cuts both ways, so a transparent utility would win just as hard
    // and leave every card in the app with no fill at all. The literal simply
    // restates the value `surface` was going to paint.
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
            root: 'bg-elevated divide-y divide-default',
          },
        },
      },
      defaultVariants: {
        variant: 'soft',
      },
    },
    // Nuxt UI paints a skeleton `bg-elevated` — which, since #1060, is the exact
    // fill of every `surface` card. A skeleton inside a card was therefore
    // invisible: same colour, no edge, nothing to see while a page loaded.
    //
    // `ink-700` rather than the next ladder step (`bg-accented`, #24242a)
    // because `animate-pulse` is `50% { opacity: .5 }` — half of every cycle the
    // fill is blended halfway back into whatever is behind it. Accented at 50%
    // over a card lands on #1f1f25 and disappears again; ink-700 at 50% lands
    // near #2a2a31, which is the contrast the static swatch only *looks* like it
    // has. Judge a skeleton colour at half opacity, not at full.
    skeleton: {
      base: 'bg-ink-700',
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
