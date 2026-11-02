import { createContext, useCallback, useContext, useMemo, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, setCsrfToken } from '../api/client'
import type { Me } from '../api/types'

interface AuthState {
  me: Me | null
  loading: boolean
  can: (permission: string) => boolean
  login: (login: string, password: string) => Promise<Me>
  logout: () => Promise<void>
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient()
  const meQuery = useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      try {
        return await api<Me>('/me')
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null
        throw e
      }
    },
    staleTime: 60_000,
    retry: false,
  })

  const me = meQuery.data ?? null
  const perms = useMemo(() => new Set(me?.permissions ?? []), [me])

  const login = useCallback(
    async (login: string, password: string) => {
      const res = await api<{ me: Me; csrf: string }>('/auth/login', { method: 'POST', body: { login, password } })
      setCsrfToken(res.csrf)
      // Drop the previous user's cached data, but keep the ['me'] query itself: clear() would detach the
      // observer that RequireAuth watches, so the new user would never be seen and the page would stay on /login.
      qc.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' })
      qc.setQueryData(['me'], res.me)
      return res.me
    },
    [qc],
  )

  const logout = useCallback(async () => {
    await api('/auth/logout', { method: 'POST' }).catch(() => undefined)
    setCsrfToken(null)
    qc.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' })
    qc.setQueryData(['me'], null)
  }, [qc])

  const changePassword = useCallback(
    async (currentPassword: string, newPassword: string) => {
      const res = await api<{ me: Me; csrf: string }>('/auth/change-password', { method: 'POST', body: { currentPassword, newPassword } })
      setCsrfToken(res.csrf)
      qc.setQueryData(['me'], res.me)
    },
    [qc],
  )

  const value = useMemo<AuthState>(
    () => ({ me, loading: meQuery.isLoading, can: (p) => perms.has(p), login, logout, changePassword }),
    [me, meQuery.isLoading, perms, login, logout, changePassword],
  )
  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth outside AuthProvider')
  return ctx
}
