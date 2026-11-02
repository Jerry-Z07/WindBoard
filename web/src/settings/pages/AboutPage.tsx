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
import { useI18n } from '../../i18n/context.ts'
import { formatLocalized } from '../../i18n/message.ts'
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
  const { t } = useI18n()
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
          reminders.push({
            message: t(
              'Settings_About_SettingsManagement_Exported_Fmt',
              result.path ?? SETTINGS_FILE_NAME,
            ),
            severity: 'informational',
          })
        } else if (result.cancelled !== true) {
          reminders.push({
            message: t(
              'Web_Settings_ExportFailed_Fmt',
              result.error === undefined
                ? t('Common_UnknownError')
                : formatLocalized(t, result.error),
            ),
            severity: 'error',
          })
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
          reminders.push({
            message: t(
              'Web_Settings_ImportFailed_Fmt',
              parsed.error === undefined
                ? t('Web_Settings_FileFormatInvalid')
                : formatLocalized(t, parsed.error),
            ),
            severity: 'error',
          })
          return
        }
        settingsService.replace(parsed.settings)
        reminders.push({
          message: t('Settings_About_SettingsManagement_Imported_Fmt', picked.name),
          severity: 'informational',
        })
        for (const note of parsed.notes ?? []) {
          reminders.push({ message: formatLocalized(t, note), severity: 'warning' })
        }
      } finally {
        setBusy(false)
      }
    })()
  }

  return (
    <div className="wb-settings-page">
      <SettingsCard
        title={t('Settings_About_Version')}
        description={t('Web_Settings_Version_Description')}
        anchor={SETTINGS_ANCHORS.aboutVersion}
      >
        <code className="wb-settings-version">{WEB_VERSION}</code>
      </SettingsCard>

      <SettingsSection title={t('Settings_About_SettingsManagement_SectionTitle')}>
        <SettingsCard
          title={t('Settings_About_SettingsManagement_Export_Title')}
          description={t('Web_Settings_ExportSettings_Description')}
          anchor={SETTINGS_ANCHORS.aboutExportSettings}
        >
          <Button icon={<ArrowExport24Regular />} disabled={busy} onClick={handleExportSettings}>
            {t('Settings_About_SettingsManagement_Export_Title')}
          </Button>
        </SettingsCard>

        <SettingsCard
          title={t('Settings_About_SettingsManagement_Import_Title')}
          description={t('Web_Settings_ImportSettings_Description')}
          anchor={SETTINGS_ANCHORS.aboutImportSettings}
        >
          <Button icon={<ArrowImport24Regular />} disabled={busy} onClick={handleImportSettings}>
            {t('Settings_About_SettingsManagement_Import_Title')}
          </Button>
        </SettingsCard>

        <SettingsCard
          title={t('Settings_About_SettingsManagement_Reset_Title')}
          description={t('Settings_About_SettingsManagement_Reset_Description')}
          anchor={SETTINGS_ANCHORS.aboutResetSettings}
        >
          <Button
            appearance="primary"
            icon={<ArrowReset24Regular />}
            onClick={() => {
              setResetConfirmOpen(true)
            }}
          >
            {t('Settings_About_SettingsManagement_Reset_Title')}
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
            <DialogTitle>{t('Settings_About_SettingsManagement_ResetConfirm_Title')}</DialogTitle>
            <DialogContent>{t('Settings_About_SettingsManagement_ResetConfirm_Content')}</DialogContent>
            <DialogActions>
              <Button
                appearance="secondary"
                onClick={() => {
                  setResetConfirmOpen(false)
                }}
              >
                {t('Common_Cancel')}
              </Button>
              <Button
                appearance="primary"
                onClick={() => {
                  settingsService.reset()
                  setResetConfirmOpen(false)
                }}
              >
                {t('Common_ResetToDefault')}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  )
}
