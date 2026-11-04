/** The web address a click on a new-tab link asks for, or none when it hit no such link. */
export function newTabLinkOf(target: EventTarget | null): string | null {
  const link = target instanceof Element ? target.closest('a[target="_blank"]') : null
  const href = link?.getAttribute('href') ?? ''
  return /^https?:\/\//i.test(href) ? href : null
}
