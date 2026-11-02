import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router'
import {
  Alert, Box, Button, FormControlLabel, MenuItem, Paper, Stack, Switch, TextField, Typography,
} from '@mui/material'
import { DataGrid, type GridColDef, type GridColumnVisibilityModel, type GridSortModel } from '@mui/x-data-grid'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { FieldMeta, ListResult, ModuleMeta } from '../api/types'
import { useModule } from '../api/useModules'
import StatusChip from '../components/StatusChip'
import RecordForm from '../components/RecordForm'
import { useAuth } from '../auth/AuthContext'
import AddIcon from '@mui/icons-material/Add'
import { formatMoney } from '../utils/format'
import { amountSx } from '../theme'

/** Generic list page (REQ-LIST-1…6): server paging/sorting, search, year, closed toggle, server totals. */
export default function ModuleListPage() {
  const { key } = useParams()
  const { module, isLoading } = useModule(key)
  if (isLoading) return null
  if (!module) return <Alert severity="warning">没有权限或模块不存在</Alert>
  return <ModuleList key={module.key} module={module} />
}

function hiddenByDefault(f: FieldMeta, m: ModuleMeta): boolean {
  if (m.titleColumns.includes(f.name) || f.name === m.statusColumn || f.name === m.yearColumn || m.totals.includes(f.name)) return false
  return f.name === 'timestamp' || f.name === 'comment' || f.name.endsWith('_id') || f.kind === 'json' || f.name.startsWith('pdf_')
}

function ModuleList({ module: m }: { module: ModuleMeta }) {
  const navigate = useNavigate()
  const { can } = useAuth()
  const [creating, setCreating] = useState(false)
  const [params, setParams] = useSearchParams()
  const page = Number(params.get('page') ?? 1)
  const pageSize = Number(params.get('pageSize') ?? 50)
  const sort = params.get('sort') ?? ''
  const q = params.get('q') ?? ''
  const year = params.get('year') ?? ''
  const includeClosed = params.get('includeClosed') === 'true'
  const [search, setSearch] = useState(q)

  // Filter-bar state lives in the URL (shareable, back button works).
  const update = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(patch)) {
      if (v === null || v === '') next.delete(k)
      else next.set(k, v)
    }
    setParams(next, { replace: true })
  }

  useEffect(() => {
    const t = setTimeout(() => search !== q && update({ q: search, page: null }), 400)
    return () => clearTimeout(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search])

  const years = useQuery({ queryKey: ['years', m.key], queryFn: () => api<number[]>(`/modules/${m.key}/years`), enabled: !!m.yearColumn, staleTime: 600_000 })
  const list = useQuery({
    queryKey: ['list', m.key, page, pageSize, sort, q, year, includeClosed],
    queryFn: () => api<ListResult>(`/modules/${m.key}`, { query: { page, pageSize, sort, q, year, includeClosed } }),
    placeholderData: keepPreviousData,
  })

  const visibilityKey = `grid-cols:${m.key}`
  const [visibility, setVisibility] = useState<GridColumnVisibilityModel>(() => {
    try {
      const saved = localStorage.getItem(visibilityKey)
      if (saved) return JSON.parse(saved) as GridColumnVisibilityModel
    } catch { /* private window etc. */ }
    return Object.fromEntries(m.fields.filter((f) => hiddenByDefault(f, m)).map((f) => [f.name, false]))
  })

  const columns = useMemo<GridColDef[]>(
    () =>
      m.fields.map((f): GridColDef => {
        const base: GridColDef = { field: f.name, headerName: f.label, minWidth: 90, flex: m.titleColumns[0] === f.name ? 2 : 1 }
        if (f.name === m.statusColumn) return { ...base, minWidth: 110, renderCell: (p) => <StatusChip value={p.value} closedValues={m.closedValues} /> }
        if (f.kind === 'money') return { ...base, type: 'number', align: 'right', headerAlign: 'right', valueFormatter: (v) => formatMoney(v), cellClassName: 'amount' }
        if (f.kind === 'id' || f.kind === 'int') return { ...base, minWidth: 70, flex: 0.5 }
        if (f.kind === 'date') return { ...base, minWidth: 100, flex: 0.7 }
        return base
      }),
    [m],
  )

  const sortModel: GridSortModel = sort
    ? sort.split(',').map((s) => ({ field: s.replace(/^-/, ''), sort: s.startsWith('-') ? 'desc' : 'asc' }))
    : []

  const totals = list.data?.totals ?? {}
  return (
    <Stack spacing={1.5}>
      <Box sx={{ display: 'flex', alignItems: 'center' }}>
        <Typography variant="h6" sx={{ flexGrow: 1 }}>{m.title}</Typography>
        {can(`${m.key}:create`) && m.editFields.length > 0 && (
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>新增{m.title}</Button>
        )}
      </Box>
      <Paper variant="outlined" sx={{ p: 1.5, display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
        <TextField sx={{ maxWidth: 320 }} placeholder="搜索（名称、编号、备注…）" value={search} onChange={(e) => setSearch(e.target.value)} />
        {m.yearColumn && (
          <TextField select sx={{ width: 120 }} label="年份" value={year} onChange={(e) => update({ year: e.target.value, page: null })}>
            <MenuItem value="">全部</MenuItem>
            {years.data?.map((y) => <MenuItem key={y} value={String(y)}>{y}</MenuItem>)}
          </TextField>
        )}
        {m.closedValues.length > 0 && (
          <FormControlLabel
            control={<Switch checked={includeClosed} onChange={(e) => update({ includeClosed: e.target.checked ? 'true' : null, page: null })} />}
            label={`显示${m.closedValues.join('、')}记录`}
          />
        )}
      </Paper>

      {list.error && <Alert severity="error">{(list.error as Error).message}</Alert>}
      <Paper variant="outlined" sx={{ height: 'calc(100vh - 250px)', minHeight: 400, '& .amount': amountSx }}>
        <DataGrid
          density="compact"
          rows={list.data?.rows ?? []}
          columns={columns}
          loading={list.isFetching}
          rowCount={list.data?.total ?? 0}
          paginationMode="server"
          sortingMode="server"
          filterMode="server"
          disableColumnFilter
          disableRowSelectionOnClick
          pageSizeOptions={[25, 50, 100, 200]}
          paginationModel={{ page: page - 1, pageSize }}
          onPaginationModelChange={(pm) => update({ page: String(pm.page + 1), pageSize: String(pm.pageSize) })}
          sortModel={sortModel}
          onSortModelChange={(sm) => update({ sort: sm.map((s) => (s.sort === 'desc' ? '-' : '') + s.field).join(',') || null, page: null })}
          columnVisibilityModel={visibility}
          onColumnVisibilityModelChange={(v) => {
            setVisibility(v)
            try { localStorage.setItem(visibilityKey, JSON.stringify(v)) } catch { /* ignore */ }
          }}
          onRowClick={(p) => navigate(`/m/${m.key}/${p.id}`)}
          sx={{ border: 0, '& .MuiDataGrid-row': { cursor: 'pointer' } }}
        />
      </Paper>
      {creating && (
        <RecordForm module={m} onClose={() => setCreating(false)} onSaved={(r) => { setCreating(false); navigate(`/m/${m.key}/${r.id}`) }} />
      )}
      {m.totals.length > 0 && list.data && (
        <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap', px: 1 }}>
          <Typography variant="body2" color="text.secondary">共 {list.data.total} 条，合计：</Typography>
          {m.totals.map((t) => (
            <Typography key={t} variant="body2" sx={amountSx}>
              {m.fields.find((f) => f.name === t)?.label ?? t}：<b>{formatMoney(totals[t] ?? 0)}</b>
            </Typography>
          ))}
        </Box>
      )}
    </Stack>
  )
}
