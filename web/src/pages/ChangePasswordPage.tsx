import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { Alert, Box, Button, Paper, TextField, Typography } from '@mui/material'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../api/client'
import { useNotify } from '../components/Notify'

export default function ChangePasswordPage() {
  const { me, changePassword, logout } = useAuth()
  const navigate = useNavigate()
  const { notify } = useNotify()
  const [f, setF] = useState({ current: '', next: '', confirm: '' })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (f.next !== f.confirm) return setError('两次输入的新密码不一致')
    setBusy(true)
    setError(null)
    try {
      await changePassword(f.current, f.next)
      notify('密码已修改，旧系统也将使用新密码')
      navigate('/', { replace: true })
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '修改失败')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Box sx={{ minHeight: me?.mustChangePassword ? '100vh' : undefined, display: 'grid', placeItems: 'center', p: 2 }}>
      <Paper component="form" onSubmit={submit} sx={{ p: 4, width: '100%', maxWidth: 420 }}>
        <Typography variant="h6" sx={{ mb: 1 }}>修改密码</Typography>
        {me?.mustChangePassword && (
          <Alert severity="warning" sx={{ mb: 2 }}>为了账户安全，请先设置新密码（至少 10 位）。新密码同时适用于旧系统。</Alert>
        )}
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <TextField label="当前密码" type="password" autoComplete="current-password" value={f.current} onChange={(e) => setF({ ...f, current: e.target.value })} sx={{ mb: 2 }} required />
        <TextField label="新密码" type="password" autoComplete="new-password" value={f.next} onChange={(e) => setF({ ...f, next: e.target.value })} sx={{ mb: 2 }} required helperText="至少 10 位，不要使用常见密码或登入名" />
        <TextField label="确认新密码" type="password" autoComplete="new-password" value={f.confirm} onChange={(e) => setF({ ...f, confirm: e.target.value })} sx={{ mb: 3 }} required />
        <Box sx={{ display: 'flex', gap: 1 }}>
          <Button type="submit" variant="contained" disabled={busy}>保存</Button>
          {me?.mustChangePassword ? (
            <Button onClick={async () => { await logout(); navigate('/login') }}>退出</Button>
          ) : (
            <Button onClick={() => navigate(-1)}>取消</Button>
          )}
        </Box>
      </Paper>
    </Box>
  )
}
