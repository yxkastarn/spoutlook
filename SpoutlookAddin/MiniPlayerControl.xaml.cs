using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SpoutlookAddin
{
    /// <summary>
    /// Code-behind for <see cref="MiniPlayerControl"/>.
    ///
    /// Responsibilities:
    ///   • Show login / player panels depending on auth state.
    ///   • Poll <c>GET /me/player/currently-playing</c> every ~2 s.
    ///   • Update track name, artist, album art, progress bar and volume.
    ///   • Forward button clicks to <see cref="SpotifyApiClient"/>.
    ///   • Surface <see cref="SpotifyApiException"/> errors to the user.
    /// </summary>
    public partial class MiniPlayerControl : UserControl, IDisposable
    {
        private readonly SpotifyAuth      _auth;
        private readonly SpotifyApiClient _api;

        private readonly DispatcherTimer  _pollTimer;
        private readonly HttpClient       _imageClient = new HttpClient();
        private bool _disposed;

        // Prevents seek-bar value-changed events from triggering seeks while we
        // are updating it programmatically.
        private bool _suppressSliderEvents;
        private bool _userDraggingProgress;
        private bool _isPlaying;

        public MiniPlayerControl(SpotifyAuth auth, SpotifyApiClient api)
        {
            _auth = auth;
            _api  = api;

            InitializeComponent();

            _auth.TokensUpdated += OnTokensUpdated;

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            _pollTimer.Tick += async (_, __) => await PollSafeAsync();

            UpdatePanelVisibility();
        }

        // ------------------------------------------------------------------ //
        //  IDisposable
        // ------------------------------------------------------------------ //

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pollTimer.Stop();
            _imageClient.Dispose();
        }

        // ------------------------------------------------------------------ //
        //  Public API called by MiniPlayerHostControl
        // ------------------------------------------------------------------ //

        public void StartPolling() => _pollTimer.Start();
        public void StopPolling()  => _pollTimer.Stop();

        // ------------------------------------------------------------------ //
        //  Auth events
        // ------------------------------------------------------------------ //

        private void OnTokensUpdated(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdatePanelVisibility();
                if (_auth.IsAuthenticated)
                    _pollTimer.Start();
                else
                    _pollTimer.Stop();
            });
        }

        private void UpdatePanelVisibility()
        {
            bool loggedIn = _auth.IsAuthenticated;
            LoginPanel.Visibility  = loggedIn ? Visibility.Collapsed : Visibility.Visible;
            PlayerPanel.Visibility = loggedIn ? Visibility.Visible   : Visibility.Collapsed;
            BtnLogout.Visibility   = loggedIn ? Visibility.Visible   : Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ //
        //  Login / logout buttons
        // ------------------------------------------------------------------ //

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            BtnLogin.IsEnabled        = false;
            TxtLoginStatus.Text       = "Opening Spotify login in your browser…";
            TxtLoginStatus.Visibility = Visibility.Visible;

            var success = await _auth.StartLoginAsync();

            if (!success)
            {
                TxtLoginStatus.Text = "Login failed or timed out. Please try again.";
                BtnLogin.IsEnabled  = true;
            }
            // On success, OnTokensUpdated() will update the UI.
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            _auth.Logout();
            ResetPlayerState();
        }

        // ------------------------------------------------------------------ //
        //  Playback controls
        // ------------------------------------------------------------------ //

        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            await RunApiCallAsync(_isPlaying ? _api.PauseAsync() : _api.PlayAsync());

            // Optimistic UI update – confirmed by next poll tick.
            _isPlaying = !_isPlaying;
            UpdatePlayPauseIcon();
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e) =>
            await RunApiCallAsync(_api.PreviousAsync());

        private async void BtnNext_Click(object sender, RoutedEventArgs e) =>
            await RunApiCallAsync(_api.NextAsync());

        // ------------------------------------------------------------------ //
        //  Volume slider
        // ------------------------------------------------------------------ //

        private async void SliderVolume_ValueChanged(
            object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvents) return;
            await RunApiCallAsync(_api.SetVolumeAsync((int)e.NewValue));
        }

        // ------------------------------------------------------------------ //
        //  Progress slider
        // ------------------------------------------------------------------ //

        private void SliderProgress_MouseDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e) =>
            _userDraggingProgress = true;

        private async void SliderProgress_MouseUp(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            _userDraggingProgress = false;
            if (_currentTrackDurationMs > 0)
            {
                var posMs = (int)(SliderProgress.Value / 100.0 * _currentTrackDurationMs);
                await RunApiCallAsync(_api.SeekAsync(posMs));
            }
        }

        private void SliderProgress_ValueChanged(
            object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvents || !_userDraggingProgress) return;
            // Update time label while dragging.
            var posMs = (int)(e.NewValue / 100.0 * _currentTrackDurationMs);
            TxtPosition.Text = FormatMs(posMs);
        }

        // ------------------------------------------------------------------ //
        //  Polling
        // ------------------------------------------------------------------ //

        private int    _currentTrackDurationMs;
        private string _currentAlbumArtUrl = "";

        private async Task PollSafeAsync()
        {
            try
            {
                await RefreshNowPlayingAsync();
            }
            catch (SpotifyApiException ex)
            {
                TxtDevice.Text = $"⚠ {ex.Message}";
            }
            catch
            {
                // Transient network errors are silently ignored between polls.
            }
        }

        private async Task RefreshNowPlayingAsync()
        {
            var state = await _api.GetCurrentlyPlayingAsync();

            if (state?.Item == null)
            {
                TxtTrackName.Text  = "Not playing";
                TxtArtistName.Text = "–";
                return;
            }

            _isPlaying = state.IsPlaying;
            UpdatePlayPauseIcon();

            // Track metadata.
            TxtTrackName.Text  = state.Item.Name;
            TxtArtistName.Text = string.Join(", ", Array.ConvertAll(
                state.Item.Artists, a => a.Name));

            // Album art (only fetch when track changes).
            var artUrl = state.Item.Album?.Images.Length > 0
                ? state.Item.Album.Images[0].Url
                : "";

            if (artUrl != _currentAlbumArtUrl)
            {
                _currentAlbumArtUrl = artUrl;
                await LoadAlbumArtAsync(artUrl);
            }

            // Progress.
            if (!_userDraggingProgress)
            {
                _currentTrackDurationMs = state.Item.DurationMs;

                _suppressSliderEvents = true;
                SliderProgress.Value = _currentTrackDurationMs > 0
                    ? (double)state.ProgressMs / _currentTrackDurationMs * 100.0
                    : 0;
                _suppressSliderEvents = false;

                TxtPosition.Text = FormatMs(state.ProgressMs);
                TxtDuration.Text = FormatMs(_currentTrackDurationMs);
            }

            // Volume.
            if (state.Device?.VolumePercent is int vol)
            {
                _suppressSliderEvents = true;
                SliderVolume.Value    = vol;
                _suppressSliderEvents = false;
            }

            // Device.
            TxtDevice.Text = state.Device != null
                ? $"🔊 {state.Device.Name}"
                : "";
        }

        private async Task LoadAlbumArtAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                ImgAlbumArt.Source = null;
                return;
            }

            try
            {
                var bytes = await _imageClient.GetByteArrayAsync(url);
                var bmp   = new BitmapImage();
                using var stream = new System.IO.MemoryStream(bytes);
                bmp.BeginInit();
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                ImgAlbumArt.Source = bmp;
            }
            catch
            {
                ImgAlbumArt.Source = null;
            }
        }

        // ------------------------------------------------------------------ //
        //  Helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Runs an API call and shows any <see cref="SpotifyApiException"/> in the
        /// device label so the user gets immediate feedback.
        /// </summary>
        private async Task RunApiCallAsync(Task apiCall)
        {
            try
            {
                await apiCall;
            }
            catch (SpotifyApiException ex)
            {
                TxtDevice.Text = $"⚠ {ex.Message}";
            }
        }

        private void UpdatePlayPauseIcon()
        {
            IcoPlay.Visibility  = _isPlaying ? Visibility.Collapsed : Visibility.Visible;
            IcoPause.Visibility = _isPlaying ? Visibility.Visible   : Visibility.Collapsed;
        }

        private void ResetPlayerState()
        {
            TxtTrackName.Text  = "Not playing";
            TxtArtistName.Text = "–";
            ImgAlbumArt.Source = null;
            TxtPosition.Text   = "0:00";
            TxtDuration.Text   = "0:00";
            SliderProgress.Value = 0;
            _currentAlbumArtUrl  = "";
        }

        private static string FormatMs(int ms)
        {
            var ts = TimeSpan.FromMilliseconds(ms);
            return ts.Hours > 0
                ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }
}
