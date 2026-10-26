/**
 * `GET /api/truemain-identity/{nameTag}` (#2335, #2336): the slug of the Riot ID a
 * profile URL names, spelled as TrueMain stores it — what the page's canonical
 * points at, whatever casing the visitor typed. An unknown account is a `404`.
 */
export interface TruemainIdentity {
  nameTag: string
}
