import { Link as RouterLink } from 'react-router'
import { Alert, Box, Card, CardContent, CardHeader, Chip, Link, Skeleton, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { TodoItem } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import StatusChip from '../components/StatusChip'

/** 待办事项 (M28): my to-dos, and other people's when permitted (REQ-WF-3). */
export default function HomePage() {
  const { me, can } = useAuth()
  const mine = useQuery({ queryKey: ['todos', 'mine'], queryFn: () => api<TodoItem[]>('/todos') })
  const others = useQuery({ queryKey: ['todos', 'others'], queryFn: () => api<TodoItem[]>('/todos', { query: { scope: 'others' } }), enabled: can('todo:all') })

  const byPerson = new Map<string, TodoItem[]>()
  for (const t of others.data ?? []) byPerson.set(t.personTo, [...(byPerson.get(t.personTo) ?? []), t])

  return (
    <Stack spacing={2}>
      <Typography variant="h6">{me?.displayName}，您好</Typography>
      <Card variant="outlined">
        <CardHeader title={`我的待办（${mine.data?.length ?? 0}）`} slotProps={{ title: { variant: 'subtitle1' } }} />
        <CardContent sx={{ pt: 0 }}>
          {mine.isLoading ? <Skeleton height={80} /> : mine.data?.length ? <TodoList items={mine.data} /> : <Alert severity="success">暂无待办事项</Alert>}
        </CardContent>
      </Card>

      {can('todo:all') && (
        <Card variant="outlined">
          <CardHeader title={`其他人员待办（${others.data?.length ?? 0}）`} slotProps={{ title: { variant: 'subtitle1', color: 'error' } }} />
          <CardContent sx={{ pt: 0 }}>
            {others.isLoading ? <Skeleton height={80} /> : [...byPerson.entries()].map(([person, items]) => (
              <Box key={person} sx={{ mb: 2 }}>
                <Typography variant="subtitle2" sx={{ mb: 0.5 }}>{person}（{items.length}）</Typography>
                <TodoList items={items} />
              </Box>
            ))}
          </CardContent>
        </Card>
      )}
    </Stack>
  )
}

function TodoList({ items }: { items: TodoItem[] }) {
  return (
    <Stack divider={<Box sx={{ borderBottom: 1, borderColor: 'divider' }} />}>
      {items.map((t) => (
        <Box key={t.historyId} sx={{ py: 0.75, display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
          <Chip label={t.moduleTitle} variant="outlined" />
          <Link component={RouterLink} to={`/m/${t.module}/${t.recordId}`} underline="hover" sx={{ fontWeight: 500 }}>{t.title}</Link>
          <StatusChip value={t.statusTo} />
          <Typography variant="body2" color="text.secondary" sx={{ flexGrow: 1, minWidth: 200 }}>
            {t.personFrom} → {t.personTo}{t.comment ? `：${t.comment}` : ''}
          </Typography>
          <Typography variant="caption" color="text.secondary">{t.timestamp}</Typography>
        </Box>
      ))}
    </Stack>
  )
}
