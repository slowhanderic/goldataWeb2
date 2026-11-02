import { useMemo, useState } from 'react'
import { Link as RouterLink, Outlet, useLocation, useNavigate } from 'react-router'
import {
  AppBar, Autocomplete, Badge, Box, Collapse, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText,
  ListSubheader, Menu, MenuItem, TextField, Toolbar, Tooltip, Typography, useMediaQuery,
} from '@mui/material'
import { useColorScheme, useTheme } from '@mui/material/styles'
import MenuIcon from '@mui/icons-material/Menu'
import AssignmentIcon from '@mui/icons-material/AssignmentOutlined'
import AccountCircle from '@mui/icons-material/AccountCircle'
import ExpandLess from '@mui/icons-material/ExpandLess'
import ExpandMore from '@mui/icons-material/ExpandMore'
import AdminIcon from '@mui/icons-material/AdminPanelSettingsOutlined'
import SearchIcon from '@mui/icons-material/Search'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { SearchHit, TodoItem } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { brand } from '../theme/brand'

const DRAWER_WIDTH = 220

export default function AppLayout() {
  const theme = useTheme()
  const wide = useMediaQuery(theme.breakpoints.up('lg')) // permanent drawer ≥ 1200 px (NFR-UX-1)
  const [mobileOpen, setMobileOpen] = useState(false)
  const { me, can, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const { mode, setMode } = useColorScheme()
  const [userMenu, setUserMenu] = useState<HTMLElement | null>(null)
  const [closed, setClosed] = useState<Record<string, boolean>>({})

  const todos = useQuery({ queryKey: ['todos', 'mine'], queryFn: () => api<TodoItem[]>('/todos'), refetchInterval: 120_000 })
  const isAdmin = can('users:admin') || can('roles:admin')

  const drawer = useMemo(
    () => (
      <Box sx={{ overflowY: 'auto', height: '100%' }}>
        <Toolbar />
        <List dense>
          <ListItemButton component={RouterLink} to="/" selected={location.pathname === '/'} onClick={() => setMobileOpen(false)}>
            <ListItemIcon sx={{ minWidth: 32 }}><AssignmentIcon fontSize="small" /></ListItemIcon>
            <ListItemText primary="待办事项" />
          </ListItemButton>
        </List>
        <Divider />
        {me?.menu.map((g) => (
          <List
            key={g.group}
            dense
            subheader={
              <ListSubheader
                component="div"
                sx={{ cursor: 'pointer', display: 'flex', justifyContent: 'space-between', alignItems: 'center', lineHeight: '32px' }}
                onClick={() => setClosed((c) => ({ ...c, [g.group]: !c[g.group] }))}
              >
                {g.label}
                {closed[g.group] ? <ExpandMore fontSize="small" /> : <ExpandLess fontSize="small" />}
              </ListSubheader>
            }
          >
            <Collapse in={!closed[g.group]}>
              {g.items.map((m) => (
                <ListItemButton
                  key={m.key}
                  sx={{ pl: 3 }}
                  component={RouterLink}
                  to={`/m/${m.key}`}
                  disabled={!m.generic}
                  selected={location.pathname.startsWith(`/m/${m.key}`)}
                  onClick={() => setMobileOpen(false)}
                >
                  <ListItemText primary={m.title} secondary={m.generic ? undefined : '后续阶段'} />
                </ListItemButton>
              ))}
            </Collapse>
          </List>
        ))}
        {isAdmin && (
          <List dense subheader={<ListSubheader sx={{ lineHeight: '32px' }}>设置</ListSubheader>}>
            <ListItemButton sx={{ pl: 3 }} component={RouterLink} to="/admin" selected={location.pathname.startsWith('/admin')} onClick={() => setMobileOpen(false)}>
              <ListItemIcon sx={{ minWidth: 28 }}><AdminIcon fontSize="small" /></ListItemIcon>
              <ListItemText primary="用户与权限" />
            </ListItemButton>
          </List>
        )}
      </Box>
    ),
    [me, closed, location.pathname, isAdmin],
  )

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <AppBar position="fixed" sx={{ zIndex: (t) => t.zIndex.drawer + 1 }}>
        <Toolbar variant="dense" sx={{ gap: 1 }}>
          {!wide && (
            <IconButton color="inherit" edge="start" onClick={() => setMobileOpen(true)} aria-label="菜单">
              <MenuIcon />
            </IconButton>
          )}
          <Typography variant="h6" component={RouterLink} to="/" sx={{ color: 'inherit', textDecoration: 'none', whiteSpace: 'nowrap', fontSize: 17 }}>
            {brand.appTitle}
          </Typography>
          <Box sx={{ flexGrow: 1, display: 'flex', justifyContent: 'center', px: 2 }}>
            <GlobalSearch onPick={(h) => navigate(`/m/${h.module}/${h.id}`)} />
          </Box>
          <Tooltip title="我的待办">
            <IconButton color="inherit" component={RouterLink} to="/">
              <Badge badgeContent={todos.data?.length ?? 0} color="secondary" max={99}>
                <AssignmentIcon />
              </Badge>
            </IconButton>
          </Tooltip>
          <Tooltip title={`${me?.displayName ?? ''}（${me?.role ?? ''} · ${me?.rank ?? ''}）`}>
            <IconButton color="inherit" onClick={(e) => setUserMenu(e.currentTarget)} aria-label="用户菜单">
              <AccountCircle />
            </IconButton>
          </Tooltip>
          <Menu anchorEl={userMenu} open={!!userMenu} onClose={() => setUserMenu(null)}>
            <MenuItem disabled>{me?.displayName} · {me?.role}{me?.rank ? ` · ${me.rank}` : ''}</MenuItem>
            <Divider />
            <MenuItem onClick={() => { setUserMenu(null); navigate('/change-password') }}>修改密码</MenuItem>
            {can('mcp:read') && <MenuItem onClick={() => { setUserMenu(null); navigate('/tokens') }}>MCP 访问令牌</MenuItem>}
            <MenuItem onClick={() => setMode(mode === 'dark' ? 'light' : 'dark')}>{mode === 'dark' ? '浅色模式' : '深色模式'}</MenuItem>
            <Divider />
            <MenuItem onClick={async () => { setUserMenu(null); await logout(); navigate('/login') }}>退出登入</MenuItem>
          </Menu>
        </Toolbar>
      </AppBar>

      <Box component="nav" sx={{ width: { lg: DRAWER_WIDTH }, flexShrink: { lg: 0 } }}>
        <Drawer
          variant={wide ? 'permanent' : 'temporary'}
          open={wide || mobileOpen}
          onClose={() => setMobileOpen(false)}
          ModalProps={{ keepMounted: true }}
          sx={{ '& .MuiDrawer-paper': { width: DRAWER_WIDTH, boxSizing: 'border-box' } }}
        >
          {drawer}
        </Drawer>
      </Box>

      <Box component="main" sx={{ flexGrow: 1, minWidth: 0, p: { xs: 1.5, md: 2 } }}>
        <Toolbar variant="dense" />
        <Outlet />
      </Box>
    </Box>
  )
}

function GlobalSearch({ onPick }: { onPick: (h: SearchHit) => void }) {
  const [input, setInput] = useState('')
  const q = input.trim()
  const results = useQuery({
    queryKey: ['search', q],
    queryFn: () => api<SearchHit[]>('/search', { query: { q, limit: 5 } }),
    enabled: q.length >= 2,
    staleTime: 30_000,
  })
  return (
    <Autocomplete<SearchHit, false, false, false>
      sx={{ width: '100%', maxWidth: 520, '& .MuiInputBase-root': { bgcolor: 'rgba(255,255,255,0.15)', color: 'inherit' } }}
      options={results.data ?? []}
      loading={results.isFetching}
      filterOptions={(x) => x}
      groupBy={(h) => h.moduleTitle}
      getOptionLabel={(h) => h.title}
      isOptionEqualToValue={(a, b) => a.module === b.module && a.id === b.id}
      noOptionsText={q.length < 2 ? '输入至少 2 个字' : '没有找到'}
      onInputChange={(_, v) => setInput(v)}
      onChange={(_, h) => h && onPick(h)}
      value={null}
      renderOption={(props, h) => (
        <li {...props} key={`${h.module}-${h.id}`}>
          <Typography variant="body2" noWrap>{h.title}</Typography>
          {h.status && <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>{h.status}</Typography>}
        </li>
      )}
      renderInput={(params) => (
        <TextField
          {...params}
          placeholder="搜索项目、合同、订单、供应商…"
          slotProps={{ ...params.slotProps, input: { ...params.slotProps.input, startAdornment: <SearchIcon fontSize="small" sx={{ mr: 0.5, opacity: 0.8 }} /> } }}
        />
      )}
    />
  )
}
