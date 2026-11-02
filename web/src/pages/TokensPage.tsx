import { useState } from 'react'
import {
  Alert, Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper, Stack,
  Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ApiTokenInfo } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { useNotify } from '../components/Notify'

/** Personal MCP access tokens (002-spec §5.3, §7.1). The token is shown once. */
export default function TokensPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const { notify, notifyError } = useNotify()
  const tokens = useQuery({ queryKey: ['tokens'], queryFn: () => api<ApiTokenInfo[]>('/me/tokens') })
  const [open, setOpen] = useState(false)
  const [form, setForm] = useState({ name: '', days: 90, write: false })
  const [created, setCreated] = useState<{ token: string; mcpUrl: string } | null>(null)

  async function create() {
    try {
      const res = await api<{ token: string; mcpUrl: string }>('/me/tokens', {
        method: 'POST',
        body: { name: form.name, days: form.days, scopes: form.write ? ['mcp:read', 'mcp:write'] : ['mcp:read'] },
      })
      setOpen(false)
      setCreated(res)
      qc.invalidateQueries({ queryKey: ['tokens'] })
    } catch (e) {
      notifyError(e)
    }
  }

  async function revoke(id: number) {
    try {
      await api(`/me/tokens/${id}`, { method: 'DELETE' })
      notify('令牌已撤销')
      qc.invalidateQueries({ queryKey: ['tokens'] })
    } catch (e) {
      notifyError(e)
    }
  }

  const config = created && JSON.stringify(
    { mcpServers: { goldata: { type: 'http', url: created.mcpUrl, headers: { Authorization: `Bearer ${created.token}` } } } }, null, 2)

  return (
    <Stack spacing={2}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Typography variant="h6">MCP 访问令牌</Typography>
        <Button variant="contained" onClick={() => setOpen(true)}>新建令牌</Button>
      </Box>
      <Alert severity="info">
        AI 客户端（如 Claude）通过 MCP 以<b>您的身份和权限</b>读取系统数据。人事/工资数据默认不开放给 MCP。请勿把令牌发给他人；不用时请撤销。
      </Alert>
      <Paper variant="outlined">
        <Table size="small">
          <TableHead>
            <TableRow><TableCell>名称</TableCell><TableCell>范围</TableCell><TableCell>创建</TableCell><TableCell>到期</TableCell><TableCell>最近使用</TableCell><TableCell /></TableRow>
          </TableHead>
          <TableBody>
            {tokens.data?.map((t) => (
              <TableRow key={t.id} sx={t.revokedAt ? { opacity: 0.5 } : undefined}>
                <TableCell>{t.name || '—'}</TableCell>
                <TableCell>{t.scopes}</TableCell>
                <TableCell>{t.createdAt.slice(0, 10)}</TableCell>
                <TableCell>{t.expiresAt.slice(0, 10)}</TableCell>
                <TableCell>{t.lastUsedAt?.replace('T', ' ').slice(0, 16) ?? '—'}</TableCell>
                <TableCell align="right">{t.revokedAt ? '已撤销' : <Button color="error" onClick={() => revoke(t.id)}>撤销</Button>}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Paper>

      <Dialog open={open} onClose={() => setOpen(false)} maxWidth="xs" fullWidth>
        <DialogTitle>新建 MCP 令牌</DialogTitle>
        <DialogContent>
          <TextField sx={{ mt: 1, mb: 2 }} label="名称（如：我的电脑 Claude）" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <TextField select label="有效期" value={form.days} onChange={(e) => setForm({ ...form, days: Number(e.target.value) })}>
            {[7, 30, 90].map((d) => <MenuItem key={d} value={d}>{d} 天</MenuItem>)}
          </TextField>
          {can('mcp:write') && (
            <FormControlLabel sx={{ mt: 1 }} control={<Checkbox checked={form.write} onChange={(e) => setForm({ ...form, write: e.target.checked })} />} label="允许写入（转交、关联等，需逐次确认）" />
          )}
        </DialogContent>
        <DialogActions><Button onClick={() => setOpen(false)}>取消</Button><Button variant="contained" onClick={create}>创建</Button></DialogActions>
      </Dialog>

      <Dialog open={!!created} onClose={() => setCreated(null)} maxWidth="sm" fullWidth>
        <DialogTitle>令牌已创建</DialogTitle>
        <DialogContent>
          <Alert severity="warning" sx={{ mb: 2 }}>令牌只显示这一次，请立即复制保存。</Alert>
          <Typography variant="caption">MCP 客户端配置：</Typography>
          <Box component="pre" sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 1, overflowX: 'auto', fontSize: 12 }}>{config}</Box>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => { void navigator.clipboard?.writeText(config ?? ''); notify('已复制') }}>复制配置</Button>
          <Button variant="contained" onClick={() => setCreated(null)}>完成</Button>
        </DialogActions>
      </Dialog>
    </Stack>
  )
}
