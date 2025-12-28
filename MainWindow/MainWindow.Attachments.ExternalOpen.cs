using System.Diagnostics;

namespace WindBoard
{
    public partial class MainWindow
    {
        private void OpenExternal(string pathOrUrl)
        {
            try
            {
                Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
        }

        private bool TryOpenAttachmentExternal(BoardAttachment? attachment)
        {
            if (attachment == null) return false;

            if (attachment.Type == BoardAttachmentType.Video && !string.IsNullOrWhiteSpace(attachment.FilePath))
            {
                OpenExternal(attachment.FilePath);
                return true;
            }

            if (attachment.Type == BoardAttachmentType.Link && !string.IsNullOrWhiteSpace(attachment.Url))
            {
                OpenExternal(attachment.Url);
                return true;
            }

            return false;
        }

        private bool TryOpenAttachmentExternalOnDoubleClick(BoardAttachment? attachment, int clickCount)
            => clickCount >= 2 && TryOpenAttachmentExternal(attachment);
    }
}

