/**
 * 导出对话框（PNG、PDF、WBIX）。桌面权威源 Features/Export/UI/ExportDialog.cs:19-233：字段顺序、默认值、PNG 画面尺寸算法与「仅 PNG / 仅 PDF / 仅位图」的显隐规则都在那里；文案取自 Strings/zh-CN/Export.resw。
 * 纸张与画面尺寸、文件名、页索引、校验等纯逻辑全部在 features/export/export-options.ts。
 */

import { useMemo, useState, type ReactNode } from 'react'
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Option,
  ProgressBar,
  Text,
} from '@fluentui/react-components'
import { ArrowExportRegular, DismissRegular } from '@fluentui/react-icons'
import { useBoardController, useBoardSnapshot, useReminderService, useSettings } from '../../app/hooks.ts'
import { useI18n, type Translator } from '../../i18n/context.ts'
import { formatLocalized } from '../../i18n/message.ts'
import {
  DEFAULT_EXPORT_OPTIONS,
  PDF_DPI_VALUES,
  PNG_HEIGHTS,
  computePngFrameSize,
  formatExportSuggestedFileName,
  formatPngFolderName,
  isExportFormat,
  isExportRange,
  isMultiPagePng,
  isPdfDpi,
  isPngAspect,
  isPngHeight,
  resolvePageIndices,
  validateExportOptions,
} from '../../features/export/export-options.ts'
import type {
  ExportFormat,
  ExportOptions,
  ExportRange,
  PngAspect,
  PngHeight,
} from '../../features/export/export-options.ts'
import { runExport } from '../../features/export/export-runner.ts'

/** 属性。 */
export interface ExportDialogProps {
  readonly open: boolean
  readonly onClose: () => void
}

/** 格式文案的 key（`Export_FileType_Png`、`Export_FileType_Pdf`、`Export_Format_Wbix_WithExt`；`Export.resw:59-70`）。 */
const FORMAT_LABEL_KEYS = {
  png: 'Export_FileType_Png',
  pdf: 'Export_FileType_Pdf',
  wbix: 'Export_Format_Wbix_WithExt',
} as const satisfies Record<ExportFormat, string>

/** 页范围文案的 key（`Export_PageScope_Current/All/Range`；`Export.resw:77-85`）。 */
const RANGE_LABEL_KEYS = {
  current: 'Export_PageScope_Current',
  all: 'Export_PageScope_All',
  range: 'Export_PageScope_Range',
} as const satisfies Record<ExportRange, string>

/** PNG 比例文案的 key（`Export_PngAspect_Square/4_3/16_9`；`Export.resw:104-112`）。 */
const ASPECT_LABEL_KEYS = {
  square: 'Export_PngAspect_Square',
  'ratio-4-3': 'Export_PngAspect_4_3',
  'ratio-16-9': 'Export_PngAspect_16_9',
} as const satisfies Record<PngAspect, string>

/** PDF 清晰度文案的 key（`Export_PdfDpi_Standard/High/Ultra`；`Export.resw:119-127`）。 */
const DPI_LABEL_KEYS = {
  96: 'Export_PdfDpi_Standard',
  192: 'Export_PdfDpi_High',
  288: 'Export_PdfDpi_Ultra',
} as const satisfies Record<number, string>

/** 数字输入：空串视为"未填"（`NaN`），交给 `validateExportOptions` 报错。 */
function parseNumberInput(value: string): number {
  const trimmed = value.trim()
  return trimmed === '' ? Number.NaN : Number(trimmed)
}

/** 数字输入的回显：非有限值显示为空串，避免输入框里出现 "NaN"。 */
function numberInputValue(value: number): string {
  return Number.isFinite(value) ? String(value) : ''
}

/**
 * PNG 高度选项文案（桌面 `ExportDialog.cs:97-114` 的 `UpdatePngSizeItems`：
 * 文案随比例变化，2160 档用 4K 写法，见 `Export_PngSizeItem_Fmt` / `Export_PngSize4kItem_Fmt`）。
 */
function pngHeightLabel(t: Translator, height: PngHeight, aspect: PngAspect): string {
  const frame = computePngFrameSize(aspect, height)
  const width = String(frame.pixelWidth)
  const pixelHeight = String(frame.pixelHeight)
  return height === 2160
    ? t('Export_PngSize4kItem_Fmt', width, pixelHeight)
    : t('Export_PngSizeItem_Fmt', String(height), width, pixelHeight)
}

/** 导出对话框。 */
export function ExportDialog(props: ExportDialogProps): ReactNode {
  return (
    <Dialog
      open={props.open}
      onOpenChange={(_event, data) => {
        if (!data.open) {
          props.onClose()
        }
      }}
    >
      <DialogSurface className="wb-dialog">
        {/* 仅在打开时挂载表单：关闭即卸载，下次打开自动回到默认值（桌面每次都新建 ContentDialog） */}
        {props.open ? <ExportDialogForm onClose={props.onClose} /> : null}
      </DialogSurface>
    </Dialog>
  )
}

/** 表单（挂载即"打开一次"，因此不需要用 effect 重置状态）。 */
function ExportDialogForm(props: { readonly onClose: () => void }): ReactNode {
  const snapshot = useBoardSnapshot()
  const reminders = useReminderService()
  // 真实导出要读工作区快照与外观设置（背景色 / 元素卡主题）
  const controller = useBoardController()
  const settings = useSettings()
  const { t } = useI18n()

  const pageCount = snapshot.pageCount

  // 打开时刻决定默认文件名；表单存活期间保持不变，避免用户看到文件名每帧跳变。
  const [now] = useState(() => new Date())
  const [options, setOptions] = useState<ExportOptions>(() => DEFAULT_EXPORT_OPTIONS)
  const [busy, setBusy] = useState(false)

  const update = (patch: Partial<ExportOptions>): void => {
    setOptions((previous) => ({ ...previous, ...patch }))
  }

  const validation = useMemo(
    () => validateExportOptions(options, pageCount),
    [options, pageCount],
  )
  const pageIndices = useMemo(
    () =>
      resolvePageIndices(
        options.range,
        snapshot.currentPageIndex,
        pageCount,
        options.pageRangeText,
      ),
    [options.range, options.pageRangeText, snapshot.currentPageIndex, pageCount],
  )
  const frame = useMemo(
    () => computePngFrameSize(options.pngAspect, options.pngHeight),
    [options.pngAspect, options.pngHeight],
  )

  const isPng = options.format === 'png'
  const isPdf = options.format === 'pdf'
  const isRaster = options.format !== 'wbix'
  const multiPagePng = isMultiPagePng(options, pageCount)
  const suggestedName = formatExportSuggestedFileName(now, options.format)
  const folderName = formatPngFolderName(now)

  const handleExport = (): void => {
    if (!validation.valid || busy) {
      return
    }

    // 真实导出：桌面顺序见 WindBoard/Features/Export/ExportFlow.cs（快照 → 光栅选项 → WBIX 整库 → 页范围 → 按格式分派），细节在 export-runner.ts。编码与写盘期间留在对话框里显示进度，结束后再关。
    setBusy(true)
    void (async () => {
      try {
        await new Promise<void>((resolve) => {
          setTimeout(resolve, 0)
        })
        const outcome = await runExport(options, {
          controller,
          background: settings.appearance.canvasBackgroundHex,
          elementCardTheme: settings.appearance.elementCardTheme,
          t,
        })
        if (outcome.ok) {
          reminders.push({
            message:
              outcome.message === undefined
                ? t('Export_Completed_Title')
                : formatLocalized(t, outcome.message),
            severity: 'informational',
          })
          return
        }
        // 用户取消不提示（与桌面一致：取消选择器就是静默返回）
        if (outcome.cancelled !== true) {
          reminders.push({
            message:
              outcome.error === undefined ? t('Export_Failed_Title') : formatLocalized(t, outcome.error),
            severity: 'error',
          })
        }
      } catch (error) {
        reminders.push({
          message: `${t('Export_Failed_Title')}：${error instanceof Error ? error.message : String(error)}`,
          severity: 'error',
        })
      } finally {
        setBusy(false)
        props.onClose()
      }
    })()
  }

  return (
    <DialogBody>
      <DialogTitle
        action={
          // `Common_Close` = "关闭"（`Common.resw:50-52`）；桌面 ContentDialog 的关闭按钮由系统提供。
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            aria-label={t('Common_Close')}
            disabled={busy}
            onClick={props.onClose}
          />
        }
      >
        {/* `Export_Dialog_Title`（`Export.resw:20-22`；桌面 `ExportDialog.cs:161`） */}
        {t('Export_Dialog_Title')}
      </DialogTitle>

      <DialogContent className="wb-dialog__form">
        {/* 格式（`ExportDialog.cs:19-28`，下拉三项顺序 PNG / PDF / WBIX） */}
        {/* 标签 `Export_Format_Label`（`Export.resw:71-73`；桌面 `ExportDialog.cs:144`） */}
        <Field label={t('Export_Format_Label')}>
          <Dropdown
            value={t(FORMAT_LABEL_KEYS[options.format])}
            selectedOptions={[options.format]}
            onOptionSelect={(_event, data) => {
              if (isExportFormat(data.optionValue)) {
                update({ format: data.optionValue })
              }
            }}
          >
            <Option value="png" text={t(FORMAT_LABEL_KEYS.png)}>
              {t(FORMAT_LABEL_KEYS.png)}
            </Option>
            <Option value="pdf" text={t(FORMAT_LABEL_KEYS.pdf)}>
              {t(FORMAT_LABEL_KEYS.pdf)}
            </Option>
            <Option value="wbix" text={t(FORMAT_LABEL_KEYS.wbix)}>
              {t(FORMAT_LABEL_KEYS.wbix)}
            </Option>
          </Dropdown>
        </Field>

        {/* 页范围（`ExportDialog.cs:30-45`、`:92-95`） */}
        {/* 标签 `Export_PageScope_Label`（`Export.resw:74-76`；桌面 `ExportDialog.cs:146`） */}
        <Field label={t('Export_PageScope_Label')}>
          <Dropdown
            value={t(RANGE_LABEL_KEYS[options.range])}
            selectedOptions={[options.range]}
            onOptionSelect={(_event, data) => {
              if (isExportRange(data.optionValue)) {
                update({ range: data.optionValue })
              }
            }}
          >
            <Option value="current" text={t(RANGE_LABEL_KEYS.current)}>
              {t(RANGE_LABEL_KEYS.current)}
            </Option>
            <Option value="all" text={t(RANGE_LABEL_KEYS.all)}>
              {t(RANGE_LABEL_KEYS.all)}
            </Option>
            <Option value="range" text={t(RANGE_LABEL_KEYS.range)}>
              {t(RANGE_LABEL_KEYS.range)}
            </Option>
          </Dropdown>
        </Field>

        {/*
          页码范围文本框（`ExportDialog.cs:41-45` 的 `rangeBox`、`:92-95` 的 `UpdateRangeEnabled`）：
          桌面是**始终在面板里**的单个 TextBox，只在 `scope == Range` 时启用（禁用而非隐藏），
          因此这里同样常驻、用 `disabled` 表达。占位符取 `Export_PageRange_Placeholder`（`Export.resw:86-88`）。
          桌面没有可见标签（只有占位符），`Export.resw` 里也没有对应 key；这里的 `Field` 标签是
          网页端为读屏软件关联控件而补的（唯一一处 resw 无出处的可见文案）。
        */}
        <Field label={t('Web_Export_PageRangeLabel')}>
          <Input
            value={options.pageRangeText}
            placeholder={t('Export_PageRange_Placeholder')}
            disabled={options.range !== 'range'}
            onChange={(_event, data) => {
              update({ pageRangeText: data.value })
            }}
          />
        </Field>

        {/* 仅 PNG：画面比例 + 输出分辨率（`ExportDialog.cs:47-61`、`:116-132`） */}
        {/* 标签 `Export_PngAspect_Label`（`Export.resw:89-91`） */}
        {isPng ? (
          <>
            <Field label={t('Export_PngAspect_Label')}>
              <Dropdown
                value={t(ASPECT_LABEL_KEYS[options.pngAspect])}
                selectedOptions={[options.pngAspect]}
                onOptionSelect={(_event, data) => {
                  if (isPngAspect(data.optionValue)) {
                    update({ pngAspect: data.optionValue })
                  }
                }}
              >
                <Option value="square" text={t(ASPECT_LABEL_KEYS.square)}>
                  {t(ASPECT_LABEL_KEYS.square)}
                </Option>
                <Option value="ratio-4-3" text={t(ASPECT_LABEL_KEYS['ratio-4-3'])}>
                  {t(ASPECT_LABEL_KEYS['ratio-4-3'])}
                </Option>
                <Option value="ratio-16-9" text={t(ASPECT_LABEL_KEYS['ratio-16-9'])}>
                  {t(ASPECT_LABEL_KEYS['ratio-16-9'])}
                </Option>
              </Dropdown>
            </Field>

            {/* 标签 `Export_PngSize_Label`（`Export.resw:92-94`） */}
            <Field label={t('Export_PngSize_Label')}>
              <Dropdown
                value={pngHeightLabel(t, options.pngHeight, options.pngAspect)}
                selectedOptions={[String(options.pngHeight)]}
                onOptionSelect={(_event, data) => {
                  const height = Number(data.optionValue)
                  if (isPngHeight(height)) {
                    update({ pngHeight: height })
                  }
                }}
              >
                {PNG_HEIGHTS.map((height) => (
                  <Option key={height} value={String(height)} text={pngHeightLabel(t, height, options.pngAspect)}>
                    {pngHeightLabel(t, height, options.pngAspect)}
                  </Option>
                ))}
              </Dropdown>
            </Field>
          </>
        ) : null}

        {/* 仅 PDF：DPI（`ExportDialog.cs:63-72`、`:178-185`） */}
        {/* 标签 `Export_PdfDpi_Label`（`Export.resw:95-97`） */}
        {isPdf ? (
          <Field label={t('Export_PdfDpi_Label')}>
            <Dropdown
              value={t(DPI_LABEL_KEYS[options.pdfDpi])}
              selectedOptions={[String(options.pdfDpi)]}
              onOptionSelect={(_event, data) => {
                const dpi = Number(data.optionValue)
                if (isPdfDpi(dpi)) {
                  update({ pdfDpi: dpi })
                }
              }}
            >
              {PDF_DPI_VALUES.map((dpi) => (
                <Option key={dpi} value={String(dpi)} text={t(DPI_LABEL_KEYS[dpi])}>
                  {t(DPI_LABEL_KEYS[dpi])}
                </Option>
              ))}
            </Dropdown>
          </Field>
        ) : null}

        {/* 留白：PNG/PDF 共用（`ExportDialog.cs:74-78`、`:130-131`） */}
        {/* 标签 `Export_Padding_Label`、占位符 `Export_Padding_Placeholder`（`Export.resw:98-103`；桌面 `ExportDialog.cs:74-78`） */}
        {isRaster ? (
          <Field label={t('Export_Padding_Label')}>
            <Input
              type="number"
              min={0}
              placeholder={t('Export_Padding_Placeholder')}
              value={numberInputValue(options.paddingDip)}
              onChange={(_event, data) => {
                update({ paddingDip: parseNumberInput(data.value) })
              }}
            />
          </Field>
        ) : null}

        {/* 校验提示（桌面在确认后才弹窗报错，网页端内联提示；规则见 `validateExportOptions`） */}
        {validation.issues.length > 0 ? (
          <MessageBar intent="warning">
            <MessageBarBody>
              {validation.issues.map((issue) => formatLocalized(t, issue)).join('；')}
            </MessageBarBody>
          </MessageBar>
        ) : null}

        {/*
          提示区：导出页数 / PNG 固定画面 / 多页子目录 / WBIX 整库。
          这四条是**网页端独有的动态提示**，`Export.resw` 里没有对应 key（桌面把这些信息放在
          后续的导出流程与结果对话框里，见 `ExportFlow.cs:187-215`、`Export_Completed_File_Fmt`），
          因此文案由网页端自拟；不要当成桌面原文。
        */}
        <Text className="wb-dialog__hint">
          {t('Web_Export_PageCount_Fmt', pageIndices.length)}
          {isPng
            ? t('Web_Export_SizeSuffix_Fmt', frame.pixelWidth, frame.pixelHeight)
            : ''}
        </Text>
        {multiPagePng ? (
          <Text className="wb-dialog__hint">{t('Web_Export_MultiPageFolder_Fmt', folderName)}</Text>
        ) : null}
        {options.format === 'wbix' ? (
          <Text className="wb-dialog__hint">{t('Web_Export_WbixWholeWorkspace')}</Text>
        ) : null}

        {busy ? (
          <>
            <ProgressBar />
            <MessageBar intent="info">
              <MessageBarBody>{t('Export_Busy_Default_Message')}</MessageBarBody>
            </MessageBar>
          </>
        ) : null}

        {/*
          静态提示（桌面 `Export_Hint_Text`，`Export.resw:128-130`；在桌面面板里是最后一个子元素，
          `ExportDialog.cs:85-90` 定义、`:157` 加入 `panel.Children`），放在动态提示之后、文件名之前。
        */}
        <Text className="wb-dialog__hint">{t('Export_Hint_Text')}</Text>

        {/* 默认文件名（桌面 `ExportPickers.cs:43-54`；WBIX 无 WindBoard- 前缀） */}
        <Text className="wb-dialog__filename">{suggestedName}</Text>
      </DialogContent>

      <DialogActions>
        {/* `Common_Cancel` = "取消"（`Common.resw:23-25`；桌面 `ExportDialog.cs:164` 的 `CloseButtonText`） */}
        <Button appearance="secondary" disabled={busy} onClick={props.onClose}>
          {t('Common_Cancel')}
        </Button>
        {/*
          主按钮文案取 `Common_Next` = "下一步"（桌面 `ExportDialog.cs:163` 的 `PrimaryButtonText`）：
          桌面点它之后还要选保存位置，真正的"导出"发生在后续步骤。
          桌面 ContentDialog 本身没有按钮图标，网页端保留既有的 `ArrowExportRegular` 以维持视觉一致。
        */}
        <Button
          appearance="primary"
          icon={<ArrowExportRegular />}
          disabled={!validation.valid || busy}
          onClick={handleExport}
        >
          {t('Common_Next')}
        </Button>
      </DialogActions>
    </DialogBody>
  )
}
