/**
 * 设置 → 关于（桌面 `WindBoard/Settings/Pages/AboutSettingsPage.xaml` +
 * `SettingsManagementPage.xaml`）。
 *
 * 网页端保留"版本"与"设置管理"两块；桌面关于页的自动更新检查
 * （`AboutSettingsPage.Updates.cs`）与 Debug 入口按 `research/03-ui-inventory.md` §9 不做。
 * 导出/导入设置的真实文件能力属 P4，这里按钮置灰并注明；只有"恢复默认设置"
 * 可用（对应桌面 `SettingsManagementPage.xaml` 的二次确认后重置）。
 */

import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
} from '@fluentui/react-components'
import { ArrowExport24Regular, ArrowImport24Regular, ArrowReset24Regular } from '@fluentui/react-icons'
import { useState, type ReactNode } from 'react'
import { useSettingsService } from '../../app/hooks.ts'
import { SettingsCard, SettingsSection } from './SettingsCard.tsx'

/**
 * 网页端版本号。
 *
 * 浏览器里读不到 `package.json`（也没有构建期注入版本号），因此按任务要求写死；
 * 发版时需与 `web/package.json` 的 `version` 手动同步。
 */
const WEB_VERSION = '0.1.0'

/** 关于页。 */
export function AboutPage(): ReactNode {
  const settingsService = useSettingsService()
  const [resetConfirmOpen, setResetConfirmOpen] = useState(false)

  return (
    <div className="wb-settings-page">
      <SettingsCard title="版本" description="网页端版本号（与 package.json 保持一致）">
        <code className="wb-settings-version">{WEB_VERSION}</code>
      </SettingsCard>

      <SettingsSection title="设置管理">
        <SettingsCard title="导出设置" description="导出当前全部设置到 JSON 文件（P4 提供）">
          <Button icon={<ArrowExport24Regular />} disabled>
            导出设置
          </Button>
        </SettingsCard>

        <SettingsCard title="导入设置" description="从 JSON 文件导入并覆盖当前全部设置（P4 提供）">
          <Button icon={<ArrowImport24Regular />} disabled>
            导入设置
          </Button>
        </SettingsCard>

        <SettingsCard title="恢复默认设置" description="清空当前设置并恢复默认值">
          <Button
            appearance="primary"
            icon={<ArrowReset24Regular />}
            onClick={() => {
              setResetConfirmOpen(true)
            }}
          >
            恢复默认设置
          </Button>
        </SettingsCard>
      </SettingsSection>

      {/* 二次确认（桌面 `Settings_About_SettingsManagement_ResetConfirm_*` 文案）。 */}
      <Dialog
        open={resetConfirmOpen}
        modalType="alert"
        onOpenChange={(_, data) => {
          setResetConfirmOpen(data.open)
        }}
      >
        <DialogSurface className="wb-settings-confirm">
          <DialogBody>
            <DialogTitle>确认恢复默认设置</DialogTitle>
            <DialogContent>
              当前全部设置将恢复为默认值，并清空现有设置记录。此操作会立即保存，是否继续？
            </DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={() => {
                  setResetConfirmOpen(false)
                }}
              >
                取消
              </Button>
              <Button
                appearance="primary"
                onClick={() => {
                  settingsService.reset()
                  setResetConfirmOpen(false)
                }}
              >
                恢复默认
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  )
}
