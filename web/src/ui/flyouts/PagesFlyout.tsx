/**
 * 页面管理（缩略图列表 + 删除）面板内容。
 *
 * 桌面出处：`WindBoard/MainWindow.xaml:586-678`（`PagesFlyout`）。
 * - 面板：宽 280、最大高 520、圆角 14、内边距 12（`:603-608`）
 * - 布局：标题行 + 列表，行间距 10（`:609-613`）
 * - 列表项：**宽 240**、圆角 12、1px 描边、内边距 8、下外边距 8（`:633-640`）
 *   （任务描述里写的"行高 240"应是对这条 `Width="240"` 的误读；桌面没有固定行高，
 *   行高由内容撑开：头部 24 + 间距 6 + 缩略图 104 + 内边距 16）
 * - 头部：页码 14 号 SemiBold + 右侧 24×24 删除按钮（圆角 7、图标 12 号）（`:641-663`）
 * - 缩略图：高 104、底色为画布背景（`:665-669`）
 * - 点击切换页、删除按钮删除该页（`UI/MainWindow/MainWindow.Pages.cs` 的 `OnPagesListItemClick` /
 *   `OnDeletePageClicked`）
 *
 * P3 阶段缩略图用"占位方块 + 页码"表示（真实缩略图渲染属 P5）。
 *
 * 本组件只渲染**面板内容**，锚点与开合由 `DockBar` 的 Fluent `Popover` 负责。
 */

import type { ReactNode } from 'react'
import { DeleteRegular } from '@fluentui/react-icons'
import { useBoardController, useBoardSnapshot } from '../../app/hooks.ts'

/** 属性。 */
export interface PagesFlyoutProps {
  /** 关闭面板（桌面点击列表项不关闭面板，删除后也不关闭，因此本组件不调用）。 */
  readonly onClose: () => void
}

/** 面板内容。 */
export function PagesFlyout(props: PagesFlyoutProps): ReactNode {
  // 桌面在页面管理面板里切换/删除页面后保持面板打开（`MainWindow.Pages.cs`），因此这里不调用 onClose
  void props.onClose

  const controller = useBoardController()
  const snapshot = useBoardSnapshot()
  const pages = Array.from({ length: snapshot.pageCount }, (_unused, index) => index)

  const selectPage = (index: number): void => {
    if (index !== snapshot.currentPageIndex) {
      controller.setCurrentPageIndex(index)
    }
  }

  const deletePage = (index: number): void => {
    // 控制器只暴露"删除当前页"，因此先切到目标页再删（桌面 `RemovePage(page)` 会自行修正当前下标，
    // 网页端由 `BoardWorkspace.removePage` 把当前下标夹到 `min(index, count - 1)`，结果一致）
    selectPage(index)
    controller.removeCurrentPage()
  }

  return (
    <div className="wb-flyout wb-flyout--panel wb-flyout--pages">
      <div className="wb-flyout__heading">页面管理</div>
      <div className="wb-pages" role="listbox" aria-label="页面管理">
        {pages.map((index) => {
          const selected = index === snapshot.currentPageIndex
          return (
            <div
              key={index}
              className="wb-pages__row"
              data-selected={selected ? 'true' : undefined}
              role="option"
              aria-selected={selected}
              tabIndex={0}
              onClick={() => {
                selectPage(index)
              }}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault()
                  selectPage(index)
                }
              }}
            >
              <div className="wb-pages__head">
                <span className="wb-pages__number">{index + 1}</span>
                <button
                  type="button"
                  className="wb-pages__delete"
                  title="删除该页"
                  aria-label={`删除第 ${index + 1} 页`}
                  onClick={(event) => {
                    // 阻止冒泡，避免删除时同时触发"切换页"
                    event.stopPropagation()
                    deletePage(index)
                  }}
                >
                  <DeleteRegular fontSize={12} />
                </button>
              </div>
              <div className="wb-pages__thumb" aria-hidden>
                <span className="wb-pages__thumb-number">{index + 1}</span>
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
