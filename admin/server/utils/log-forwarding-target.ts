/**
 * What the shared log-forwarding plugin (`web/layers/nitro`) needs from this app:
 * the process its errors are filed under in the ops logs, and the API it posts
 * them to. A process the forwarder does not know fails the typecheck there.
 */
export function logForwardingTarget() {
  return { process: 'Admin' as const, apiBaseUrl: useRuntimeConfig().opsApiBaseUrl }
}
