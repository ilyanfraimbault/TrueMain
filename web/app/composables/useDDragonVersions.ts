export function useDDragonVersions() {
  return useLazyFetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json', {
    key: 'ddragon-versions',
    default: () => [],
    server: false,
  })
}
