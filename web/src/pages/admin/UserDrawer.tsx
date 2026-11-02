import { useMemo, useState } from 'react'
import {
  Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Chip, Divider, Drawer, MenuItem, Stack, TextField,
  ToggleButton, ToggleButtonGroup, Tooltip, Typography,
} from '@mui/material'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { ApiTokenInfo, PermissionDef, PermissionSource, UserListItem, UserOverride } from '../../api/types'
import { useNotify } from '../../components/Notify'
import { usePermissionCatalog, useRanks, useRoles } from './adminApi'

type Tri = 'inherit' | 'allow' | 'deny'

/** Edit drawer: basics, per-user overrides (继承/允许/禁止), effective permissions with sources, tokens (REQ-ADM-3/7/8). */
export default function UserDrawer({ user, onClose, onSaved }: { user: UserListItem; onClose: () => void; onSaved: () => void }) {
  const qc = useQueryClient()
  const { notify, notifyError } = useNotify()
  const roles = useRoles()
  const ranks = useRanks()
  const catalog = usePermissionCatalog()
  const overrides = useQuery({ queryKey: ['admin', 'overrides', user.id], queryFn: () => api<UserOverride[]>(`/admin/users/${user.id}/overrides`) })
  const effective = useQuery({ queryKey: ['admin', 'effective', user.id], queryFn: () => api<PermissionSource[]>(`/admin/users/${user.id}/effective-permissions`) })
  const tokens = useQuery({ queryKey: ['admin', 'tokens', user.id], queryFn: () => api<ApiTokenInfo[]>(`/admin/users/${user.id}/tokens`) })

  const [basic, setBasic] = useState({ roleId: String(user.roleId), rank: user.rank, displayName: user.displayName, login: user.login })
  const [draft, setTri] = useState<Record<string, Tri> | null>(null)
  const tri = useMemo<Record<string, Tri>>(
    () => draft ?? Object.fromEntries((overrides.data ?? []).map((o) => [o.permission, o.allow ? 'allow' : 'deny'])),
    [draft, overrides.data],
  )

  const groups = useMemo(() => {
    const m = new Map<string, PermissionDef[]>()
    for (const p of catalog.data ?? []) m.set(p.group, [...(m.get(p.group) ?? []), p])
    return [...m.entries()]
  }, [catalog.data])
  const effectiveMap = new Map(effective.data?.map((e) => [e.permission, e.sources]) ?? [])
  const labelOf = (code: string) => catalog.data?.find((p) => p.code === code)?.label ?? code

  async function saveBasic() {
    try {
      await api(`/admin/users/${user.id}`, {
        method: 'PUT',
        body: {
          roleId: Number(basic.roleId), rank: basic.rank, rowVersion: user.rowVersion,
          displayName: user.nameLocked ? null : basic.displayName, login: user.nameLocked ? null : basic.login,
        },
      })
      notify('已保存（角色变更后该用户需重新登入）')
      onSaved()
      onClose()
    } catch (e) {
      notifyError(e)
    }
  }

  async function saveOverrides() {
    try {
      const list = Object.entries(tri).filter(([, v]) => v !== 'inherit').map(([permission, v]) => ({ permission, allow: v === 'allow' }))
      await api(`/admin/users/${user.id}/overrides`, { method: 'PUT', body: { overrides: list } })
      notify('个别权限已保存')
      qc.invalidateQueries({ queryKey: ['admin', 'effective', user.id] })
      onSaved()
    } catch (e) {
      notifyError(e)
    }
  }

  async function revokeAll() {
    try {
      await api(`/admin/users/${user.id}/tokens`, { method: 'DELETE' })
      notify('令牌已全部撤销')
      qc.invalidateQueries({ queryKey: ['admin', 'tokens', user.id] })
    } catch (e) {
      notifyError(e)
    }
  }

  return (
    <Drawer anchor="right" open onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 560 }, p: 2 } } }}>
      <Typography variant="h6" sx={{ mb: 2 }}>{user.displayName}（{user.login}）</Typography>

      <Typography variant="subtitle2" sx={{ mb: 1 }}>基本信息</Typography>
      <Stack spacing={1.5}>
        <Tooltip title={user.nameLocked ? '该用户已登入过或出现在业务记录中：姓名和登入名用于匹配待办与修改记录，不能再修改。需要改名请新建用户并停用旧用户。' : ''}>
          <Box sx={{ display: 'flex', gap: 1.5 }}>
            <TextField label="登入名" value={basic.login} disabled={user.nameLocked} onChange={(e) => setBasic({ ...basic, login: e.target.value })} />
            <TextField label="姓名" value={basic.displayName} disabled={user.nameLocked} onChange={(e) => setBasic({ ...basic, displayName: e.target.value })} />
          </Box>
        </Tooltip>
        <Box sx={{ display: 'flex', gap: 1.5 }}>
          <TextField select label="角色" value={basic.roleId} onChange={(e) => setBasic({ ...basic, roleId: e.target.value })}>
            {roles.data?.map((r) => <MenuItem key={r.id} value={String(r.id)}>{r.name}{r.isReadOnly ? '（只读）' : ''}</MenuItem>)}
          </TextField>
          <TextField select label="职级" value={basic.rank} onChange={(e) => setBasic({ ...basic, rank: e.target.value })}>
            {ranks.data?.map((r) => <MenuItem key={r.name} value={r.name}>{r.name}</MenuItem>)}
          </TextField>
        </Box>
        <Box><Button variant="contained" onClick={saveBasic}>保存基本信息</Button></Box>
      </Stack>

      <Divider sx={{ my: 2 }} />
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="subtitle2">个别权限（在角色之上增加或禁止，禁止优先）</Typography>
        <Button variant="outlined" onClick={saveOverrides}>保存个别权限</Button>
      </Box>
      {groups.map(([group, perms]) => (
        <Accordion key={group} disableGutters variant="outlined">
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography variant="body2">{group}</Typography>
            {perms.some((p) => tri[p.code] && tri[p.code] !== 'inherit') && <Chip sx={{ ml: 1 }} color="secondary" label="有个别设置" />}
          </AccordionSummary>
          <AccordionDetails sx={{ pt: 0 }}>
            {perms.map((p) => (
              <Box key={p.code} sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', py: 0.25 }}>
                <Typography variant="body2" sx={{ color: effectiveMap.has(p.code) ? 'text.primary' : 'text.disabled' }}>
                  {p.label}{p.isSensitive && ' ⚠'}
                </Typography>
                <ToggleButtonGroup size="small" exclusive value={tri[p.code] ?? 'inherit'} onChange={(_, v: Tri | null) => v && setTri({ ...tri, [p.code]: v })}>
                  <ToggleButton value="inherit" sx={{ py: 0, px: 1 }}>继承</ToggleButton>
                  <ToggleButton value="allow" sx={{ py: 0, px: 1 }} color="success">允许</ToggleButton>
                  <ToggleButton value="deny" sx={{ py: 0, px: 1 }} color="error">禁止</ToggleButton>
                </ToggleButtonGroup>
              </Box>
            ))}
          </AccordionDetails>
        </Accordion>
      ))}

      <Divider sx={{ my: 2 }} />
      <Typography variant="subtitle2" sx={{ mb: 1 }}>有效权限（{effective.data?.length ?? 0}）</Typography>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
        {effective.data?.map((e) => (
          <Tooltip key={e.permission} title={`来源：${e.sources.map((s) => ({ role: '角色', 'role+rank': '角色+职级', override: '个别权限' })[s] ?? s).join('、')}`}>
            <Chip label={labelOf(e.permission)} variant={e.sources.includes('override') ? 'filled' : 'outlined'} color={e.sources.includes('override') ? 'secondary' : 'default'} />
          </Tooltip>
        ))}
      </Box>

      <Divider sx={{ my: 2 }} />
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="subtitle2">MCP 令牌</Typography>
        <Button color="error" disabled={!tokens.data?.some((t) => !t.revokedAt)} onClick={revokeAll}>全部撤销</Button>
      </Box>
      {tokens.data?.length ? tokens.data.map((t) => (
        <Typography key={t.id} variant="body2" sx={{ opacity: t.revokedAt ? 0.5 : 1 }}>
          {t.name || '未命名'} · {t.scopes} · 到期 {t.expiresAt.slice(0, 10)}{t.revokedAt ? ' · 已撤销' : ''}
        </Typography>
      )) : <Alert severity="info">没有令牌</Alert>}
    </Drawer>
  )
}
