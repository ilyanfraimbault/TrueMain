import { timingSafeEqual } from 'node:crypto'
import { createError, defineEventHandler, getRequestIP, readBody } from 'h3'

interface LoginBody {
  username?: unknown
  password?: unknown
}

// In-memory, per-IP brute-force throttle. Nitro applies no rate limiting by
// default, and the credential check below is cheap, so without this an attacker
// who can reach the port could try thousands of passwords per second. A single
// admin instance makes a process-local counter sufficient; a multi-instance
// deploy would want a shared store (e.g. Redis).
const MAX_ATTEMPTS = 5
const WINDOW_MS = 60_000
const attempts = new Map<string, { count: number, resetAt: number }>()

function tooManyAttempts(ip: string): boolean {
  const now = Date.now()
  const entry = attempts.get(ip)
  if (!entry || now > entry.resetAt) {
    attempts.set(ip, { count: 1, resetAt: now + WINDOW_MS })
    return false
  }
  entry.count += 1
  return entry.count > MAX_ATTEMPTS
}

function clearAttempts(ip: string): void {
  attempts.delete(ip)
}

// Constant-time string comparison. A plain `!==` short-circuits on the first
// differing byte, leaking length/prefix information through response timing.
// timingSafeEqual requires equal-length buffers, so pad both sides to a fixed
// width well above any realistic credential length before comparing.
const COMPARE_WIDTH = 256
function safeEqual(a: string, b: string): boolean {
  const encoder = new TextEncoder()
  const bufA = encoder.encode(a.padEnd(COMPARE_WIDTH).slice(0, COMPARE_WIDTH))
  const bufB = encoder.encode(b.padEnd(COMPARE_WIDTH).slice(0, COMPARE_WIDTH))
  return timingSafeEqual(bufA, bufB)
}

/**
 * Single-operator login. Compares the submitted credentials against the
 * `adminUsername`/`adminPassword` runtime config (both default to `truemain`
 * for local dev, overridable via NUXT_ADMIN_USERNAME / NUXT_ADMIN_PASSWORD).
 * On a match it seals an httpOnly session cookie via nuxt-auth-utils so the
 * password never round-trips again. Throttled per IP to resist brute force and
 * compared in constant time to avoid timing leaks.
 */
export default defineEventHandler(async (event) => {
  const ip = getRequestIP(event, { xForwardedFor: true }) ?? 'unknown'
  if (tooManyAttempts(ip)) {
    throw createError({ statusCode: 429, statusMessage: 'Too many attempts, try again later' })
  }

  const body = await readBody<LoginBody>(event)
  const username = typeof body?.username === 'string' ? body.username : ''
  const password = typeof body?.password === 'string' ? body.password : ''

  const { adminUsername, adminPassword } = useRuntimeConfig(event)

  // Evaluate both comparisons unconditionally so a wrong username and a wrong
  // password take the same path.
  const usernameMatch = safeEqual(username, adminUsername)
  const passwordMatch = safeEqual(password, adminPassword)
  if (!usernameMatch || !passwordMatch) {
    throw createError({ statusCode: 401, statusMessage: 'Invalid credentials' })
  }

  clearAttempts(ip)

  await setUserSession(event, {
    user: { name: username },
  })

  return { ok: true }
})
