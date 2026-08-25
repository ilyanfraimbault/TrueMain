/**
 * The build label the footer prints, from the two runtime-config values the
 * deploy injects (`NUXT_PUBLIC_APP_ENV` / `NUXT_PUBLIC_APP_VERSION`).
 *
 * The point of the label is to answer "which build am I looking at, and has it
 * reached prod yet?" without diffing SHAs on GitHub. Preprod builds carry a
 * prerelease version (`1.20.0-rc.4`) that names the release they are heading
 * for and how many preprod deploys in they are; prod carries the bare release
 * tag. So the environment is only worth printing when it *isn't* prod — on
 * truemain.lol the version alone is unambiguous, and a "production" badge would
 * be noise on every page.
 *
 * Returns null rather than a partial label whenever there is no version to
 * show: locally both vars are empty, and a deploy that forgot to pass
 * `APP_VERSION` should print nothing rather than a bare "preprod ·".
 */
export function formatBuildLabel(input: {
  env?: string | null
  version?: string | null
}): string | null {
  const version = input.version?.trim()
  if (!version) return null

  const env = input.env?.trim().toLowerCase()
  if (!env || env === 'production' || env === 'prod') return version

  return `${env} · ${version}`
}
