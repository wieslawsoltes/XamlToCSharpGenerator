using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using ControlCatalog.ViewModels;

namespace ControlCatalog.Pages
{
    public partial class NotificationsPage : UserControl
    {
        private NotificationViewModel _viewModel;

        public NotificationsPage()
        {
            this.InitializeComponent();

            _viewModel = new NotificationViewModel();

            DataContext = _viewModel;
        }

        private void InitializeComponent()
        {
            InitializeComponent(true);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _viewModel.NotificationManager = new WindowNotificationManager(TopLevel.GetTopLevel(this)!);
        }

        public void NotificationOnClick()
        {
            this.Get<WindowNotificationManager>("ControlNotifications").Show("Notification clicked");
        }
    }
}
