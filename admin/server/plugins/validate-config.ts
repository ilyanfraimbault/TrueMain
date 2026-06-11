/**
 * Fail-fast configuration guard. The admin dashboard ships with `truemain`
 * defaults so local development works with zero setup, but those defaults must
 * never reach a deployed environment: the authenticated session proxies the
 * privileged ops API key. Outside dev we therefore refuse to boot unless real
 * credentials and a strong session secret have been provided, turning a silent
 * "anyone can log in as truemain/truemain" into a hard startup failure.
 */
const INSECURE_DEFAULT = 'truemain'
const MIN_SESSION_PASSWORD_LENGTH = 32

export default defineNitroPlugin(() => {
  if (import.meta.dev) {
    return
  }

  const config = useRuntimeConfig()
  const problems: string[] = []

  if (!config.adminUsername || config.adminUsername === INSECURE_DEFAULT) {
    problems.push('NUXT_ADMIN_USERNAME is unset or still the insecure `truemain` default')
  }
  if (!config.adminPassword || config.adminPassword === INSECURE_DEFAULT) {
    problems.push('NUXT_ADMIN_PASSWORD is unset or still the insecure `truemain` default')
  }
  if (!config.session?.password || config.session.password.length < MIN_SESSION_PASSWORD_LENGTH) {
    problems.push(`NUXT_SESSION_PASSWORD must be set to a random secret of at least ${MIN_SESSION_PASSWORD_LENGTH} characters`)
  }
  if (!config.opsKey) {
    problems.push('NUXT_OPS_KEY is unset — the ops proxy would forward an empty X-Ops-Key and every backend call would 401')
  }

  if (problems.length > 0) {
    throw new Error(
      `Refusing to start the admin dashboard with insecure configuration:\n- ${problems.join('\n- ')}`,
    )
  }
})
