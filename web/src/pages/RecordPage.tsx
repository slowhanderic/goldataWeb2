import { useRef, useState } from 'react'
import { Link as RouterLink, useNavigate, useParams } from 'react-router'
import {
  Alert, Box, Breadcrumbs, Button, Chip, Grid, IconButton, LinearProgress, Link, List, ListItem, ListItemText, Menu, MenuItem,
  Paper, Skeleton, Stack, Tab, Tabs, TextField, Tooltip, Typography,
} from '@mui/material'
import { DataGrid, type GridColDef } from '@mui/x-data-grid'
import DeleteIcon from '@mui/icons-material/DeleteOutlined'
import DownloadIcon from '@mui/icons-material/DownloadOutlined'
import UploadIcon from '@mui/icons-material/UploadFileOutlined'
import EditIcon from '@mui/icons-material/EditOutlined'
import SendIcon from '@mui/icons-material/SendOutlined'
import AddIcon from '@mui/icons-material/Add'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, fileUrl, upload } from '../api/client'
import type { DependentRef, FileEntry, HistoryEntry, LogEntry, ModuleMeta, ParentRef, RecordDetail, RelatedResult } from '../api/types'
import { useModule, useModules } from '../api/useModules'
import { useAuth } from '../auth/AuthContext'
import Confirm from '../components/Confirm'
import { useNotify } from '../components/Notify'
import StatusChip from '../components/StatusChip'
import RecordForm from '../components/RecordForm'
import HandoverDialog from '../components/HandoverDialog'
import LinksPanel from '../components/LinksPanel'
import { formatBytes, formatMoney, formatValue } from '../utils/format'
import { amountSx } from '../theme'

/** Detail page: 基本信息 · 明细 · 附件 · 转交历史 · 修改记录 (002-spec §8.2). Editing comes per module in P1+. */
export default function RecordPage() {
  const { key, id } = useParams()
  const { module, isLoading } = useModule(key)
  if (isLoading) return <Skeleton height={200} />
  if (!module) return <Alert severity="warning">没有权限或模块不存在</Alert>
  return <RecordView key={`${module.key}-${id}`} m={module} id={Number(id)} />
}

function RecordView({ m, id }: { m: ModuleMeta; id: number }) {
  const { can } = useAuth()
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { notify, notifyError } = useNotify()
  const [tab, setTab] = useState('info')
  const [editing, setEditing] = useState(false)
  const [handover, setHandover] = useState(false)
  const [child, setChild] = useState<ModuleMeta | null>(null)
  const [childMenu, setChildMenu] = useState<HTMLElement | null>(null)
  const [deleting, setDeleting] = useState<DependentRef[] | null>(null)
  const modules = useModules()
  const detail = useQuery({ queryKey: ['record', m.key, id], queryFn: () => api<RecordDetail>(`/modules/${m.key}/${id}`) })
  const files = useQuery({ queryKey: ['files', m.key, id], queryFn: () => api<FileEntry[]>(`/modules/${m.key}/${id}/files`), enabled: m.fileSlots.length > 0 })
  const related = useQuery({ queryKey: ['related', m.key, id], queryFn: () => api<RelatedResult>(`/modules/${m.key}/${id}/related`) })

  const reload = () => {
    qc.invalidateQueries({ queryKey: ['record', m.key, id] })
    qc.invalidateQueries({ queryKey: ['history', m.key, id] })
    qc.invalidateQueries({ queryKey: ['logs', m.key, id] })
    qc.invalidateQueries({ queryKey: ['related', m.key, id] })
    qc.invalidateQueries({ queryKey: ['list', m.key] })
    qc.invalidateQueries({ queryKey: ['todos'] })
  }

  async function startDelete() {
    try {
      setDeleting(await api<DependentRef[]>(`/modules/${m.key}/${id}/dependents`))
    } catch (e) {
      notifyError(e)
    }
  }

  async function doDelete() {
    try {
      await api(`/modules/${m.key}/${id}`, { method: 'DELETE' })
      notify('已删除')
      qc.invalidateQueries({ queryKey: ['list', m.key] })
      navigate(`/m/${m.key}`, { replace: true })
    } catch (e) {
      notifyError(e)
      setDeleting(null)
    }
  }

  if (detail.isLoading) return <Skeleton height={300} />
  if (detail.error) return <Alert severity="error">{(detail.error as Error).message}</Alert>
  const d = detail.data!
  const status = m.statusColumn ? d.record[m.statusColumn] : null
  const relatedCount = (related.data?.children ?? []).reduce((n, g) => n + g.total, 0)
  const childModules = m.createChildren.map((c) => ({ link: c, meta: modules.data?.find((x) => x.key === c.module) })).filter((x) => x.meta)

  return (
    <Stack spacing={1.5}>
      <Breadcrumbs>
        <Link component={RouterLink} to={`/m/${m.key}`} underline="hover">{m.title}</Link>
        <Typography color="text.primary">#{id}</Typography>
      </Breadcrumbs>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        <Typography variant="h6">{d.title}</Typography>
        <StatusChip value={status} closedValues={m.closedValues} />
        <Box sx={{ flexGrow: 1 }} />
        {can(`${m.key}:update`) && m.editFields.length > 0 && <Button variant="contained" startIcon={<EditIcon />} onClick={() => setEditing(true)}>编辑</Button>}
        {m.workflow && can(`${m.key}:workflow`) && <Button variant="outlined" startIcon={<SendIcon />} onClick={() => setHandover(true)}>转交</Button>}
        {childModules.length > 0 && (
          <>
            <Button variant="outlined" startIcon={<AddIcon />} onClick={(e) => setChildMenu(e.currentTarget)}>新建关联</Button>
            <Menu anchorEl={childMenu} open={!!childMenu} onClose={() => setChildMenu(null)}>
              {childModules.map(({ link, meta }) => (
                <MenuItem key={link.module} onClick={() => { setChildMenu(null); setChild(meta!) }}>新建{link.title}</MenuItem>
              ))}
            </Menu>
          </>
        )}
        {can(`${m.key}:delete`) && <Button color="error" startIcon={<DeleteIcon />} onClick={startDelete}>删除</Button>}
      </Box>
      <Paper variant="outlined">
        <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable" sx={{ borderBottom: 1, borderColor: 'divider', px: 1 }}>
          <Tab value="info" label="基本信息" />
          {m.hasItems && <Tab value="items" label={`${m.itemsLabel ?? '明细'}（${d.items.length}）`} />}
          <Tab value="related" label={relatedCount > 0 ? `关联记录（${relatedCount}）` : '关联记录'} />
          {m.fileSlots.length > 0 && <Tab value="files" label={`附件（${files.data?.length ?? 0}）`} />}
          {m.workflow && <Tab value="history" label="转交历史" />}
          <Tab value="logs" label="修改记录" />
        </Tabs>
        <Box sx={{ p: 2 }}>
          {tab === 'info' && <InfoGrid m={m} record={d.record} parents={related.data?.parents ?? []} />}
          {tab === 'items' && <ItemsGrid m={m} items={d.items} />}
          {tab === 'related' && (
            <Stack spacing={3}>
              <LinksPanel moduleKey={m.key} id={id} />
              <RelatedPanel related={related.data} />
            </Stack>
          )}
          {tab === 'files' && <FilesPanel m={m} id={id} files={files.data ?? []} />}
          {tab === 'history' && <HistoryPanel m={m} id={id} />}
          {tab === 'logs' && <LogsPanel m={m} id={id} />}
        </Box>
      </Paper>

      {editing && (
        <RecordForm module={m} record={d.record} eTag={d.eTag} onClose={() => setEditing(false)}
          onSaved={() => { setEditing(false); reload() }} onConflict={reload} />
      )}
      {handover && (
        <HandoverDialog module={m} id={id} currentStatus={String(status ?? '')} onClose={() => setHandover(false)}
          onDone={() => { setHandover(false); reload(); setTab('history') }} />
      )}
      {child && (
        <RecordForm module={child} parent={{ module: m.key, id, title: `${m.title}：${d.title}` }} onClose={() => setChild(null)}
          onSaved={(r) => { const k = child.key; setChild(null); reload(); navigate(`/m/${k}/${r.id}`) }} />
      )}
      {deleting && (
        deleting.length > 0 ? (
          <Confirm open title="不能删除" confirmText="知道了" onClose={() => setDeleting(null)} onConfirm={() => setDeleting(null)}>
            <Typography variant="body2" sx={{ mb: 1 }}>该记录仍有以下关联内容，请先解除关联或删除附件：</Typography>
            {deleting.map((x) => <Typography key={`${x.table}.${x.column}`} variant="body2">· {x.moduleTitle ?? x.table}：{x.count} 条</Typography>)}
          </Confirm>
        ) : (
          <Confirm open danger title={`删除${m.title}`} confirmText="删除" onClose={() => setDeleting(null)} onConfirm={doDelete}>
            确定删除「{d.title}」？删除后不能恢复（修改记录中会保留删除日志）。
          </Confirm>
        )
      )}
    </Stack>
  )
}

function RelatedPanel({ related }: { related?: RelatedResult }) {
  if (!related) return <Skeleton height={120} />
  return (
    <Stack spacing={2}>
      {related.children.map((g) => (
        <Box key={`${g.module}-${g.column}`}>
          <Typography variant="subtitle2" sx={{ mb: 0.5 }}>{g.title}（{g.total}）</Typography>
          <List dense disablePadding>
            {g.rows.map((r) => (
              <ListItem key={r.id} divider>
                <ListItemText primary={<Link component={RouterLink} to={`/m/${g.module}/${r.id}`}>{r.title}</Link>} />
                <StatusChip value={r.status} />
              </ListItem>
            ))}
          </List>
          {g.total > g.rows.length && <Typography variant="caption" color="text.secondary">仅显示最新 {g.rows.length} 条</Typography>}
        </Box>
      ))}
    </Stack>
  )
}

function InfoGrid({ m, record, parents }: { m: ModuleMeta; record: Record<string, unknown>; parents: ParentRef[] }) {
  const byColumn = new Map(parents.map((p) => [p.column, p]))
  return (
    <Grid container spacing={1.5}>
      {m.fields.filter((f) => f.kind !== 'json').map((f) => {
        const parent = byColumn.get(f.name)
        return (
          <Grid key={f.name} size={{ xs: 12, sm: 6, md: 4, lg: 3 }}>
            <Typography variant="caption" color="text.secondary">{parent ? parent.label : f.label}</Typography>
            <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word', ...(f.kind === 'money' ? { fontVariantNumeric: 'tabular-nums' } : {}) }}>
              {parent ? <Link component={RouterLink} to={`/m/${parent.module}/${parent.id}`}>{parent.title}</Link>
                : f.name === m.statusColumn ? <StatusChip value={record[f.name]} closedValues={m.closedValues} />
                : formatValue(record[f.name], f.kind) || '—'}
            </Typography>
          </Grid>
        )
      })}
    </Grid>
  )
}

function ItemsGrid({ m, items }: { m: ModuleMeta; items: Record<string, unknown>[] }) {
  const cols: GridColDef[] = m.itemFields
    .filter((f) => f.name !== 'timestamp' && !f.name.endsWith('_id'))
    .map((f) =>
      f.kind === 'money'
        ? { field: f.name, headerName: f.label, type: 'number', minWidth: 90, valueFormatter: (v) => (f.name.includes('qty') ? v : formatMoney(v)), cellClassName: 'amount' }
        : { field: f.name, headerName: f.label, minWidth: 90, flex: f.name === 'item' || f.name === 'item_comment' ? 2 : 1 },
    )
  return (
    <Box sx={{ height: 480, '& .amount': amountSx }}>
      <DataGrid density="compact" rows={items as { id: number }[]} columns={cols} disableRowSelectionOnClick pageSizeOptions={[50, 100]} />
    </Box>
  )
}

function FilesPanel({ m, id, files }: { m: ModuleMeta; id: number; files: FileEntry[] }) {
  const { can } = useAuth()
  const qc = useQueryClient()
  const { notify, notifyError } = useNotify()
  const input = useRef<HTMLInputElement>(null)
  const [slot, setSlot] = useState(m.fileSlots[0]?.tag ?? '')
  const [progress, setProgress] = useState<number | null>(null)
  const [conflict, setConflict] = useState<{ file: File; suggested: string } | null>(null)
  const [toDelete, setToDelete] = useState<FileEntry | null>(null)
  const canUpload = can(`${m.key}:upload`)
  const canDelete = can(`${m.key}:file_delete`)

  async function send(file: File, mode: 'reject' | 'rename') {
    const form = new FormData()
    form.append('file', file)
    setProgress(0)
    try {
      await upload(`/modules/${m.key}/${id}/files`, form, { slot, conflict: mode }, setProgress)
      notify('上传成功')
      qc.invalidateQueries({ queryKey: ['files', m.key, id] })
    } catch (e) {
      if (e instanceof ApiError && e.code === 'FILE_EXISTS') setConflict({ file, suggested: (e.data as { suggestedName: string }).suggestedName })
      else notifyError(e)
    } finally {
      setProgress(null)
      if (input.current) input.current.value = ''
    }
  }

  async function remove(f: FileEntry) {
    try {
      await api(`/files/${f.id}`, { method: 'DELETE' })
      notify('已移到回收区')
      qc.invalidateQueries({ queryKey: ['files', m.key, id] })
    } catch (e) {
      notifyError(e)
    } finally {
      setToDelete(null)
    }
  }

  return (
    <Stack spacing={1.5}>
      {canUpload && (
        <Box
          onDragOver={(e) => e.preventDefault()}
          onDrop={(e) => { e.preventDefault(); const f = e.dataTransfer.files[0]; if (f) void send(f, 'reject') }}
          sx={{ border: '1px dashed', borderColor: 'divider', borderRadius: 1, p: 2, display: 'flex', gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}
        >
          {m.fileSlots.length > 1 && (
            <TextField select label="类别" value={slot} onChange={(e) => setSlot(e.target.value)} sx={{ width: 160 }}>
              {m.fileSlots.map((s) => <MenuItem key={s.tag} value={s.tag}>{s.label}</MenuItem>)}
            </TextField>
          )}
          <Button variant="outlined" startIcon={<UploadIcon />} onClick={() => input.current?.click()} disabled={progress !== null}>选择文件</Button>
          <Typography variant="body2" color="text.secondary">或拖放到此处 · pdf/图片/Office/dwg/dxf/zip/rar，最大 80 MB</Typography>
          <input ref={input} type="file" hidden onChange={(e) => { const f = e.target.files?.[0]; if (f) void send(f, 'reject') }} />
          {progress !== null && <LinearProgress variant="determinate" value={progress} sx={{ width: '100%' }} />}
        </Box>
      )}
      {files.length === 0 ? <Typography color="text.secondary">暂无附件</Typography> : (
        <List dense disablePadding>
          {files.map((f) => (
            <ListItem
              key={f.id}
              divider
              secondaryAction={
                <>
                  <Tooltip title="下载"><span><IconButton href={fileUrl(f.id, true)} disabled={!f.exists}><DownloadIcon /></IconButton></span></Tooltip>
                  {canDelete && <Tooltip title="删除"><IconButton onClick={() => setToDelete(f)}><DeleteIcon /></IconButton></Tooltip>}
                </>
              }
            >
              <ListItemText
                primary={f.exists ? <Link href={fileUrl(f.id)} target="_blank" rel="noopener">{f.name}</Link> : <Typography color="error" variant="body2">{f.name}（文件缺失）</Typography>}
                secondary={<>{m.fileSlots.length > 1 && <Chip label={f.slotLabel} sx={{ mr: 1 }} />}{f.timestamp} · {formatBytes(f.size)}</>}
              />
            </ListItem>
          ))}
        </List>
      )}
      <Confirm
        open={!!conflict}
        title="同名文件已存在"
        confirmText={`另存为「${conflict?.suggested ?? ''}」`}
        onClose={() => setConflict(null)}
        onConfirm={() => { const c = conflict!; setConflict(null); void send(c.file, 'rename') }}
      >
        本月文件夹中已有同名文件。旧系统会直接覆盖，新系统不会覆盖：可以用新名称保存。
      </Confirm>
      <Confirm open={!!toDelete} title="删除附件" danger confirmText="删除" onClose={() => setToDelete(null)} onConfirm={() => toDelete && void remove(toDelete)}>
        「{toDelete?.name}」将移到回收区（pdf_folder/_recycle），需要时管理员可以恢复。
      </Confirm>
    </Stack>
  )
}

function HistoryPanel({ m, id }: { m: ModuleMeta; id: number }) {
  const q = useQuery({ queryKey: ['history', m.key, id], queryFn: () => api<HistoryEntry[]>(`/modules/${m.key}/${id}/history`) })
  if (q.isLoading) return <Skeleton height={120} />
  if (!q.data?.length) return <Typography color="text.secondary">暂无转交记录</Typography>
  return (
    <List dense disablePadding>
      {q.data.map((h) => (
        <ListItem key={h.id} divider alignItems="flex-start">
          <ListItemText
            primary={<>{h.personFrom} → <b>{h.personTo}</b>　<StatusChip value={h.statusTo} /></>}
            secondary={<>{h.timestamp}{h.comment ? ` · ${h.comment}` : ''}</>}
          />
        </ListItem>
      ))}
    </List>
  )
}

function LogsPanel({ m, id }: { m: ModuleMeta; id: number }) {
  const q = useQuery({ queryKey: ['logs', m.key, id], queryFn: () => api<LogEntry[]>(`/modules/${m.key}/${id}/logs`) })
  if (q.isLoading) return <Skeleton height={120} />
  if (!q.data?.length) return <Typography color="text.secondary">暂无修改记录</Typography>
  return (
    <List dense disablePadding>
      {q.data.map((l) => (
        <ListItem key={l.id} divider alignItems="flex-start">
          <ListItemText
            primary={<><Typography component="span" variant="body2" color="secondary">{l.user} @ {l.updateTime}</Typography> {l.tag}</>}
            secondary={<Typography component="span" variant="caption" sx={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>{l.sqlText}</Typography>}
          />
        </ListItem>
      ))}
    </List>
  )
}
