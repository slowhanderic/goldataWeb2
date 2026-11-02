import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { AppRole, PermissionDef, RankItem } from '../../api/types'

export const useRoles = () => useQuery({ queryKey: ['admin', 'roles'], queryFn: () => api<AppRole[]>('/admin/roles') })
export const useRanks = () => useQuery({ queryKey: ['admin', 'ranks'], queryFn: () => api<RankItem[]>('/admin/ranks') })
export const usePermissionCatalog = () =>
  useQuery({ queryKey: ['admin', 'permissions'], queryFn: () => api<PermissionDef[]>('/admin/permissions'), staleTime: Infinity })

export const ACTIONS: { key: string; label: string }[] = [
  { key: 'read', label: '查看' }, { key: 'create', label: '新增' }, { key: 'update', label: '修改' }, { key: 'delete', label: '删除' },
  { key: 'link', label: '关联' }, { key: 'workflow', label: '转交' }, { key: 'export', label: '导出' }, { key: 'upload', label: '上传' },
  { key: 'file_delete', label: '删附件' },
]

export const STATUS_LABEL: Record<string, string> = { active: '启用', disabled: '停用', locked: '锁定' }

/** Legacy role names that the PHP menu understands (REQ-CMP-8). */
export const LEGACY_ROLES = new Set(['管理', '业务', '财务', '采购', '预算', '家装', '工程文员', '采购查阅', '工程查阅', '预算查阅'])
