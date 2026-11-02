import { createTheme } from '@mui/material/styles'
import { zhCN } from '@mui/material/locale'
import { zhCN as gridZhCN } from '@mui/x-data-grid/locales'
import { brand } from './brand'

// One theme for the whole app (002-spec §8.0, NFR-UX-2): no page sets its own colours, fonts or spacing.
export const theme = createTheme(
  {
    cssVariables: { colorSchemeSelector: 'class' },
    colorSchemes: {
      light: {
        palette: {
          primary: { main: brand.primary },
          secondary: { main: brand.secondary },
          background: { default: '#F5F6F8', paper: '#FFFFFF' },
        },
      },
      dark: {
        palette: {
          primary: { main: '#7FA7D1' },
          secondary: { main: '#C9A27A' },
        },
      },
    },
    typography: {
      // System Chinese fonts: no web-font download on the LAN. Noto Sans SC (OFL) is the open fallback.
      fontFamily: '"PingFang SC","Microsoft YaHei","Noto Sans SC","Hiragino Sans GB",Roboto,Arial,sans-serif',
      fontSize: 13,
    },
    shape: { borderRadius: 6 },
    spacing: 6,
    components: {
      MuiButton: { defaultProps: { size: 'small', disableElevation: true } },
      MuiTextField: { defaultProps: { size: 'small', fullWidth: true } },
      MuiSelect: { defaultProps: { size: 'small' } },
      MuiAutocomplete: { defaultProps: { size: 'small' } },
      MuiTooltip: { defaultProps: { arrow: true } },
      MuiIconButton: { defaultProps: { size: 'small' } },
      MuiChip: { defaultProps: { size: 'small' } },
    },
  },
  zhCN,
  gridZhCN,
)

/** Amount columns: right-aligned, even-width digits. */
export const amountSx = { fontVariantNumeric: 'tabular-nums', textAlign: 'right' } as const
