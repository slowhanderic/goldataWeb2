import { useState } from 'react'
import { Box, Dialog, DialogContent, DialogTitle, TextField, Typography } from '@mui/material'
import { DataGrid, type GridColDef } from '@mui/x-data-grid'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { AdminAuditEntry } from '../../api/types'

const ACTION_LABEL: Record<string, string> = {
  'user.create': '新增用户', 'user.update': '修改用户', 'user.disable': '停用', 'user.enable': '启用', 'user.delete': '删除用户',
  'user.reset_password': '重置密码', 'user.unlock': '解锁', 'user.overrides': '个别权限', 'user.import': '导入用户', 'token.revoke': '撤销令牌',
  'role.create': '新增角色', 'role.update': '修改角色', 'role.delete': '删除角色', 'role.permissions': '修改角色权限',
  'rank.create': '新增职级', 'rank.delete': '删除职级',
}

/** 操作日志 (REQ-ADM-14): every admin action with before/after. */
export default function AuditTab() {
  const [f, setF] = useState({ actor: '', target: '' })
  const [open, setOpen] = useState<AdminAuditEntry | null>(null)
  const log = useQuery({ queryKey: ['admin', 'audit', f], queryFn: () => api<AdminAuditEntry[]>('/admin/audit', { query: f }) })
  const cols: GridColDef<AdminAuditEntry>[] = [
    { field: 'at', headerName: '时间', width: 150, valueFormatter: (v: string) => v.replace('T', ' ').slice(0, 19) },
    { field: 'actorName', headerName: '操作人', width: 110 },
    { field: 'action', headerName: '操作', width: 120, valueFormatter: (v: string) => ACTION_LABEL[v] ?? v },
    { field: 'targetName', headerName: '对象', width: 140 },
    { field: 'afterJson', headerName: '内容', flex: 1, minWidth: 200 },
    { field: 'ip', headerName: 'IP', width: 120 },
  ]
  const pretty = (s: string | null) => {
    if (!s) return '—'
    try { return JSON.stringify(JSON.parse(s), null, 2) } catch { return s }
  }
  return (
    <Box sx={{ p: 1.5 }}>
      <Box sx={{ display: 'flex', gap: 1, mb: 1.5 }}>
        <TextField sx={{ maxWidth: 200 }} label="操作人" value={f.actor} onChange={(e) => setF({ ...f, actor: e.target.value })} />
        <TextField sx={{ maxWidth: 200 }} label="对象" value={f.target} onChange={(e) => setF({ ...f, target: e.target.value })} />
      </Box>
      <Box sx={{ height: 'calc(100vh - 290px)', minHeight: 360 }}>
        <DataGrid density="compact" rows={log.data ?? []} columns={cols} loading={log.isLoading} onRowClick={(p) => setOpen(p.row)} disableRowSelectionOnClick />
      </Box>
      <Dialog open={!!open} onClose={() => setOpen(null)} maxWidth="md" fullWidth>
        <DialogTitle>{open && (ACTION_LABEL[open.action] ?? open.action)} · {open?.targetName}</DialogTitle>
        <DialogContent sx={{ display: 'grid', gridTemplateColumns: { md: '1fr 1fr' }, gap: 2 }}>
          <Box><Typography variant="caption">修改前</Typography><Box component="pre" sx={{ fontSize: 12, bgcolor: 'action.hover', p: 1, overflowX: 'auto' }}>{pretty(open?.beforeJson ?? null)}</Box></Box>
          <Box><Typography variant="caption">修改后</Typography><Box component="pre" sx={{ fontSize: 12, bgcolor: 'action.hover', p: 1, overflowX: 'auto' }}>{pretty(open?.afterJson ?? null)}</Box></Box>
        </DialogContent>
      </Dialog>
    </Box>
  )
}
