export default defineAppConfig({
  ui: {
    // Emerald on zinc, and **not** what the public site uses any more. This
    // note used to claim the two apps shared a primary "so they read as one
    // product"; that stopped being true when `web/` moved to rosegold, and
    // stopped being true twice over when #1060 rebuilt its whole surface system
    // on a cool ink ramp. The admin portal is deliberately left behind for now
    // — it is an internal tool, and restyling it was scoped out of the redesign
    // epic (#1059) rather than forgotten. Don't "fix" the divergence by nudging
    // one token; the portal needs the same foundations pass web/ got.
    colors: {
      primary: 'emerald',
      neutral: 'zinc',
    },
    fonts: false,
  },
})
