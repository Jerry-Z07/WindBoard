using WindBoard.Models.Update;

namespace WindBoard.Services.Notifications
{
    public interface INotificationService
    {
        void ShowUpdateAvailable(UpdateInfo updateInfo);
    }
}

