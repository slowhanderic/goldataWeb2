import { useState } from 'react'
import { Alert, Paper, Stack, Tab, Tabs, Typography } from '@mui/material'
import { useAuth } from '../../auth/AuthContext'
import UsersTab from './UsersTab'
import RolesTab from './RolesTab'
import AuditTab from './AuditTab'

/** 设置 ▸ 用户与权限 (M29, 002-spec §8.3). Each tab needs its own permission. */
export default function AdminPage() {
  const { can } = useAuth()
  const tabs = [
    can('users:admin') && { key: 'users', label: '用户' },
    can('roles:admin') && { key: 'roles', label: '角色与权限' },
    (can('users:admin') || can('roles:admin')) && { key: 'audit', label: '操作日志' },
  ].filter(Boolean) as { key: string; label: string }[]
  const [tab, setTab] = useState(tabs[0]?.key ?? '')
  if (!tabs.length) return <Alert severity="warning">权限不足</Alert>
  return (
    <Stack spacing={1.5}>
      <Typography variant="h6">用户与权限</Typography>
      <Paper variant="outlined">
        <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ borderBottom: 1, borderColor: 'divider', px: 1 }}>
          {tabs.map((t) => <Tab key={t.key} value={t.key} label={t.label} />)}
        </Tabs>
        {tab === 'users' && <UsersTab />}
        {tab === 'roles' && <RolesTab />}
        {tab === 'audit' && <AuditTab />}
      </Paper>
    </Stack>
  )
}
