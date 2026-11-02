/**
 * 导入对话框（WBIX）。
 *
 * 桌面权威源：
 * - `WindBoard/UI/MainWindow/MainWindow.Import.cs:18-51`（导入入口）；
 * - `WindBoard/Features/Import/ImportFlow.cs:42-60`（选文件 → 提交）、`:99-101`（选中工作区文件时一律
 *   `AppendAfterLastPage`）、`:206-256`（解析 + 追加页 + 切当前页）、`:258-262`（失败提示）；
 * - 文案取自 `WindBoard/Strings/zh-CN/Import.resw` 与 `ImportDialog.resw`（下方常量逐条标注 key）。
 *
 * 纯逻辑在 `features/import/wbix-import.ts`：本组件只负责「选文件 → 解析 → 应用到工作区 → 提示」。
 */

import { useState, type ReactNode } from 'react'
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  MessageBar,
  MessageBarBody,
  Text,
} from '@fluentui/react-components'
import { ArrowImportRegular, DismissRegular, FolderOpenRegular } from '@fluentui/react-icons'
import { useBoardController, useReminderService } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import { applyImportedWorkspace, parseWbixForImport } from '../../features/import/wbix-import.ts'
import { pickFile } from '../../platform/io.ts'

/**
 * 文案（逐条对应 resw key，未自造）：
 * - `Import_Wbix_Dialog_Title`：导入 WBIX/WBI
 * - `ImportDialog_PickWbix`：选择 WBIX/WBI 文件…
 * - `Import_Wbix_InsertMethod_Label` + `Import_Wbix_Mode_Append`：插入方式：/ 新增在最后一页之后
 * - `Import_Failed_Title`：导入失败
 * - `Import_Wbix_ParseFailed_Message`：WBIX/WBI 文件解析失败。
 * - `Import_Wbix_Busy_Message`：正在导入，请稍候…
 * - `Import_Tip_Title`：导入提示
 * - `ImportDialog_FilePicker_Failed_Fmt`：打开文件选择器失败：{0}
 */
/**
 * 文件选择器的 accept 过滤。
 *
 * 只收 `.wbix`：桌面还支持旧格式 `.wbi`（`WbiWorkspaceImporter`），但 web 侧只移植了 WBIX 读取，
 * 允许选中 `.wbi` 只会得到一条"解析失败"，不如在选择器里就过滤掉。
 */
const WBIX_ACCEPT = '.wbix'

/** 属性。 */
export interface ImportDialogProps {
  readonly open: boolean
  readonly onClose: () => void
}

/** 导入对话框。 */
export function ImportDialog(props: ImportDialogProps): ReactNode {
  return (
    <Dialog
      open={props.open}
      onOpenChange={(_event, data) => {
        if (!data.open) {
          props.onClose()
        }
      }}
    >
      <DialogSurface className="wb-dialog wb-dialog--import">
        {/* 仅在打开时挂载：关闭即卸载，下次打开不会残留上次选中的文件 */}
        {props.open ? <ImportDialogForm onClose={props.onClose} /> : null}
      </DialogSurface>
    </Dialog>
  )
}

/** 表单（挂载即"打开一次"）。 */
function ImportDialogForm(props: { readonly onClose: () => void }): ReactNode {
  const controller = useBoardController()
  const reminders = useReminderService()
  const { t } = useI18n()
  const [fileName, setFileName] = useState<string | null>(null)
  const [fileBytes, setFileBytes] = useState<Uint8Array | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  /** 选文件（桌面 `ImportFlow.cs:52-56`；取消选择不改变当前状态）。 */
  const handlePickFile = async (): Promise<void> => {
    setError(null)
    try {
      const picked = await pickFile(WBIX_ACCEPT)
      if (picked === null) {
        return
      }

      setFileName(picked.name)
      setFileBytes(picked.bytes)
    } catch (cause) {
      setError(t('ImportDialog_FilePicker_Failed_Fmt', errorMessage(cause)))
    }
  }

  /** 解析并应用（桌面 `ImportFlow.cs:206-256`）。 */
  const handleImport = async (): Promise<void> => {
    if (fileBytes === null || busy) {
      return
    }

    setBusy(true)
    setError(null)
    try {
      // 解析是同步的 CPU 工作（解压 + JSON），先让出一次事件循环，"正在导入"才能渲染出来。
      await new Promise<void>((resolve) => {
        setTimeout(resolve, 0)
      })

      const imported = parseWbixForImport(fileBytes)
      applyImportedWorkspace(controller.workspace, imported)

      // 桌面把容忍告警只写日志（`AppLog.Warn`）；网页端没有日志层，用提醒把被跳过的内容告诉用户。
      if (imported.warnings.length > 0) {
        reminders.push({
          title: t('Import_Tip_Title'),
          message: summarizeWarnings(imported.warnings),
          severity: 'warning',
        })
      }

      props.onClose()
    } catch {
      // 桌面 `ImportFlow.cs:258-262`：任何导入异常都显示同一条解析失败文案（异常细节只进日志）。
      setError(t('Import_Wbix_ParseFailed_Message'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <DialogBody>
      <DialogTitle
        action={
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            aria-label={t('Common_Close')}
            disabled={busy}
            onClick={props.onClose}
          />
        }
      >
        {t('Import_Wbix_Dialog_Title')}
      </DialogTitle>

      <DialogContent className="wb-dialog__form">
        <div className="wb-dialog__file-row">
          <Button
            icon={<FolderOpenRegular />}
            disabled={busy}
            onClick={() => {
              void handlePickFile()
            }}
          >
            {t('ImportDialog_PickWbix')}
          </Button>
          <Text className="wb-dialog__file-name">{fileName ?? '尚未选择文件'}</Text>
        </div>

        {/* 桌面 WBIX 导入只有"追加"一条活路径（`ImportFlow.cs:99-101`），这里把模式说明摆在明面上。 */}
        <Text className="wb-dialog__hint">
          {t('Import_Wbix_InsertMethod_Label')}
          {t('Import_Wbix_Mode_Append')}
        </Text>

        {busy ? (
          <MessageBar intent="info">
            <MessageBarBody>{t('Import_Wbix_Busy_Message')}</MessageBarBody>
          </MessageBar>
        ) : null}

        {error === null ? null : (
          <MessageBar intent="error">
            <MessageBarBody>
              {t('Import_Failed_Title')}：{error}
            </MessageBarBody>
          </MessageBar>
        )}
      </DialogContent>

      <DialogActions>
        <Button appearance="secondary" disabled={busy} onClick={props.onClose}>
          {t('Common_Cancel')}
        </Button>
        <Button
          appearance="primary"
          icon={<ArrowImportRegular />}
          disabled={fileBytes === null || busy}
          onClick={() => {
            void handleImport()
          }}
        >
          {t('Common_Import')}
        </Button>
      </DialogActions>
    </DialogBody>
  )
}

/** 取异常的可读信息。 */
function errorMessage(cause: unknown): string {
  return cause instanceof Error ? cause.message : String(cause)
}

/** 汇总容忍告警（最多 3 条，其余用省略号收尾；不添加任何自造文案）。 */
function summarizeWarnings(warnings: readonly string[]): string {
  const head = warnings.slice(0, 3).join('；')
  return warnings.length > 3 ? `${head}…` : head
}
