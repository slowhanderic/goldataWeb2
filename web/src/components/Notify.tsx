import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'
import { Alert, Snackbar } from '@mui/material'
import { ApiError } from '../api/client'

type Severity = 'success' | 'error' | 'info' | 'warning'
interface Message { id: number; text: string; severity: Severity }

const NotifyContext = createContext<{ notify: (text: string, severity?: Severity) => void; notifyError: (e: unknown) => void } | null>(null)

/** Small snackbar service (MUI Snackbar), replaces a notistack dependency. */
export function NotifyProvider({ children }: { children: ReactNode }) {
  const [msg, setMsg] = useState<Message | null>(null)
  const notify = useCallback((text: string, severity: Severity = 'success') => setMsg({ id: Date.now(), text, severity }), [])
  const notifyError = useCallback(
    (e: unknown) => setMsg({ id: Date.now(), text: e instanceof ApiError ? e.message : '操作失败，请稍后重试', severity: 'error' }),
    [],
  )
  return (
    <NotifyContext value={{ notify, notifyError }}>
      {children}
      <Snackbar
        key={msg?.id}
        open={!!msg}
        autoHideDuration={msg?.severity === 'error' ? 8000 : 3000}
        onClose={() => setMsg(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity={msg?.severity ?? 'info'} variant="filled" onClose={() => setMsg(null)} sx={{ minWidth: 280 }}>
          {msg?.text}
        </Alert>
      </Snackbar>
    </NotifyContext>
  )
}

export function useNotify() {
  const ctx = useContext(NotifyContext)
  if (!ctx) throw new Error('useNotify outside NotifyProvider')
  return ctx
}
