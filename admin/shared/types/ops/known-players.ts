// Known-players management (#2019): `GET /ops/personas[/{id}]` and the operator's
// edits — `POST /ops/personas`, `PATCH|DELETE /ops/personas/{id}`, and the
// `links` / `accounts` sub-resources. "Persona" is the code's word for a known
// player; the UI says "known player".

export type PersonaListView = 'All' | 'Conflicts' | 'Unresolved' | 'Removed'
