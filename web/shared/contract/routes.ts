import type { paths } from './api'

type JsonOf<R> = R extends { content: { 'application/json': infer Body } } ? Body : never

/** Every path the API serves a `GET` on. */
export type GetPath = {
  [P in keyof paths]: paths[P] extends { get: { responses: { 200: unknown } } } ? P : never
}[keyof paths]

type GetOperation<P extends GetPath> = paths[P] extends { get: infer Operation } ? Operation : never

/** The body a `GET` on `P` answers with. */
export type GetResponse<P extends GetPath> = GetOperation<P> extends { responses: { 200: infer Ok } }
  ? JsonOf<Ok>
  : never

/** The query string a `GET` on `P` accepts. */
export type GetQuery<P extends GetPath> = GetOperation<P> extends { parameters: { query?: infer Query } }
  ? NonNullable<Query>
  : never
