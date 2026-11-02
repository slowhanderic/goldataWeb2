import { useQuery } from '@tanstack/react-query'
import { api } from './client'
import type { LookupItem } from './types'

/** Dropdown options from lookup_tables (same category keys as the legacy forms). 请选择 is a placeholder, not an option. */
export function useLookup(category: string | null | undefined) {
  return useQuery({
    queryKey: ['lookup', category],
    queryFn: () => api<LookupItem[]>(`/lookups/${encodeURIComponent(category!)}`),
    enabled: !!category,
    staleTime: 10 * 60_000,
    select: (items) => items.map((i) => i.item).filter((v) => v && v !== '请选择'),
  })
}

/** Active staff for 转交 and person fields (公司员工 with a department code). */
export function useStaff() {
  return useQuery({ queryKey: ['staff'], queryFn: () => api<string[]>('/staff'), staleTime: 10 * 60_000 })
}
