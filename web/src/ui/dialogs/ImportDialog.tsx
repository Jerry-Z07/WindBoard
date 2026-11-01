/**
 * 导入对话框（WBIX / WBI）。
 *
 * 桌面权威源：
 * - `WindBoard/UI/MainWindow/MainWindow.Import.cs`（导入入口）；
 * - `WindBoard/Features/Import/ImportFlow.cs:43-117`（选文件 → 类型识别 → 提交）、`:195-380`（WBIX/WBI 实际导入）；
 * - 警告文案取自 `WindBoard/Strings/zh-CN/Import.resw` 的 `Import_Risk_ReplaceWorkspace_Content`：
 *   "选择"覆盖整个工作区"将替换当前所有页面内容。建议在覆盖前先导出备份（WBIX）。"
 *
 * P3 只做 UI：选文件 + 风险提示 + 取消/导入；点"导入"推提醒并关闭（`handleImport` 标了 P4 接入点）。
 */

import { useRef, useState, type ReactNode } from 'react'
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  MessageBar,
  MessageBarBody,
  Text,
} from '@fluentui/react-components'
import { ArrowImportRegular, DismissRegular, FolderOpenRegular } from '@fluentui/react-icons'
import { useReminderService } from '../../app/hooks.ts'

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
  const reminders = useReminderService()
  const inputRef = useRef<HTMLInputElement>(null)
  const [fileName, setFileName] = useState<string | null>(null)

  const handleImport = (): void => {
    if (fileName === null) {
      return
    }

    // ===== P4 接入点：真实导入 =====
    // 桌面 `WindBoard/Features/Import/ImportFlow.cs:43-117` 的顺序：
    //   1) 选文件（`:43-70`）→ 2) 识别类型（`:96-101`，WBIX/WBI 走 `ImportWbixMode`）；
    //   3) 提交后按模式导入：`:195-315` WBIX、`:317-380` WBI（`ReplaceCurrentPage` 时先清当前页，
    //      默认 `AppendAfterLastPage` 追加到末页之后，见 `:100-101`）；
    //   4) 缺资源/解析失败分别提示（`:261`、`:378`）。
    // 网页端届时在这里调用 WBIX 解析（`web/src/persistence/wbix/**`）并替换工作区。
    reminders.push({ message: '导入能力将在 P4 提供', severity: 'informational' })
    props.onClose()
  }

  return (
    <DialogBody>
      <DialogTitle
        action={
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            aria-label="关闭"
            onClick={props.onClose}
          />
        }
      >
        导入
      </DialogTitle>

      <DialogContent className="wb-dialog__form">
        {/* 风险提示：桌面在"覆盖整个工作区"前弹确认（`ImportFlow.cs:398-412`） */}
        <MessageBar intent="warning">
          <MessageBarBody>
            导入 WBIX/WBI 会替换当前工作区内容（所有页面），且无法撤销。建议在覆盖前先导出备份。
          </MessageBarBody>
        </MessageBar>

        <Field label="WBIX / WBI 文件">
          <div className="wb-dialog__file-row">
            {/* 真实文件选择器：只取文件名用于展示，P3 不解析内容 */}
            <input
              ref={inputRef}
              className="wb-dialog__file-input"
              type="file"
              accept=".wbix,.wbi"
              aria-label="选择 WBIX/WBI 文件"
              onChange={(event) => {
                const file = event.target.files?.[0]
                setFileName(file === undefined ? null : file.name)
              }}
            />
            <Button
              icon={<FolderOpenRegular />}
              onClick={() => {
                inputRef.current?.click()
              }}
            >
              选择 WBIX/WBI 文件…
            </Button>
            <Text className="wb-dialog__file-name">{fileName ?? '尚未选择文件'}</Text>
          </div>
        </Field>

        <Text className="wb-dialog__hint">
          仅支持桌面版导出的 .wbix / .wbi；网页端暂不解析文件内容（P4 接入）。
        </Text>
      </DialogContent>

      <DialogActions>
        <Button appearance="secondary" onClick={props.onClose}>
          取消
        </Button>
        <Button
          appearance="primary"
          icon={<ArrowImportRegular />}
          disabled={fileName === null}
          onClick={handleImport}
        >
          导入
        </Button>
      </DialogActions>
    </DialogBody>
  )
}
