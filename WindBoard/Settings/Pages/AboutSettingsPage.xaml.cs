using System;
using System.Threading;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Settings.Pages
{
    public sealed partial class AboutSettingsPage : Page
    {
        // 说明：该页面逻辑按职责拆分到多个 partial 文件，避免单文件过大难维护。
        private bool _isSyncingUiFromSettings;
        private readonly MultiTapGestureDetector _debugUnlockTapDetector = new(requiredTaps: 5, maxInterval: TimeSpan.FromMilliseconds(800));
        private int _debugUnlockInfoNonce;
        private bool _isCheckingUpdates;
        private CancellationTokenSource? _checkUpdatesCts;
        private bool _isTestingDownloadSource;
        private CancellationTokenSource? _downloadSourceTestCts;

        public AboutSettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }
    }
}
