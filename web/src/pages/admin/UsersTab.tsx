import { useMemo, useState } from 'react'
import {
  Alert, Autocomplete, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton,
  Link, List, ListItem, ListItemText, Menu, MenuItem, Stack, TextField, Typography,
} from '@mui/material'
import { DataGrid, type GridColDef } from '@mui/x-data-grid'
import MoreIcon from '@mui/icons-material/MoreVert'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link as RouterLink } from 'react-router'
import { api } from '../../api/client'
import type { DeleteCheck, UserListItem } from '../../api/types'
import { useAuth } from '../../auth/AuthContext'
import Confirm from '../../components/Confirm'
import { useNotify } from '../../components/Notify'
import UserDrawer from './UserDrawer'
import { LEGACY_ROLES, STATUS_LABEL, useRanks, useRoles } from './adminApi'

export default function UsersTab() {
  const qc = useQueryClient()
  const { me } = useAuth()
  const { notify, notifyError } = useNotify()
  const [filter, setFilter] = useState({ q: '', roleId: '', status: '' })
  const users = useQuery({ queryKey: ['admin', 'users'], queryFn: () => api<UserListItem[]>('/admin/users') })
  const roles = useRoles()
  const [menu, setMenu] = useState<{ el: HTMLElement; user: UserListItem } | null>(null)
  const [editing, setEditing] = useState<UserListItem | null>(null)
  const [creating, setCreating] = useState(false)
  const [password, setPassword] = useState<{ user: string; password: string } | null>(null)
  const [deleting, setDeleting] = useState<{ user: UserListItem; check: DeleteCheck } | null>(null)
  const [removeStaff, setRemoveStaff] = useState(true)

  const refresh = () => qc.invalidateQueries({ queryKey: ['admin'] })
  const rows = useMemo(
    () =>
      (users.data ?? []).filter(
        (u) =>
          (!filter.q || u.login.includes(filter.q.toLowerCase()) || u.displayName.includes(filter.q)) &&
          (!filter.roleId || String(u.roleId) === filter.roleId) &&
          (!filter.status || u.status === filter.status),
      ),
    [users.data, filter],
  )

  async function act(path: string, done: string) {
    const u = menu!.user
    setMenu(null)
    try {
      await api(`/admin/users/${u.id}/${path}`, { method: 'POST' })
      notify(done)
      refresh()
    } catch (e) {
      notifyError(e)
    }
  }

  async function resetPassword() {
    const u = menu!.user
    setMenu(null)
    try {
      const res = await api<{ password: string }>(`/admin/users/${u.id}/reset-password`, { method: 'POST' })
      setPassword({ user: u.displayName, password: res.password })
      refresh()
    } catch (e) {
      notifyError(e)
    }
  }

  async function startDelete() {
    const u = menu!.user
    setMenu(null)
    try {
      setDeleting({ user: u, check: await api<DeleteCheck>(`/admin/users/${u.id}/delete-check`) })
    } catch (e) {
      notifyError(e)
    }
  }

  async function doDelete() {
    const u = deleting!.user
    try {
      await api(`/admin/users/${u.id}`, { method: 'DELETE', query: { removeFromStaffLookup: removeStaff } })
      notify(`已删除 ${u.displayName}`)
      setDeleting(null)
      refresh()
    } catch (e) {
      notifyError(e)
    }
  }

  const columns: GridColDef<UserListItem>[] = [
    { field: 'login', headerName: '登入名', width: 120 },
    { field: 'displayName', headerName: '姓名', width: 140 },
    {
      field: 'role', headerName: '角色', width: 130,
      renderCell: (p) => <>{p.value}{p.row.roleIsReadOnly && ' 🔒'}{!LEGACY_ROLES.has(p.value) && <Chip sx={{ ml: 0.5 }} label="旧系统无菜单" variant="outlined" />}</>,
    },
    { field: 'rank', headerName: '职级', width: 80 },
    {
      field: 'status', headerName: '状态', width: 90,
      renderCell: (p) => <Chip label={STATUS_LABEL[p.value]} color={p.value === 'active' ? 'success' : p.value === 'locked' ? 'warning' : 'default'} variant={p.value === 'active' ? 'filled' : 'outlined'} />,
    },
    { field: 'mustChangePassword', headerName: '待改密', width: 80, renderCell: (p) => (p.value ? '是' : '') },
    { field: 'lastLoginAt', headerName: '最后登入', width: 150, valueFormatter: (v: string | null) => v?.replace('T', ' ').slice(0, 16) ?? '—' },
    { field: 'activeTokens', headerName: '令牌', width: 60 },
    {
      field: 'actions', headerName: '', width: 60, sortable: false,
      renderCell: (p) => <IconButton onClick={(e) => { e.stopPropagation(); setMenu({ el: e.currentTarget, user: p.row }) }}><MoreIcon /></IconButton>,
    },
  ]

  return (
    <Box sx={{ p: 1.5 }}>
      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', mb: 1.5 }}>
        <TextField sx={{ maxWidth: 220 }} placeholder="搜索登入名 / 姓名" value={filter.q} onChange={(e) => setFilter({ ...filter, q: e.target.value })} />
        <TextField select sx={{ width: 150 }} label="角色" value={filter.roleId} onChange={(e) => setFilter({ ...filter, roleId: e.target.value })}>
          <MenuItem value="">全部</MenuItem>
          {roles.data?.map((r) => <MenuItem key={r.id} value={String(r.id)}>{r.name}</MenuItem>)}
        </TextField>
        <TextField select sx={{ width: 120 }} label="状态" value={filter.status} onChange={(e) => setFilter({ ...filter, status: e.target.value })}>
          <MenuItem value="">全部</MenuItem>
          {Object.entries(STATUS_LABEL).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}
        </TextField>
        <Box sx={{ flexGrow: 1 }} />
        <Button variant="contained" onClick={() => setCreating(true)}>+ 新增用户</Button>
      </Box>
      <Box sx={{ height: 'calc(100vh - 290px)', minHeight: 360 }}>
        <DataGrid density="compact" rows={rows} columns={columns} loading={users.isLoading} disableRowSelectionOnClick onRowClick={(p) => setEditing(p.row)} sx={{ '& .MuiDataGrid-row': { cursor: 'pointer' } }} />
      </Box>

      <Menu anchorEl={menu?.el} open={!!menu} onClose={() => setMenu(null)}>
        <MenuItem onClick={() => { setEditing(menu!.user); setMenu(null) }}>编辑</MenuItem>
        <MenuItem onClick={resetPassword}>重置密码</MenuItem>
        {menu?.user.status === 'locked' && <MenuItem onClick={() => act('unlock', '已解锁')}>解锁</MenuItem>}
        {menu?.user.status === 'disabled'
          ? <MenuItem onClick={() => act('enable', '已启用')}>启用</MenuItem>
          : <MenuItem disabled={menu?.user.id === me?.id} onClick={() => act('disable', '已停用，会话和令牌已失效')}>停用</MenuItem>}
        <MenuItem disabled={menu?.user.id === me?.id} onClick={startDelete} sx={{ color: 'error.main' }}>删除…</MenuItem>
      </Menu>

      {creating && <CreateUserDialog onClose={() => setCreating(false)} onCreated={(u, pw) => { setCreating(false); setPassword({ user: u, password: pw }); refresh() }} />}
      {editing && <UserDrawer user={editing} onClose={() => setEditing(null)} onSaved={refresh} />}

      <Dialog open={!!password} onClose={() => setPassword(null)} maxWidth="xs" fullWidth>
        <DialogTitle>{password?.user} 的初始密码</DialogTitle>
        <DialogContent>
          <Alert severity="warning" sx={{ mb: 2 }}>密码只显示这一次。用户首次登入后必须修改密码（新旧系统通用）。</Alert>
          <Typography sx={{ fontFamily: 'monospace', fontSize: 20, textAlign: 'center', letterSpacing: 1 }}>{password?.password}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => { void navigator.clipboard?.writeText(password?.password ?? ''); notify('已复制') }}>复制</Button>
          <Button variant="contained" onClick={() => setPassword(null)}>完成</Button>
        </DialogActions>
      </Dialog>

      {deleting && (
        deleting.check.openTodos.length > 0 ? (
          <Dialog open onClose={() => setDeleting(null)} maxWidth="sm" fullWidth>
            <DialogTitle>不能删除 {deleting.user.displayName}</DialogTitle>
            <DialogContent>
              <Alert severity="warning" sx={{ mb: 1 }}>该用户仍有 {deleting.check.openTodos.length} 项待办。请先把这些记录转交给其他人，或改为「停用」。</Alert>
              <List dense>
                {deleting.check.openTodos.slice(0, 50).map((t) => (
                  <ListItem key={`${t.module}-${t.recordId}`}>
                    <ListItemText primary={<Link component={RouterLink} to={`/m/${t.module}/${t.recordId}`}>{t.moduleTitle} #{t.recordId}</Link>} />
                  </ListItem>
                ))}
              </List>
            </DialogContent>
            <DialogActions><Button onClick={() => setDeleting(null)}>关闭</Button></DialogActions>
          </Dialog>
        ) : (
          <Confirm open danger title={`删除用户 ${deleting.user.displayName}`} confirmText="删除" typeToConfirm={deleting.user.login} onClose={() => setDeleting(null)} onConfirm={doDelete}>
            <Typography variant="body2" sx={{ mb: 1 }}>删除后该账户无法登入新旧系统，令牌一并作废。业务记录和修改记录中的姓名保持不变。一般建议使用「停用」。</Typography>
            {deleting.check.inStaffLookup && (
              <FormControlLabel control={<Checkbox checked={removeStaff} onChange={(e) => setRemoveStaff(e.target.checked)} />} label="同时从「公司员工」下拉列表移除（转交时不再显示）" />
            )}
          </Confirm>
        )
      )}
    </Box>
  )
}

function CreateUserDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (name: string, password: string) => void }) {
  const roles = useRoles()
  const ranks = useRanks()
  const { notifyError } = useNotify()
  const staff = useQuery({ queryKey: ['admin', 'staff-names'], queryFn: () => api<string[]>('/admin/users/staff-names') })
  const codes = useQuery({ queryKey: ['admin', 'staff-codes'], queryFn: () => api<string[]>('/admin/users/staff-codes') })
  const [f, setF] = useState({ login: '', displayName: '', roleId: '', rank: '员工', password: '', addToStaffLookup: true, staffCode: '' })
  const [busy, setBusy] = useState(false)
  const inStaff = staff.data?.includes(f.displayName.trim())

  async function submit() {
    setBusy(true)
    try {
      const res = await api<{ user: UserListItem; initialPassword: string }>('/admin/users', {
        method: 'POST',
        body: { ...f, roleId: Number(f.roleId), password: f.password || null, staffCode: f.staffCode || null, addToStaffLookup: f.addToStaffLookup && !inStaff },
      })
      onCreated(res.user.displayName, res.initialPassword)
    } catch (e) {
      notifyError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>新增用户</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="登入名" value={f.login} onChange={(e) => setF({ ...f, login: e.target.value.toLowerCase() })} helperText="小写字母、数字、. _ -，2–32 位；创建后首次登入前可修改" required />
          <Autocomplete
            freeSolo
            options={staff.data ?? []}
            inputValue={f.displayName}
            onInputChange={(_, v) => setF({ ...f, displayName: v })}
            renderInput={(p) => <TextField {...p} label="姓名" required helperText="与业务记录、待办和修改记录中的姓名一致，必须唯一；用户登入后不能再改" />}
          />
          <Box sx={{ display: 'flex', gap: 2 }}>
            <TextField select label="角色" value={f.roleId} onChange={(e) => setF({ ...f, roleId: e.target.value })} required>
              {roles.data?.map((r) => <MenuItem key={r.id} value={String(r.id)}>{r.name}{r.isReadOnly ? '（只读）' : ''}</MenuItem>)}
            </TextField>
            <TextField select label="职级" value={f.rank} onChange={(e) => setF({ ...f, rank: e.target.value })}>
              {ranks.data?.map((r) => <MenuItem key={r.name} value={r.name}>{r.name}</MenuItem>)}
            </TextField>
          </Box>
          <TextField label="初始密码（留空自动生成）" type="password" value={f.password} onChange={(e) => setF({ ...f, password: e.target.value })} autoComplete="new-password" />
          {f.displayName && !inStaff && (
            <Box>
              <Alert severity="info" sx={{ mb: 1 }}>「{f.displayName}」不在「公司员工」下拉列表中（转交时选人用）。</Alert>
              <Box sx={{ display: 'flex', gap: 2, alignItems: 'center' }}>
                <FormControlLabel control={<Checkbox checked={f.addToStaffLookup} onChange={(e) => setF({ ...f, addToStaffLookup: e.target.checked })} />} label="添加到「公司员工」" />
                {f.addToStaffLookup && (
                  <TextField select sx={{ width: 180 }} label="部门代码" value={f.staffCode} onChange={(e) => setF({ ...f, staffCode: e.target.value })} helperText="留空按角色自动选择">
                    <MenuItem value="">自动</MenuItem>
                    {codes.data?.map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}
                  </TextField>
                )}
              </Box>
            </Box>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>取消</Button>
        <Button variant="contained" disabled={busy || !f.login || !f.displayName || !f.roleId} onClick={submit}>创建</Button>
      </DialogActions>
    </Dialog>
  )
}
