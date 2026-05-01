# Fix imported text element size/display mismatch

## Goal
Fix the display issue for imported text elements on the board so that text reflows when the element size changes and the content never draws outside the frame.

## Requirements
- Text elements should show more text after they are enlarged
- When text elements are shrunk, text drawing should remain clipped to the element boundary
- Keep the existing import model and element size data structures unchanged
- Limit the change scope to rendering and test code directly related to the issue

## Acceptance Criteria
- [ ] After importing a text element, resizing it causes the text to reflow/display based on the current frame width and height
- [ ] Text content no longer draws outside the element boundary
- [ ] Existing import-related tests continue to pass
- [ ] Add a regression test that covers the preview no longer being fixed at 160 characters

## Technical Notes
- `SizeWorld` already exists in the import flow; focus on `BoardSceneRenderer`
- Direct2D text is not clipped to the layout rect by default and requires explicit clipping
