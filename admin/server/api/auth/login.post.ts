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

// Drop entries whose window has elapsed so the map doesn't grow unbounded with
// one stale record per source IP over the life of the process.
function evictExpired(now: number): void {
  for (const [ip, entry] of attempts) {
    if (now > entry.resetAt) {
      attempts.delete(ip)
    }
  }
}

function tooManyAttempts(ip: string): boolean {
  const now = Date.now()
  evictExpired(now)
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
// timingSafeEqual requires equal byte length, so encode each side into a fixed
// fixed-size, zero-filled buffer. We size by *bytes* (not characters):
// `String.padEnd` pads UTF-16 code units, but UTF-8 encoding emits a variable
// number of bytes per character, so a multi-byte character (accent, emoji)
// would otherwise yield mismatched lengths and throw a 500.
const COMPARE_WIDTH = 512
const sharedEncoder = new TextEncoder()
function safeEqual(a: string, b: string): boolean {
  const bufA = new Uint8Array(COMPARE_WIDTH)
  const bufB = new Uint8Array(COMPARE_WIDTH)
  // encodeInto writes UTF-8 into the fixed buffer and silently stops at its end;
  // the remainder stays zero. Credentials never approach 512 bytes, so no
  // realistic value is truncated.
  sharedEncoder.encodeInto(a, bufA)
  sharedEncoder.encodeInto(b, bufB)
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
  const { adminUsername, adminPassword, trustProxy } = useRuntimeConfig(event)

  // Key the throttle on the real client IP. Trust `X-Forwarded-For` ONLY behind
  // a TLS-terminating reverse proxy that overwrites it (prod: Caddy, with
  // NUXT_TRUST_PROXY=true). In a direct-exposure deployment (dev, qa on :3002)
  // `X-Forwarded-For` is attacker-controlled — a client could send a fresh
  // value per attempt to cycle past the per-IP limit and brute-force the
  // password — so fall back to the TCP peer IP, which cannot be spoofed.
  const ip = getRequestIP(event, { xForwardedFor: trustProxy }) ?? 'unknown'
  if (tooManyAttempts(ip)) {
    throw createError({ statusCode: 429, statusMessage: 'Too many attempts, try again later' })
  }

  const body = await readBody<LoginBody>(event)
  const username = typeof body?.username === 'string' ? body.username : ''
  const password = typeof body?.password === 'string' ? body.password : ''

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
