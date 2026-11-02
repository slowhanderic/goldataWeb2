import { useMemo, useState } from 'react'
import {
  Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, List, ListItemButton,
  ListItemText, MenuItem, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, ToggleButton, ToggleButtonGroup, Typography,
} from '@mui/material'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { AppRole, MatrixDiff, PermissionDef, RoleMatrix } from '../../api/types'
import Confirm from '../../components/Confirm'
import { useNotify } from '../../components/Notify'
import { ACTIONS, LEGACY_ROLES, usePermissionCatalog, useRanks, useRoles } from './adminApi'

const ALL = '*'

/** 角色与权限 (REQ-ADM-9…13): role list + permission matrix per rank layer, with dry-run diff before saving. */
export default function RolesTab() {
  const roles = useRoles()
  const [picked, setSelected] = useState<number | null>(null)
  const [creating, setCreating] = useState(false)
  const selected = picked ?? roles.data?.[0]?.id ?? null
  const role = roles.data?.find((r) => r.id === selected)

  return (
    <Box sx={{ display: 'flex', minHeight: 500, flexDirection: { xs: 'column', md: 'row' } }}>
      <Box sx={{ width: { md: 220 }, borderRight: { md: 1 }, borderColor: 'divider', p: 1 }}>
        <List dense>
          {roles.data?.map((r) => (
            <ListItemButton key={r.id} selected={r.id === selected} onClick={() => setSelected(r.id)}>
              <ListItemText primary={`${r.name}${r.isReadOnly ? ' 🔒' : ''}`} secondary={`${r.userCount} 用户${LEGACY_ROLES.has(r.name) ? '' : ' · 旧系统无菜单'}`} />
            </ListItemButton>
          ))}
        </List>
        <Button fullWidth variant="outlined" onClick={() => setCreating(true)}>+ 新增角色</Button>
      </Box>
      <Box sx={{ flexGrow: 1, p: 1.5, minWidth: 0 }}>{role && <RoleEditor key={role.id} role={role} onDeleted={() => setSelected(null)} />}</Box>
      {creating && <CreateRoleDialog roles={roles.data ?? []} onClose={() => setCreating(false)} onCreated={(id) => { setCreating(false); setSelected(id) }} />}
    </Box>
  )
}

function RoleEditor({ role, onDeleted }: { role: AppRole; onDeleted: () => void }) {
  const qc = useQueryClient()
  const { notify, notifyError } = useNotify()
  const catalog = usePermissionCatalog()
  const ranks = useRanks()
  const matrix = useQuery({ queryKey: ['admin', 'matrix', role.id], queryFn: () => api<RoleMatrix>(`/admin/roles/${role.id}/permissions`) })
  const [draft, setDraft] = useState<Record<string, Set<string>> | null>(null)
  const layers = useMemo(
    () => draft ?? Object.fromEntries(Object.entries(matrix.data?.layers ?? {}).map(([k, v]) => [k, new Set(v)])),
    [draft, matrix.data],
  )
  const [layer, setLayer] = useState(ALL)
  const [diff, setDiff] = useState<MatrixDiff | null>(null)
  const [sensitiveOk, setSensitiveOk] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [confirmReset, setConfirmReset] = useState(false)

  const { modules, specials } = useMemo(() => {
    const mods = new Map<string, { group: string; title: string; codes: Map<string, PermissionDef> }>()
    const sp: PermissionDef[] = []
    for (const p of catalog.data ?? []) {
      if (!p.module || !p.action) { sp.push(p); continue }
      const entry = mods.get(p.module) ?? { group: p.group, title: p.label.split(':')[0], codes: new Map() }
      entry.codes.set(p.action, p)
      mods.set(p.module, entry)
    }
    return { modules: [...mods.entries()], specials: sp }
  }, [catalog.data])

  const has = (l: string, code: string) => layers[l]?.has(code) ?? false
  const toggle = (code: string, on: boolean) =>
    setDraft(() => {
      const s = new Set(layers[layer] ?? [])
      if (on) s.add(code)
      else s.delete(code)
      return { ...layers, [layer]: s }
    })
  const writeBlocked = (p: PermissionDef) => role.isReadOnly && p.isWrite

  function body() {
    return { layers: Object.fromEntries(Object.entries(layers).map(([k, v]) => [k, [...v]])), rowVersion: matrix.data!.rowVersion }
  }

  async function preview() {
    try {
      const d = await api<MatrixDiff>(`/admin/roles/${role.id}/permissions`, { method: 'PUT', body: body(), query: { dryRun: true } })
      if (!d.added.length && !d.removed.length) return notify('没有变化', 'info')
      setSensitiveOk(false)
      setDiff(d)
    } catch (e) {
      notifyError(e)
    }
  }

  async function save() {
    try {
      await api(`/admin/roles/${role.id}/permissions`, { method: 'PUT', body: body() })
      notify(`已保存，${diff?.affectedUsers.length ?? 0} 个用户在下次操作时生效`)
      setDiff(null)
      setDraft(null)
      qc.invalidateQueries({ queryKey: ['admin'] })
    } catch (e) {
      notifyError(e)
    }
  }

  async function del() {
    try {
      await api(`/admin/roles/${role.id}`, { method: 'DELETE' })
      notify('角色已删除')
      qc.invalidateQueries({ queryKey: ['admin', 'roles'] })
      onDeleted()
    } catch (e) {
      notifyError(e)
    } finally {
      setConfirmDelete(false)
    }
  }

  async function reset() {
    try {
      await api(`/admin/roles/${role.id}/reset-default`, { method: 'POST' })
      notify('已恢复默认权限')
      setDraft(null)
      qc.invalidateQueries({ queryKey: ['admin'] })
    } catch (e) {
      notifyError(e)
    } finally {
      setConfirmReset(false)
    }
  }

  const label = (code: string) => {
    const [c, rank] = code.split('@')
    return `${catalog.data?.find((p) => p.code === c)?.label ?? c}${rank ? `（仅${rank}）` : ''}`
  }

  return (
    <Stack spacing={1.5}>
      <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography variant="subtitle1">{role.name}</Typography>
        <Chip label={`${role.userCount} 用户`} />
        {role.isReadOnly && <Chip label="只读角色" color="info" />}
        {!LEGACY_ROLES.has(role.name) && <Chip label="旧系统无对应菜单" variant="outlined" />}
        <Box sx={{ flexGrow: 1 }} />
        <ToggleButtonGroup size="small" exclusive value={layer} onChange={(_, v) => v && setLayer(v)}>
          <ToggleButton value={ALL}>全部职级</ToggleButton>
          {ranks.data?.map((r) => <ToggleButton key={r.name} value={r.name}>仅{r.name}</ToggleButton>)}
        </ToggleButtonGroup>
        {role.isSeeded && <Button onClick={() => setConfirmReset(true)}>恢复默认</Button>}
        <Button color="error" disabled={role.userCount > 0} onClick={() => setConfirmDelete(true)}>删除角色</Button>
        <Button variant="contained" onClick={preview} disabled={!matrix.data}>保存</Button>
      </Box>
      {layer !== ALL && <Alert severity="info">正在编辑「仅{layer}」层：在「全部职级」中已授予的权限显示为已勾选且不可修改。</Alert>}

      <Box sx={{ overflowX: 'auto' }}>
        <Table size="small" stickyHeader>
          <TableHead>
            <TableRow>
              <TableCell>模块</TableCell>
              {ACTIONS.map((a) => <TableCell key={a.key} align="center">{a.label}</TableCell>)}
            </TableRow>
          </TableHead>
          <TableBody>
            {modules.map(([key, mod], i) => {
              const header = i === 0 || modules[i - 1][1].group !== mod.group
              return [
                header && (
                  <TableRow key={`g-${mod.group}`}>
                    <TableCell colSpan={ACTIONS.length + 1} sx={{ bgcolor: 'action.hover', fontWeight: 600 }}>
                      {mod.group}{mod.group === '人事' && ' ⚠ 敏感'}
                    </TableCell>
                  </TableRow>
                ),
                <TableRow key={key} hover>
                  <TableCell sx={{ pl: 3 }}>{mod.title}</TableCell>
                  {ACTIONS.map((a) => {
                    const p = mod.codes.get(a.key)
                    if (!p) return <TableCell key={a.key} align="center" sx={{ color: 'text.disabled' }}>–</TableCell>
                    const inherited = layer !== ALL && has(ALL, p.code)
                    return (
                      <TableCell key={a.key} align="center" padding="checkbox">
                        <Checkbox size="small" checked={inherited || has(layer, p.code)} disabled={inherited || writeBlocked(p)} onChange={(e) => toggle(p.code, e.target.checked)} />
                      </TableCell>
                    )
                  })}
                </TableRow>,
              ]
            })}
          </TableBody>
        </Table>
      </Box>

      <Typography variant="subtitle2">特殊权限</Typography>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
        {specials.map((p) => {
          const inherited = layer !== ALL && has(ALL, p.code)
          return (
            <FormControlLabel
              key={p.code}
              control={<Checkbox size="small" checked={inherited || has(layer, p.code)} disabled={inherited || writeBlocked(p)} onChange={(e) => toggle(p.code, e.target.checked)} />}
              label={`${p.label}${p.isSensitive ? ' ⚠' : ''}`}
            />
          )
        })}
      </Box>

      <Dialog open={!!diff} onClose={() => setDiff(null)} maxWidth="sm" fullWidth>
        <DialogTitle>确认修改「{role.name}」的权限</DialogTitle>
        <DialogContent>
          {diff?.added.map((a) => <Typography key={a} variant="body2" color="success.main">＋ {label(a)}</Typography>)}
          {diff?.removed.map((r) => <Typography key={r} variant="body2" color="error.main">－ {label(r)}</Typography>)}
          <Typography variant="body2" sx={{ mt: 2 }}>影响 {diff?.affectedUsers.length} 个用户：{diff?.affectedUsers.join('、') || '无'}</Typography>
          {diff?.sensitive && (
            <Alert severity="warning" sx={{ mt: 2 }}>
              包含敏感权限（人事/工资、用户或角色管理）。
              <FormControlLabel control={<Checkbox checked={sensitiveOk} onChange={(e) => setSensitiveOk(e.target.checked)} />} label="我确认授予这些敏感权限" />
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDiff(null)}>取消</Button>
          <Button variant="contained" disabled={!!diff?.sensitive && !sensitiveOk} onClick={save}>确认保存</Button>
        </DialogActions>
      </Dialog>
      <Confirm open={confirmDelete} danger title={`删除角色「${role.name}」`} confirmText="删除" onClose={() => setConfirmDelete(false)} onConfirm={del}>该角色没有用户，删除后不可恢复。</Confirm>
      <Confirm open={confirmReset} title="恢复默认权限" onClose={() => setConfirmReset(false)} onConfirm={reset}>把「{role.name}」的权限恢复为与旧系统一致的默认设置？</Confirm>
    </Stack>
  )
}

function CreateRoleDialog({ roles, onClose, onCreated }: { roles: AppRole[]; onClose: () => void; onCreated: (id: number) => void }) {
  const qc = useQueryClient()
  const { notifyError } = useNotify()
  const [f, setF] = useState({ name: '', description: '', isReadOnly: false, copyFromRoleId: '' })
  async function submit() {
    try {
      const r = await api<AppRole>('/admin/roles', {
        method: 'POST',
        body: { ...f, copyFromRoleId: f.copyFromRoleId ? Number(f.copyFromRoleId) : null },
      })
      qc.invalidateQueries({ queryKey: ['admin', 'roles'] })
      onCreated(r.id)
    } catch (e) {
      notifyError(e)
    }
  }
  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>新增角色</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="角色名称" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} helperText="创建后不能改名，不能包含 '-'" required />
          <TextField label="说明" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} />
          <TextField select label="复制权限自" value={f.copyFromRoleId} onChange={(e) => setF({ ...f, copyFromRoleId: e.target.value })}>
            <MenuItem value="">不复制（空白）</MenuItem>
            {roles.map((r) => <MenuItem key={r.id} value={String(r.id)}>{r.name}</MenuItem>)}
          </TextField>
          <FormControlLabel control={<Checkbox checked={f.isReadOnly} onChange={(e) => setF({ ...f, isReadOnly: e.target.checked })} />} label="只读角色（不能拥有任何写入权限）" />
          <Alert severity="info">旧 PHP 系统只认识原有的 10 个角色。新角色的用户登入旧系统时菜单为空。</Alert>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>取消</Button><Button variant="contained" disabled={!f.name.trim()} onClick={submit}>创建</Button></DialogActions>
    </Dialog>
  )
}
