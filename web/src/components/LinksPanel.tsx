import { useEffect, useState } from 'react'
import { Link as RouterLink } from 'react-router'
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, Link, Radio, Stack, Switch,
  Table, TableBody, TableCell, TableHead, TableRow, TextField, Tooltip, Typography,
} from '@mui/material'
import LinkOffIcon from '@mui/icons-material/LinkOff'
import AddLinkIcon from '@mui/icons-material/AddLink'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import type { LinkCandidate, LinkGroup } from '../api/types'
import { formatMoney } from '../utils/format'
import { amountSx } from '../theme'
import Confirm from './Confirm'
import { useNotify } from './Notify'
import StatusChip from './StatusChip'

/** Links of a record (REQ-LINK-1/2): outgoing links can be added/removed; incoming allocations are shown. */
export default function LinksPanel({ moduleKey, id }: { moduleKey: string; id: number }) {
  const qc = useQueryClient()
  const { notify, notifyError } = useNotify()
  const q = useQuery({ queryKey: ['links', moduleKey, id], queryFn: () => api<LinkGroup[]>(`/modules/${moduleKey}/${id}/links`) })
  const [adding, setAdding] = useState<LinkGroup | null>(null)
  const [removing, setRemoving] = useState<{ group: LinkGroup; linkId: number; fromId: number; title: string } | null>(null)

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ['links'] })
    qc.invalidateQueries({ queryKey: ['related'] })
    qc.invalidateQueries({ queryKey: ['record', moduleKey, id] })
  }

  async function unlink() {
    const r = removing!
    try {
      await api(`/links/${r.group.type.code}/${r.fromId}`, { method: 'DELETE', query: { linkId: r.group.type.kind === 'share' ? r.linkId : undefined } })
      notify('已解除关联')
      refresh()
    } catch (e) {
      notifyError(e)
    } finally {
      setRemoving(null)
    }
  }

  if (q.isLoading) return null
  const groups = (q.data ?? []).filter((g) => g.direction === 'out' || g.items.length > 0)
  if (groups.length === 0) return null

  return (
    <Stack spacing={2}>
      {groups.map((g) => {
        const out = g.direction === 'out'
        const share = g.type.kind === 'share'
        const unallocated = share && out && g.amount !== null && g.allocated !== null ? Math.round((g.amount - g.allocated) * 100) / 100 : null
        return (
          <Box key={`${g.direction}-${g.type.code}`}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap', mb: 0.5 }}>
              <Typography variant="subtitle2">{out ? `关联${g.type.otherTitle}` : `${g.type.otherTitle}（分摊到本记录）`}</Typography>
              {share && g.allocated !== null && (
                <Typography variant="caption" color="text.secondary" sx={amountSx}>
                  已分摊 {formatMoney(g.allocated)}{g.amount !== null && out ? ` / 金额 ${formatMoney(g.amount)}` : ''}
                </Typography>
              )}
              {unallocated !== null && unallocated !== 0 && (
                <Chip color={unallocated < 0 ? 'error' : 'warning'} label={unallocated < 0 ? `超出 ${formatMoney(-unallocated)}` : `未分摊 ${formatMoney(unallocated)}`} />
              )}
              <Box sx={{ flexGrow: 1 }} />
              {out && g.type.canEdit && (share || g.items.length === 0) && (
                <Button startIcon={<AddLinkIcon />} onClick={() => setAdding(g)}>关联{g.type.otherTitle}</Button>
              )}
              {out && g.type.canEdit && !share && g.items.length > 0 && <Button onClick={() => setAdding(g)}>更改</Button>}
            </Box>
            {g.items.length === 0 ? (
              <Typography variant="body2" color="text.secondary">未关联</Typography>
            ) : (
              <Table size="small">
                <TableBody>
                  {g.items.map((it) => (
                    <TableRow key={`${it.linkId}-${it.otherId}`}>
                      <TableCell><Link component={RouterLink} to={`/m/${g.type.otherModule}/${it.otherId}`}>{it.otherTitle}</Link></TableCell>
                      {share && <TableCell align="right" sx={amountSx}>{formatMoney(it.share)}</TableCell>}
                      <TableCell align="right" width={48}>
                        {g.type.canEdit && (
                          <Tooltip title="解除关联">
                            <IconButton onClick={() => setRemoving({ group: g, linkId: it.linkId, fromId: out ? id : it.otherId, title: it.otherTitle })}>
                              <LinkOffIcon />
                            </IconButton>
                          </Tooltip>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </Box>
        )
      })}
      {adding && <LinkDialog group={adding} fromId={id} onClose={() => setAdding(null)} onDone={() => { setAdding(null); refresh() }} />}
      <Confirm open={!!removing} danger title="解除关联" confirmText="解除" onClose={() => setRemoving(null)} onConfirm={unlink}>
        解除与「{removing?.title}」的关联？{removing?.group.type.kind === 'share' ? '对应的分摊记录将被删除。' : ''}
      </Confirm>
    </Stack>
  )
}

function LinkDialog({ group, fromId, onClose, onDone }: { group: LinkGroup; fromId: number; onClose: () => void; onDone: () => void }) {
  const t = group.type
  const share = t.kind === 'share'
  const [search, setSearch] = useState('')
  const [q, setQ] = useState('')
  const [includeClosed, setIncludeClosed] = useState(false)
  const [picked, setPicked] = useState<LinkCandidate | null>(null)
  const [amount, setAmount] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const h = setTimeout(() => setQ(search), 300)
    return () => clearTimeout(h)
  }, [search])

  const cands = useQuery({
    queryKey: ['link-candidates', t.code, fromId, q, includeClosed],
    queryFn: () => api<LinkCandidate[]>(`/links/${t.code}/candidates`, { query: { fromId, q, includeClosed } }),
  })

  const fromUnallocated = group.amount !== null && group.allocated !== null ? group.amount - group.allocated : null
  function pick(c: LinkCandidate) {
    setPicked(c)
    if (!share) return
    // Default share: what is still open on both sides (REQ-LINK-2).
    const toOpen = c.amount !== null && c.allocated !== null ? c.amount - c.allocated : null
    const open = [fromUnallocated, toOpen].filter((x): x is number => x !== null && x > 0)
    setAmount(open.length ? String(Math.round(Math.min(...open) * 100) / 100) : '')
  }

  async function submit() {
    if (!picked) return
    setBusy(true)
    setError(null)
    try {
      await api(`/links/${t.code}`, { method: 'POST', body: { fromId, toId: picked.id, shareAmount: share ? amount : null } })
      onDone()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : '关联失败')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open onClose={busy ? undefined : onClose} maxWidth="md" fullWidth>
      <DialogTitle>关联{t.otherTitle}</DialogTitle>
      <DialogContent dividers>
        {error && <Alert severity="error" sx={{ mb: 1 }}>{error}</Alert>}
        <Box sx={{ display: 'flex', gap: 2, mb: 1, alignItems: 'center' }}>
          <TextField autoFocus placeholder={`搜索${t.otherTitle}（名称、编号…）`} value={search} onChange={(e) => setSearch(e.target.value)} />
          <FormControlLabel control={<Switch checked={includeClosed} onChange={(e) => setIncludeClosed(e.target.checked)} />} label="含已结案/作废" sx={{ whiteSpace: 'nowrap' }} />
        </Box>
        <Box sx={{ maxHeight: 360, overflowY: 'auto' }}>
          <Table size="small" stickyHeader>
            <TableHead>
              <TableRow>
                <TableCell padding="checkbox" />
                <TableCell>{t.otherTitle}</TableCell>
                <TableCell>状态</TableCell>
                <TableCell align="right">金额</TableCell>
                {share && <TableCell align="right">已分摊</TableCell>}
              </TableRow>
            </TableHead>
            <TableBody>
              {(cands.data ?? []).map((c) => (
                <TableRow key={c.id} hover selected={picked?.id === c.id} sx={{ cursor: c.alreadyLinked ? 'default' : 'pointer', opacity: c.alreadyLinked ? 0.5 : 1 }}
                  onClick={() => !c.alreadyLinked && pick(c)}>
                  <TableCell padding="checkbox"><Radio size="small" checked={picked?.id === c.id} disabled={c.alreadyLinked} /></TableCell>
                  <TableCell>{c.title}{c.alreadyLinked && <Chip sx={{ ml: 1 }} label="已关联" />}</TableCell>
                  <TableCell><StatusChip value={c.status} /></TableCell>
                  <TableCell align="right" sx={amountSx}>{formatMoney(c.amount)}</TableCell>
                  {share && <TableCell align="right" sx={amountSx}>{formatMoney(c.allocated)}</TableCell>}
                </TableRow>
              ))}
              {cands.data?.length === 0 && (
                <TableRow><TableCell colSpan={5}><Typography variant="body2" color="text.secondary">没有找到</Typography></TableCell></TableRow>
              )}
            </TableBody>
          </Table>
        </Box>
        {share && picked && (
          <Box sx={{ mt: 2, display: 'flex', gap: 2, alignItems: 'center' }}>
            <TextField sx={{ maxWidth: 220 }} label={`${t.shareLabel}（￥）`} value={amount} onChange={(e) => setAmount(e.target.value)}
              slotProps={{ htmlInput: { inputMode: 'decimal', style: { textAlign: 'right' } } }} />
            {fromUnallocated !== null && <Typography variant="body2" color="text.secondary">本记录未分摊：{formatMoney(fromUnallocated)}</Typography>}
          </Box>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>取消</Button>
        <Button variant="contained" disabled={!picked || busy} onClick={submit}>关联</Button>
      </DialogActions>
    </Dialog>
  )
}
