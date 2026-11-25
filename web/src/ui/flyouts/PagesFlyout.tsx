/**
 * 页面管理（缩略图列表加删除）面板内容，桌面出处 MainWindow.xaml:586-678（PagesFlyout）：面板宽 280、最大高 520、圆角 14、内边距 12（:603-608）；标题行加列表、行间距 10（:609-613）；列表项宽 240、圆角 12、1px 描边、内边距 8、下外边距 8（:633-640），桌面没有固定行高，由内容撑开（头部 24 加间距 6 加缩略图 104 加内边距 16）；头部为 14 号 SemiBold 页码加右侧 24×24 删除按钮（圆角 7、图标 12 号，:641-663）；缩略图高 104、底色为画布背景（:665-669）；点击切换页、点删除按钮删页（MainWindow.Pages.cs 的 OnPagesListItemClick / OnDeletePageClicked）。
 * 缩略图由 PageThumbnail 用 Canvas2D 绘制（算法见 render/thumbnail.ts，照搬桌面 PageThumbnailControl 的折线近似）；本组件只渲染面板内容，锚点与开合由 DockBar 的 Fluent Popover 负责。
 */

import type { ReactNode } from 'react'
import { DeleteRegular } from '@fluentui/react-icons'
import { useBoardController, useBoardSnapshot, useSettings } from '../../app/hooks.ts'
import { PageThumbnail } from '../shell/PageThumbnail.tsx'
import { useI18n } from '../../i18n/context.ts'

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
  const settings = useSettings()
  const { t } = useI18n()
  const pages = Array.from({ length: snapshot.pageCount }, (_unused, index) => index)
  // 缩略图尺寸：列表项宽 240 减去左右内边距 8×2（`MainWindow.xaml:633-640,665`），高固定 104
  const thumbnailWidth = 224
  const thumbnailHeight = 104
  const canvasBackground = settings.appearance.canvasBackgroundHex

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
      <div className="wb-flyout__heading">{t('MainWindow_PageManager_Title')}</div>
      <div className="wb-pages" role="listbox" aria-label={t('MainWindow_PageManager_Title')}>
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
                  title={t('Web_DeletePage')}
                  aria-label={t('Web_DeletePageNumber_Fmt', index + 1)}
                  onClick={(event) => {
                    // 阻止冒泡，避免删除时同时触发"切换页"
                    event.stopPropagation()
                    deletePage(index)
                  }}
                >
                  <DeleteRegular fontSize={12} />
                </button>
              </div>
              <div className="wb-pages__thumb">
                <PageThumbnail
                  items={controller.workspace.pages[index].session.document.items}
                  widthDip={thumbnailWidth}
                  heightDip={thumbnailHeight}
                  background={canvasBackground}
                />
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
