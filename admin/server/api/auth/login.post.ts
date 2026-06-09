import { createError, defineEventHandler, readBody } from 'h3'

interface LoginBody {
  username?: unknown
  password?: unknown
}

/**
 * Single-operator login. Compares the submitted credentials against the
 * `adminUsername`/`adminPassword` runtime config (both default to `truemain`
 * for local dev, overridable via NUXT_ADMIN_USERNAME / NUXT_ADMIN_PASSWORD).
 * On a match it seals an httpOnly session cookie via nuxt-auth-utils so the
 * password never round-trips again.
 */
export default defineEventHandler(async (event) => {
  const body = await readBody<LoginBody>(event)
  const username = typeof body?.username === 'string' ? body.username : ''
  const password = typeof body?.password === 'string' ? body.password : ''

  const { adminUsername, adminPassword } = useRuntimeConfig(event)

  if (username !== adminUsername || password !== adminPassword) {
    throw createError({ statusCode: 401, statusMessage: 'Invalid credentials' })
  }

  await setUserSession(event, {
    user: { name: username },
  })

  return { ok: true }
})
