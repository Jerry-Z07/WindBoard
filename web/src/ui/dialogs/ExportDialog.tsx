/**
 * 导出对话框（PNG / PDF / WBIX）。
 *
 * 桌面权威源：`WindBoard/Features/Export/UI/ExportDialog.cs:19-233`——字段顺序、默认值、
 * PNG 画面尺寸算法与"仅 PNG / 仅 PDF / 仅位图"的显隐规则都在那里；
 * 文案取自 `WindBoard/Strings/zh-CN/Export.resw`。
 *
 * P3 只做 UI：点"导出"后推一条提醒并关闭（`handleExport` 里标了 P4 接入点）。
 * 纸张/画面尺寸、文件名、页索引、校验等**纯逻辑**全部在 `features/export/export-options.ts`。
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
  Text,
} from '@fluentui/react-components'
import { ArrowExportRegular, DismissRegular } from '@fluentui/react-icons'
import { useBoardSnapshot, useReminderService } from '../../app/hooks.ts'
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

/** 属性。 */
export interface ExportDialogProps {
  readonly open: boolean
  readonly onClose: () => void
}

/** 格式文案（桌面 `Export_FileType_Png/Pdf`、`Export_Format_Wbix_WithExt`）。 */
const FORMAT_LABELS: Record<ExportFormat, string> = {
  png: 'PNG 图片',
  pdf: 'PDF 文档',
  wbix: 'WBIX 源文件 (.wbix)',
}

/** 页范围文案（桌面 `Export_PageScope_Current/All/Range`；网页端用起止页输入，故去掉"1,3-5"示例）。 */
const RANGE_LABELS: Record<ExportRange, string> = {
  current: '当前页',
  all: '全部页',
  range: '指定范围',
}

/** PNG 比例文案（桌面 `Export_PngAspect_Square/4_3/16_9`）。 */
const ASPECT_LABELS: Record<PngAspect, string> = {
  square: '1:1（方形）',
  'ratio-4-3': '4:3',
  'ratio-16-9': '16:9',
}

/** PDF 清晰度文案（桌面 `Export_PdfDpi_Standard/High/Ultra`）。 */
const DPI_LABELS: Record<number, string> = {
  96: '标准（96 DPI）',
  192: '高清（192 DPI）',
  288: '超清（288 DPI）',
}

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
function pngHeightLabel(height: PngHeight, aspect: PngAspect): string {
  const frame = computePngFrameSize(aspect, height)
  const size = `${String(frame.pixelWidth)}×${String(frame.pixelHeight)}`
  return height === 2160 ? `4K（${size}）` : `${String(height)}P（${size}）`
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

  const pageCount = snapshot.pageCount
  const currentPage = Math.min(
    Math.max(snapshot.currentPageIndex + 1, 1),
    Math.max(pageCount, 1),
  )

  // 打开时刻决定默认文件名；表单存活期间保持不变，避免用户看到文件名每帧跳变。
  const [now] = useState(() => new Date())
  const [options, setOptions] = useState<ExportOptions>(() => ({
    ...DEFAULT_EXPORT_OPTIONS,
    fromPage: currentPage,
    toPage: currentPage,
  }))

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
        options.fromPage,
        options.toPage,
      ),
    [options.range, options.fromPage, options.toPage, snapshot.currentPageIndex, pageCount],
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
    if (!validation.valid) {
      return
    }

    // ===== P4 接入点：真实导出 =====
    // 桌面 `WindBoard/Features/Export/ExportFlow.cs:43-117` 的顺序：
    //   1) 取工作区快照（`:62-70`，导出期间不再读实时文档）；
    //   2) 组装 `BoardRasterExportOptions`（`:72-77`：DPI、留白、背景色、固定画面）；
    //   3) WBIX 直接导出整库（`:79-84`，忽略页范围）；
    //   4) `resolvePageIndices` 等价逻辑 → 0 基页索引（`:86-91`，本文件已用 `pageIndices` 算好）；
    //   5) 按格式分派：PDF `:169-185`、PNG 单页/多页 `:187-233`（多页走 `WindBoard-{date}` 子目录）。
    // 网页端届时把下面这行替换为真实导出调用即可（文件名用 `suggestedName`）。
    reminders.push({ message: '导出能力将在 P4 提供', severity: 'informational' })
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
        导出
      </DialogTitle>

      <DialogContent className="wb-dialog__form">
        {/* 格式（`ExportDialog.cs:19-28`，下拉三项顺序 PNG / PDF / WBIX） */}
        <Field label="导出格式">
          <Dropdown
            value={FORMAT_LABELS[options.format]}
            selectedOptions={[options.format]}
            onOptionSelect={(_event, data) => {
              if (isExportFormat(data.optionValue)) {
                update({ format: data.optionValue })
              }
            }}
          >
            <Option value="png" text={FORMAT_LABELS.png}>
              {FORMAT_LABELS.png}
            </Option>
            <Option value="pdf" text={FORMAT_LABELS.pdf}>
              {FORMAT_LABELS.pdf}
            </Option>
            <Option value="wbix" text={FORMAT_LABELS.wbix}>
              {FORMAT_LABELS.wbix}
            </Option>
          </Dropdown>
        </Field>

        {/* 页范围（`ExportDialog.cs:30-45`、`:92-95`） */}
        <Field label="导出范围">
          <Dropdown
            value={RANGE_LABELS[options.range]}
            selectedOptions={[options.range]}
            onOptionSelect={(_event, data) => {
              if (isExportRange(data.optionValue)) {
                update({ range: data.optionValue })
              }
            }}
          >
            <Option value="current" text={RANGE_LABELS.current}>
              {RANGE_LABELS.current}
            </Option>
            <Option value="all" text={RANGE_LABELS.all}>
              {RANGE_LABELS.all}
            </Option>
            <Option value="range" text={RANGE_LABELS.range}>
              {RANGE_LABELS.range}
            </Option>
          </Dropdown>
        </Field>

        {options.range === 'range' ? (
          <div className="wb-dialog__range">
            <Field label="起始页">
              <Input
                type="number"
                min={1}
                max={pageCount}
                value={numberInputValue(options.fromPage)}
                onChange={(_event, data) => {
                  update({ fromPage: parseNumberInput(data.value) })
                }}
              />
            </Field>
            <Field label="结束页">
              <Input
                type="number"
                min={1}
                max={pageCount}
                value={numberInputValue(options.toPage)}
                onChange={(_event, data) => {
                  update({ toPage: parseNumberInput(data.value) })
                }}
              />
            </Field>
          </div>
        ) : null}

        {/* 仅 PNG：画面比例 + 输出分辨率（`ExportDialog.cs:47-61`、`:116-132`） */}
        {isPng ? (
          <>
            <Field label="画面比例（PNG）">
              <Dropdown
                value={ASPECT_LABELS[options.pngAspect]}
                selectedOptions={[options.pngAspect]}
                onOptionSelect={(_event, data) => {
                  if (isPngAspect(data.optionValue)) {
                    update({ pngAspect: data.optionValue })
                  }
                }}
              >
                <Option value="square" text={ASPECT_LABELS.square}>
                  {ASPECT_LABELS.square}
                </Option>
                <Option value="ratio-4-3" text={ASPECT_LABELS['ratio-4-3']}>
                  {ASPECT_LABELS['ratio-4-3']}
                </Option>
                <Option value="ratio-16-9" text={ASPECT_LABELS['ratio-16-9']}>
                  {ASPECT_LABELS['ratio-16-9']}
                </Option>
              </Dropdown>
            </Field>

            <Field label="输出分辨率（PNG）">
              <Dropdown
                value={pngHeightLabel(options.pngHeight, options.pngAspect)}
                selectedOptions={[String(options.pngHeight)]}
                onOptionSelect={(_event, data) => {
                  const height = Number(data.optionValue)
                  if (isPngHeight(height)) {
                    update({ pngHeight: height })
                  }
                }}
              >
                {PNG_HEIGHTS.map((height) => (
                  <Option key={height} value={String(height)} text={pngHeightLabel(height, options.pngAspect)}>
                    {pngHeightLabel(height, options.pngAspect)}
                  </Option>
                ))}
              </Dropdown>
            </Field>
          </>
        ) : null}

        {/* 仅 PDF：DPI（`ExportDialog.cs:63-72`、`:178-185`） */}
        {isPdf ? (
          <Field label="清晰度（PDF）">
            <Dropdown
              value={DPI_LABELS[options.pdfDpi]}
              selectedOptions={[String(options.pdfDpi)]}
              onOptionSelect={(_event, data) => {
                const dpi = Number(data.optionValue)
                if (isPdfDpi(dpi)) {
                  update({ pdfDpi: dpi })
                }
              }}
            >
              {PDF_DPI_VALUES.map((dpi) => (
                <Option key={dpi} value={String(dpi)} text={DPI_LABELS[dpi]}>
                  {DPI_LABELS[dpi]}
                </Option>
              ))}
            </Dropdown>
          </Field>
        ) : null}

        {/* 留白：PNG/PDF 共用（`ExportDialog.cs:74-78`、`:130-131`） */}
        {isRaster ? (
          <Field label="内容留白（DIP）">
            <Input
              type="number"
              min={0}
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
            <MessageBarBody>{validation.issues.join('；')}</MessageBarBody>
          </MessageBar>
        ) : null}

        {/* 提示区：导出页数 / PNG 固定画面 / 多页子目录 / WBIX 整库 */}
        <Text className="wb-dialog__hint">
          {`将导出 ${String(pageIndices.length)} 页`}
          {isPng ? `，画面 ${String(frame.pixelWidth)}×${String(frame.pixelHeight)}` : ''}
        </Text>
        {multiPagePng ? (
          <Text className="wb-dialog__hint">
            {`多页 PNG 将导出到 ${folderName} 子目录（桌面 ExportFlow.cs:212-215）`}
          </Text>
        ) : null}
        {options.format === 'wbix' ? (
          <Text className="wb-dialog__hint">WBIX 会导出整个工作区（忽略页范围）。</Text>
        ) : null}

        {/* 默认文件名（桌面 `ExportPickers.cs:43-54`；WBIX 无 WindBoard- 前缀） */}
        <Text className="wb-dialog__filename">{suggestedName}</Text>
      </DialogContent>

      <DialogActions>
        <Button appearance="secondary" onClick={props.onClose}>
          取消
        </Button>
        <Button
          appearance="primary"
          icon={<ArrowExportRegular />}
          disabled={!validation.valid}
          onClick={handleExport}
        >
          导出
        </Button>
      </DialogActions>
    </DialogBody>
  )
}
