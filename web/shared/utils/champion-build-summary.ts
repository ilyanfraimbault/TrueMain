/**
 * Resolution + prose for the server-rendered build summary (#1123). Pure and
 * dependency-free so it can be unit-tested away from Nitro and from the page:
 * the failure mode that matters here is not a crash but a *plausible wrong
 * sentence* — an item printed in the wrong order, a rune attributed to the wrong
 * tree, or a placeholder that reads like a measurement.
 */

export * from './champion-build-summary/resolve'
export * from './champion-build-summary/sentences'
