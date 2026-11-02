/**
 * 设置 → 关于（桌面 `WindBoard/Settings/Pages/AboutSettingsPage.xaml` +
 * `SettingsManagementPage.xaml`）。
 *
 * 网页端保留"版本"与"设置管理"两块；桌面关于页的自动更新检查
 * （`AboutSettingsPage.Updates.cs`）与 Debug 入口按 `research/03-ui-inventory.md` §9 不做。
 * 三张卡（导出/导入/恢复默认）在 P4 全部接通：导出/导入走 `settings-file.ts` 的
 * 「白名单序列化 + 容忍注释/尾逗号解析」，文件读写走 `platform/io.ts`。
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
import { useReminderService, useSettings, useSettingsService } from '../../app/hooks.ts'
import { pickFile, saveFile } from '../../platform/io.ts'
import { SETTINGS_FILE_NAME, parseSettingsFile, serializeSettingsFile } from '../settings-file.ts'
import { SETTINGS_ANCHORS } from '../search-targets.ts'
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
  const settings = useSettings()
  const reminders = useReminderService()
  const [resetConfirmOpen, setResetConfirmOpen] = useState(false)
  const [busy, setBusy] = useState(false)

  /** 导出当前设置到 JSON 文件（桌面 `SettingsManagementPage` 的导出卡片）。 */
  const handleExportSettings = (): void => {
    setBusy(true)
    void (async () => {
      try {
        const text = serializeSettingsFile(settings)
        const result = await saveFile({
          suggestedName: SETTINGS_FILE_NAME,
          mimeType: 'application/json',
          bytes: new TextEncoder().encode(text),
        })
        if (result.ok) {
          reminders.push({ message: `已导出设置：${result.path ?? SETTINGS_FILE_NAME}`, severity: 'informational' })
        } else if (result.cancelled !== true) {
          reminders.push({ message: `导出设置失败：${result.error ?? '未知错误'}`, severity: 'error' })
        }
      } finally {
        setBusy(false)
      }
    })()
  }

  /** 从 JSON 文件导入设置（桌面同卡片；未知字段忽略、非法值回落默认）。 */
  const handleImportSettings = (): void => {
    setBusy(true)
    void (async () => {
      try {
        const picked = await pickFile('.json,application/json')
        if (picked === null) {
          return
        }
        const parsed = parseSettingsFile(new TextDecoder().decode(picked.bytes))
        if (!parsed.ok || parsed.settings === undefined) {
          reminders.push({ message: `导入设置失败：${parsed.error ?? '文件格式不正确'}`, severity: 'error' })
          return
        }
        settingsService.replace(parsed.settings)
        reminders.push({ message: `已导入设置：${picked.name}`, severity: 'informational' })
        for (const note of parsed.notes ?? []) {
          reminders.push({ message: note, severity: 'warning' })
        }
      } finally {
        setBusy(false)
      }
    })()
  }

  return (
    <div className="wb-settings-page">
      <SettingsCard
        title="版本"
        description="网页端版本号（与 package.json 保持一致）"
        anchor={SETTINGS_ANCHORS.aboutVersion}
      >
        <code className="wb-settings-version">{WEB_VERSION}</code>
      </SettingsCard>

      <SettingsSection title="设置管理">
        <SettingsCard
          title="导出设置"
          description="导出当前全部设置到 JSON 文件（与桌面版格式兼容）"
          anchor={SETTINGS_ANCHORS.aboutExportSettings}
        >
          <Button icon={<ArrowExport24Regular />} disabled={busy} onClick={handleExportSettings}>
            导出设置
          </Button>
        </SettingsCard>

        <SettingsCard
          title="导入设置"
          description="从 JSON 文件导入并覆盖当前全部设置（忽略未知字段，非法值回落默认）"
          anchor={SETTINGS_ANCHORS.aboutImportSettings}
        >
          <Button icon={<ArrowImport24Regular />} disabled={busy} onClick={handleImportSettings}>
            导入设置
          </Button>
        </SettingsCard>

        <SettingsCard
          title="恢复默认设置"
          description="清空当前设置并恢复默认值"
          anchor={SETTINGS_ANCHORS.aboutResetSettings}
        >
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
