import type { paths } from './api'

export type Method = 'get' | 'post' | 'put' | 'patch' | 'delete'

type JsonOf<R> = R extends { content: { 'application/json': infer Body } } ? Body : never

type Operation<P extends keyof paths, M extends Method> = paths[P] extends { [K in M]: infer O } ? O : never

/** Every path the API serves method `M` on. */
export type PathOf<M extends Method> = {
  [P in keyof paths]: Operation<P, M> extends { responses: unknown } ? P : never
}[keyof paths]

/** The body a successful `M` on `P` answers with (`undefined` for 204 and other empty answers). */
export type ResponseOf<P extends PathOf<M>, M extends Method> = Operation<P, M> extends { responses: infer R }
  ? R extends { 200: infer Ok } ? JsonOf<Ok>
    : R extends { 201: infer Created } ? JsonOf<Created>
      : R extends { 202: infer Accepted } ? JsonOf<Accepted>
        : undefined
  : never

/** The query string `M` on `P` accepts. */
export type QueryOf<P extends PathOf<M>, M extends Method> = Operation<P, M> extends { parameters: { query?: infer Q } }
  ? NonNullable<Q>
  : never

/** The `{name}` segments of `P`, by name. */
export type PathParamsOf<P extends PathOf<M>, M extends Method> = Operation<P, M> extends { parameters: { path?: infer V } }
  ? { [K in keyof NonNullable<V>]: string | number }
  : never

/** The JSON body `M` on `P` takes. */
export type BodyOf<P extends PathOf<M>, M extends Method> = Operation<P, M> extends { requestBody?: infer B }
  ? JsonOf<NonNullable<B>>
  : never

export type GetPath = PathOf<'get'>
export type GetResponse<P extends GetPath> = ResponseOf<P, 'get'>
export type GetQuery<P extends GetPath> = QueryOf<P, 'get'>

/** `template` with each `{name}` replaced by its encoded value. */
export function fillPath(template: string, params: Record<string, string | number> = {}): string {
  return template.replace(/\{(\w+)\}/g, (_, name: string) => {
    const value = params[name]
    if (value === undefined) {
      throw new Error(`Missing path parameter "${name}" for ${template}`)
    }
    return encodeURIComponent(String(value))
  })
}
