/**
 * 橡皮（像素或整笔加滑动清屏）面板内容，桌面出处 MainWindow.xaml:332-429（EraserFlyout）：外层 StackPanel 宽 232、Spacing 10（:338）；擦除模式卡圆角 14 加 1px 描边，标题 12 号、不透明度 0.85，两个 RadioButton 横向间距 16（:338-382）；滑动清屏条 232×52、圆角 14、文字 15 号 SemiBold，把手 44×44、圆角 22、左边距 6、底色 #F2F2F2、图标 16 号 #6B6B6B（:386-429）；拖到最大位移的 90% 才清屏、松手回弹（UI/Common/ClearCanvasSlideController.cs:12-14,190-196），空页时整条置灰不可拖（UpdateEnabledState）。
 * 桌面用指针捕获加 TranslateTransform 做拖动，网页端用 Pointer Events 加同样的阈值判定，位移用 CSS transform 表达。
 */

import type { PointerEvent as ReactPointerEvent, ReactNode } from 'react'
import { useRef, useState } from 'react'
import { Radio, RadioGroup } from '@fluentui/react-components'
import { DeleteRegular } from '@fluentui/react-icons'
import { useBoardController, useBoardSnapshot } from '../../app/hooks.ts'
import type { EraserMode } from '../../tools/tool-settings.ts'
import { clearSlideProgress, clearSlideThumbMaxOffset, isClearSlideComplete } from '../shell/dock-layout.ts'
import { useI18n } from '../../i18n/context.ts'

/** 属性。 */
export interface EraserFlyoutProps {
  /** 关闭面板（清屏完成后自动收起，与桌面一致）。 */
  readonly onClose: () => void
}

/** 滑动条底板尺寸（`MainWindow.xaml:386`）。 */
const SLIDE_HOST_WIDTH = 232
/** 把手尺寸（`MainWindow.xaml:404-405`）。 */
const THUMB_SIZE = 44

/** 一次拖动过程中的起点信息。 */
interface DragState {
  readonly pointerId: number
  readonly startX: number
  readonly startOffset: number
}

/** 面板内容。 */
export function EraserFlyout(props: EraserFlyoutProps): ReactNode {
  const controller = useBoardController()
  const snapshot = useBoardSnapshot()
  const { t } = useI18n()
  const [offset, setOffset] = useState(0)
  /**
   * 位移的实时值。必须在 ref 里同步维护：pointerup 处理器读的是本次渲染闭包里的 offset，而快速拖动时最后一串 pointermove 与 pointerup 可能落在同一帧内，闭包里的值还没更新，直接用 state 判定阈值会漏掉最后一段位移（表现为拖到底却不清屏）；桌面的把手读的是 TranslateTransform.X（实时值），这里用 ref 对齐同一语义。
   */
  const offsetRef = useRef(0)
  // 渲染期不能读 ref（oxlint `react/refs`），因此按下时把"最大位移"同步进 state，
  // 仅用于 `aria-valuenow`；拖拽过程中的边界仍由 `maxOffset()` 实时计算。
  const [maxOffsetPx, setMaxOffsetPx] = useState(0)
  const hostRef = useRef<HTMLDivElement | null>(null)
  const dragRef = useRef<DragState | null>(null)

  // 空页没有可清的内容，整条置灰（`ClearCanvasSlideController.UpdateEnabledState`）
  const canClear = snapshot.itemCount > 0

  const maxOffset = (): number => {
    const width = hostRef.current?.clientWidth ?? SLIDE_HOST_WIDTH
    return clearSlideThumbMaxOffset(width, THUMB_SIZE)
  }

  const reset = (): void => {
    dragRef.current = null
    offsetRef.current = 0
    setOffset(0)
  }

  const onPointerDown = (event: ReactPointerEvent<HTMLDivElement>): void => {
    if (!canClear || dragRef.current !== null) {
      return
    }
    dragRef.current = { pointerId: event.pointerId, startX: event.clientX, startOffset: offsetRef.current }
    setMaxOffsetPx(maxOffset())
    try {
      event.currentTarget.setPointerCapture(event.pointerId)
    } catch {
      // 指针已失效时 `setPointerCapture` 会抛 NotFoundError（与 `dom-input.ts` 同一处理）：
      // 拖动仍可继续，只是移出把手后收不到事件。
    }
  }

  const onPointerMove = (event: ReactPointerEvent<HTMLDivElement>): void => {
    const drag = dragRef.current
    if (drag === null || drag.pointerId !== event.pointerId) {
      return
    }
    const next = Math.min(Math.max(drag.startOffset + (event.clientX - drag.startX), 0), maxOffset())
    offsetRef.current = next
    setOffset(next)
  }

  const onPointerUp = (event: ReactPointerEvent<HTMLDivElement>): void => {
    const drag = dragRef.current
    if (drag === null || drag.pointerId !== event.pointerId) {
      return
    }
    try {
      if (event.currentTarget.hasPointerCapture(event.pointerId)) {
        event.currentTarget.releasePointerCapture(event.pointerId)
      }
    } catch {
      // 捕获已失效：无需处理。
    }
    // 用实时位移判定，避免闭包里的 state 落后最后一帧（见 `offsetRef` 注释）。
    const reached = isClearSlideComplete(offsetRef.current, maxOffset())
    reset()
    if (reached) {
      controller.clearPage()
      props.onClose()
    }
  }

  const onPointerCancel = (): void => {
    reset()
  }

  return (
    <div className="wb-flyout wb-flyout--eraser">
      <div className="wb-flyout__card">
        <div className="wb-flyout__label">{t('MainWindow_EraserMode_Title')}</div>
        <RadioGroup
          layout="horizontal"
          value={snapshot.eraserMode}
          onChange={(_event, data) => {
            controller.setEraserMode(data.value as EraserMode)
          }}
        >
          <Radio value="pixel" label={t('MainWindow_EraserMode_Point')} />
          <Radio value="stroke" label={t('MainWindow_EraserMode_Stroke')} />
        </RadioGroup>
      </div>

      <div
        ref={hostRef}
        className="wb-slide"
        data-disabled={canClear ? undefined : 'true'}
        style={{ width: SLIDE_HOST_WIDTH }}
      >
        <span className="wb-slide__text">{t('MainWindow_ClearCanvas_Slide')}</span>
        <div
          className="wb-slide__thumb"
          role="slider"
          tabIndex={canClear ? 0 : -1}
          aria-label={t('MainWindow_ClearCanvas_Slide')}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(clearSlideProgress(offset, maxOffsetPx) * 100)}
          aria-disabled={!canClear}
          style={{ transform: `translateX(${offset}px)` }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerCancel}
        >
          <DeleteRegular fontSize={16} />
        </div>
      </div>
    </div>
  )
}
