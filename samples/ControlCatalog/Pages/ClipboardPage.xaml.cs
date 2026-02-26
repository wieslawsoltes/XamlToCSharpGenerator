using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ControlCatalog.Pages
{
    public partial class ClipboardPage : UserControl
    {
        private readonly DataFormat<byte[]> _customBinaryDataFormat =
            DataFormat.CreateBytesApplicationFormat("controlcatalog-binary-data");

        private INotificationManager? _notificationManager;
        private INotificationManager NotificationManager => _notificationManager
            ??= new WindowNotificationManager(TopLevel.GetTopLevel(this)!);

        private readonly DispatcherTimer _clipboardLastDataObjectChecker;
        private DataTransfer? _storedDataTransfer;
        private bool _checkingClipboardDataTransfer;
        private Bitmap _defaultImage;

        private Run OwnsClipboardDataObjectRun =>
            OwnsClipboardDataObject ?? throw new InvalidOperationException("Named control OwnsClipboardDataObject was not initialized.");

        private Image ClipboardImageView =>
            ClipboardImage ?? throw new InvalidOperationException("Named control ClipboardImage was not initialized.");

        private TextBox ClipboardContentBox =>
            ClipboardContent ?? throw new InvalidOperationException("Named control ClipboardContent was not initialized.");

        public ClipboardPage()
        {
            InitializeComponent();

            _clipboardLastDataObjectChecker =
                new DispatcherTimer(TimeSpan.FromSeconds(0.5), default, CheckLastDataObject);

            using var asset = AssetLoader.Open(new Uri("avares://ControlCatalog/Assets/image1.jpg"));
            _defaultImage = new Bitmap(asset);
            ClipboardImageView.Source = _defaultImage;
        }

        private void InitializeComponent()
        {
            InitializeComponent(true);
        }

        private async void CopyText(object? sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(ClipboardContentBox.Text ?? string.Empty);
        }

        private async void CopyImage(object? sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetValueAsync(DataFormat.Bitmap, _defaultImage);
        }

        private async void PasteText(object? sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                ClipboardContentBox.Text = await clipboard.TryGetTextAsync();
            }
        }

        private async void PasteImage(object? sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                using var data = await clipboard.TryGetDataAsync();
                Bitmap? source = null;
                if (data != null)
                {
                    source = await data!.TryGetValueAsync(DataFormat.Bitmap);
                }
                ClipboardImageView.Source = source;
            }
        }

        private async void CopyFiles(object? sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                var storageProvider = TopLevel.GetTopLevel(this)!.StorageProvider;
                var filesPath = (ClipboardContentBox.Text ?? string.Empty)
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                if (filesPath.Length == 0)
                {
                    return;
                }
                List<string> invalidFile = new(filesPath.Length);
                List<IStorageFile> files = new(filesPath.Length);

                for (int i = 0; i < filesPath.Length; i++)
                {
                    var file = await storageProvider.TryGetFileFromPathAsync(filesPath[i]);
                    if (file is null)
                    {
                        invalidFile.Add(filesPath[i]);
                    }
                    else
                    {
                        files.Add(file);
                    }
                }

                if (invalidFile.Count > 0)
                {
                    NotificationManager.Show(new Notification("Warning", "There is one o more invalid path.", NotificationType.Warning));
                }

                if (files.Count > 0)
                {
                    var dataTransfer = _storedDataTransfer = new DataTransfer();
                    foreach (var file in files)
                        dataTransfer.Add(DataTransferItem.Create(DataFormat.File, file));
                    await clipboard.SetDataAsync(dataTransfer);
                    NotificationManager.Show(new Notification("Success", "Copy completed.", NotificationType.Success));
                }
                else
                {
                    NotificationManager.Show(new Notification("Warning", "Any files to copy in Clipboard.", NotificationType.Warning));
                }
            }
        }

        private async void PasteFiles(object? sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                var files = await clipboard.TryGetFilesAsync();

                ClipboardContentBox.Text = files != null ? string.Join(Environment.NewLine, files.Select(f => f.TryGetLocalPath() ?? f.Name)) : string.Empty;
            }
        }

        private async void GetFormats(object sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                var formats = await clipboard.GetDataFormatsAsync();
                ClipboardContentBox.Text = string.Join(Environment.NewLine, formats);
            }
        }

        private async void CopyBinaryData(object? sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                var dataTransfer = _storedDataTransfer = new DataTransfer();
                var bytes = new byte[10 * 1024 * 1024];
                new Random().NextBytes(bytes);
                dataTransfer.Add(DataTransferItem.Create(_customBinaryDataFormat, bytes));
                await clipboard.SetDataAsync(dataTransfer);
            }
        }

        private async void PasteBinaryData(object? sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                var bytes = await clipboard.TryGetValueAsync(_customBinaryDataFormat);
                ClipboardContentBox.Text = bytes is null ? "<null>" : $"{bytes.Length} bytes";
            }
        }

        private async void Clear(object sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.ClearAsync();
            }

        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _clipboardLastDataObjectChecker.Start();
            base.OnAttachedToVisualTree(e);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _clipboardLastDataObjectChecker.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        private async void CheckLastDataObject(object? sender, EventArgs e)
        {
            if(_checkingClipboardDataTransfer)
                return;
            try
            {
                _checkingClipboardDataTransfer = true;

                var owns = false;
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    var dataTransfer = await clipboard.TryGetInProcessDataAsync();
                    owns = dataTransfer == _storedDataTransfer && dataTransfer is not null;
                }

                OwnsClipboardDataObjectRun.Text = owns ? "Yes" : "No";
                OwnsClipboardDataObjectRun.Foreground = owns ? Brushes.Green : Brushes.Red;
            }
            finally
            {
                _checkingClipboardDataTransfer = false;
            }
        }
    }
}
