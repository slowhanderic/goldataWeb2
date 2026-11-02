import { StrictMode, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, Navigate, RouterProvider, useLocation } from 'react-router'
import { CssBaseline, LinearProgress } from '@mui/material'
import { ThemeProvider } from '@mui/material/styles'
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import dayjs from 'dayjs'
import 'dayjs/locale/zh-cn'
import { ApiError } from './api/client'
import { AuthProvider, useAuth } from './auth/AuthContext'
import AppLayout from './components/AppLayout'
import { NotifyProvider } from './components/Notify'
import { theme } from './theme'
import LoginPage from './pages/LoginPage'
import ChangePasswordPage from './pages/ChangePasswordPage'
import HomePage from './pages/HomePage'
import ModuleListPage from './pages/ModuleListPage'
import RecordPage from './pages/RecordPage'
import TokensPage from './pages/TokensPage'
import AdminPage from './pages/admin/AdminPage'

dayjs.locale('zh-cn')

// A session that expired or was revoked (disabled user, password reset) sends the user back to the login page.
const onError = (e: unknown) => {
  if (e instanceof ApiError && e.status === 401 && !location.pathname.startsWith('/login')) location.assign('/login')
  if (e instanceof ApiError && e.code === 'MUST_CHANGE_PASSWORD' && location.pathname !== '/change-password') location.assign('/change-password')
}
const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError }),
  mutationCache: new MutationCache({ onError }),
  defaultOptions: { queries: { retry: (n, e) => !(e instanceof ApiError && e.status < 500) && n < 2, refetchOnWindowFocus: false } },
})

function RequireAuth({ children }: { children: ReactNode }) {
  const { me, loading } = useAuth()
  const location = useLocation()
  if (loading) return <LinearProgress />
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  if (me.mustChangePassword && location.pathname !== '/change-password') return <Navigate to="/change-password" replace />
  return children
}

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    path: '/change-password',
    element: <RequireAuth><ChangePasswordPage /></RequireAuth>,
  },
  {
    element: <RequireAuth><AppLayout /></RequireAuth>,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'm/:key', element: <ModuleListPage /> },
      { path: 'm/:key/:id', element: <RecordPage /> },
      { path: 'tokens', element: <TokensPage /> },
      { path: 'admin', element: <AdminPage /> },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
])

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider theme={theme} defaultMode="light">
      <CssBaseline />
      <QueryClientProvider client={queryClient}>
        <NotifyProvider>
          <AuthProvider>
            <RouterProvider router={router} />
          </AuthProvider>
        </NotifyProvider>
      </QueryClientProvider>
    </ThemeProvider>
  </StrictMode>,
)
