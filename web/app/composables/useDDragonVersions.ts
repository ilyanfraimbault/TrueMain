export function useDDragonVersions() {
  return useFetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json', {
    key: 'ddragon-versions',
    default: () => [],
    server: false,
  })
}
