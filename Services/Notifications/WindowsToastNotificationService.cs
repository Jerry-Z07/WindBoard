using System;
using System.Diagnostics;
using System.Globalization;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WindBoard.Models.Update;

namespace WindBoard.Services.Notifications
{
    public sealed class WindowsToastNotificationService : INotificationService
    {
        public void ShowUpdateAvailable(UpdateInfo updateInfo)
        {
            if (updateInfo == null) throw new ArgumentNullException(nameof(updateInfo));

            try
            {
                string title = LocalizationService.Instance.GetString("Update_Toast_Title");
                string bodyTemplate = LocalizationService.Instance.GetString("Update_Toast_Body_Format");
                string body = string.Format(CultureInfo.CurrentUICulture, bodyTemplate, updateInfo.VersionName ?? updateInfo.Version);

                XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
                XmlNodeList texts = toastXml.GetElementsByTagName("text");
                if (texts.Count > 0) texts[0].AppendChild(toastXml.CreateTextNode(title));
                if (texts.Count > 1) texts[1].AppendChild(toastXml.CreateTextNode(body));

                var toast = new ToastNotification(toastXml)
                {
                    Tag = "WindBoard.Update"
                };

                ToastNotificationManager.CreateToastNotifier("WindBoard").Show(toast);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] Failed to show toast notification: {ex}");
            }
        }
    }
}

