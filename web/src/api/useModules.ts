import { useQuery } from '@tanstack/react-query'
import { api } from './client'
import type { ModuleMeta } from './types'

/** Module metadata (fields + Chinese labels from the DB column comments), cached for the session. */
export function useModules() {
  return useQuery({ queryKey: ['modules'], queryFn: () => api<ModuleMeta[]>('/modules'), staleTime: 10 * 60_000 })
}

export function useModule(key: string | undefined) {
  const q = useModules()
  return { ...q, module: q.data?.find((m) => m.key === key) }
}
