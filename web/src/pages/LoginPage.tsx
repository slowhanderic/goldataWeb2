import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { Alert, Box, Button, Paper, TextField, Typography } from '@mui/material'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../api/client'
import { brand } from '../theme/brand'

export default function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [form, setForm] = useState({ login: '', password: '' })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const me = await login(form.login.trim(), form.password)
      const from = (location.state as { from?: string } | null)?.from
      navigate(me.mustChangePassword ? '/change-password' : from ?? '/', { replace: true })
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '登入失败，请稍后重试')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', bgcolor: 'background.default', p: 2 }}>
      <Paper component="form" onSubmit={submit} sx={{ p: 4, width: '100%', maxWidth: 380 }} elevation={2}>
        <Typography variant="h5" sx={{ mb: 0.5, color: 'primary.main', fontWeight: 600 }}>{brand.appTitle}</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>请使用原系统的登入名和密码</Typography>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        <TextField label="登入名" value={form.login} onChange={(e) => setForm({ ...form, login: e.target.value })} autoFocus autoComplete="username" sx={{ mb: 2 }} required />
        <TextField label="密码" type="password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} autoComplete="current-password" sx={{ mb: 3 }} required />
        <Button type="submit" variant="contained" fullWidth size="medium" disabled={busy}>登入</Button>
      </Paper>
    </Box>
  )
}
