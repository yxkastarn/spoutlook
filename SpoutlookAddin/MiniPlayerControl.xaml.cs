using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SpoutlookAddin
{
    /// <summary>
    /// Code-behind for <see cref="MiniPlayerControl"/>.
    ///
    /// Responsibilities:
    ///   • Switch between Spotify and TuneIn sources.
    ///   • Spotify: show login / player panels depending on auth state.
    ///     Poll <c>GET /me/player/currently-playing</c> every ~2 s.
    ///   • TuneIn: browse popular stations, search by name, stream audio.
    /// </summary>
    public partial class MiniPlayerControl : UserControl, IDisposable
    {
        // ------------------------------------------------------------------ //
        //  Fields – Spotify
        // ------------------------------------------------------------------ //

        private readonly SpotifyAuth      _auth;
        private readonly SpotifyApiClient _api;
        private readonly DispatcherTimer  _pollTimer;
        private readonly HttpClient       _imageClient = new HttpClient();
        private bool _disposed;
        private bool _suppressSliderEvents;
        private bool _userDraggingProgress;
        private bool _isPlaying;
        private int    _currentTrackDurationMs;
        private string _currentAlbumArtUrl = "";

        // ------------------------------------------------------------------ //
        //  Fields – TuneIn
        // ------------------------------------------------------------------ //

        private readonly TuneInClient      _tuneIn;
        private          DispatcherTimer?  _tuneInSearchTimer;
        private          string            _lastSearchText = "";

        // ------------------------------------------------------------------ //
        //  Fields – shared
        // ------------------------------------------------------------------ //

        /// <summary>Currently selected source: "spotify" or "tunein".</summary>
        private string _source = "spotify";

        // ------------------------------------------------------------------ //
        //  Constructor
        // ------------------------------------------------------------------ //

        public MiniPlayerControl(SpotifyAuth auth, SpotifyApiClient api)
        {
            _auth   = auth;
            _api    = api;
            _tuneIn = new TuneInClient();

            InitializeComponent();

            _auth.TokensUpdated += OnTokensUpdated;

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            _pollTimer.Tick += async (_, __) => await PollSafeAsync();

            // Start with Spotify source selected
            ApplySourceSelection();
            UpdateSpotifyPanelVisibility();

            // Load TuneIn popular stations in the background
            _ = LoadPopularStationsAsync();
        }

        // ------------------------------------------------------------------ //
        //  IDisposable
        // ------------------------------------------------------------------ //

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pollTimer.Stop();
            _tuneInSearchTimer?.Stop();
            _imageClient.Dispose();
            _tuneIn.Dispose();
            StopTuneIn();
        }

        // ------------------------------------------------------------------ //
        //  Public API called by MiniPlayerHostControl
        // ------------------------------------------------------------------ //

        public void StartPolling() { if (_source == "spotify") _pollTimer.Start(); }
        public void StopPolling()  => _pollTimer.Stop();

        // ------------------------------------------------------------------ //
        //  Source selector
        // ------------------------------------------------------------------ //

        private void BtnSourceSpotify_Click(object sender, RoutedEventArgs e)
        {
            if (_source == "spotify") return;
            _source = "spotify";
            ApplySourceSelection();
            // Resume polling if logged in
            if (_auth.IsAuthenticated) _pollTimer.Start();
        }

        private void BtnSourceTuneIn_Click(object sender, RoutedEventArgs e)
        {
            if (_source == "tunein") return;
            _source = "tunein";
            ApplySourceSelection();
            _pollTimer.Stop();
        }

        /// <summary>
        /// Updates button appearance and panel visibility to match <see cref="_source"/>.
        /// </summary>
        private void ApplySourceSelection()
        {
            bool isSpotify = _source == "spotify";

            // Spotify button: active = green background, black text
            BtnSourceSpotify.Background = isSpotify
                ? (Brush)new SolidColorBrush(Color.FromRgb(0x1D, 0xB9, 0x54))
                : Brushes.Transparent;
            BtnSourceSpotify.Foreground = isSpotify
                ? Brushes.Black
                : (Brush)FindResource("TextSecondary");
            BtnSourceSpotify.BorderBrush = isSpotify
                ? Brushes.Transparent
                : (Brush)new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));

            // TuneIn button: active = blue background, black text
            BtnSourceTuneIn.Background = !isSpotify
                ? (Brush)new SolidColorBrush(Color.FromRgb(0x00, 0xA0, 0xE3))
                : Brushes.Transparent;
            BtnSourceTuneIn.Foreground = !isSpotify
                ? Brushes.Black
                : (Brush)FindResource("TextSecondary");
            BtnSourceTuneIn.BorderBrush = !isSpotify
                ? Brushes.Transparent
                : (Brush)new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));

            // Show/hide content sections
            SpotifySection.Visibility = isSpotify ? Visibility.Visible : Visibility.Collapsed;
            TuneInSection.Visibility  = isSpotify ? Visibility.Collapsed : Visibility.Visible;
        }

        // ------------------------------------------------------------------ //
        //  Spotify – auth events
        // ------------------------------------------------------------------ //

        private void OnTokensUpdated(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateSpotifyPanelVisibility();
                if (_auth.IsAuthenticated && _source == "spotify")
                    _pollTimer.Start();
                else
                    _pollTimer.Stop();
            });
        }

        private void UpdateSpotifyPanelVisibility()
        {
            bool loggedIn = _auth.IsAuthenticated;
            LoginPanel.Visibility  = loggedIn ? Visibility.Collapsed : Visibility.Visible;
            PlayerPanel.Visibility = loggedIn ? Visibility.Visible   : Visibility.Collapsed;
            BtnLogout.Visibility   = loggedIn ? Visibility.Visible   : Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ //
        //  Spotify – login / logout
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
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            _auth.Logout();
            ResetPlayerState();
        }

        // ------------------------------------------------------------------ //
        //  Spotify – playback controls
        // ------------------------------------------------------------------ //

        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            await RunApiCallAsync(_isPlaying ? _api.PauseAsync() : _api.PlayAsync());
            _isPlaying = !_isPlaying;
            UpdatePlayPauseIcon();
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e) =>
            await RunApiCallAsync(_api.PreviousAsync());

        private async void BtnNext_Click(object sender, RoutedEventArgs e) =>
            await RunApiCallAsync(_api.NextAsync());

        // ------------------------------------------------------------------ //
        //  Spotify – volume / progress sliders
        // ------------------------------------------------------------------ //

        private async void SliderVolume_ValueChanged(
            object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvents) return;
            await RunApiCallAsync(_api.SetVolumeAsync((int)e.NewValue));
        }

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
            var posMs = (int)(e.NewValue / 100.0 * _currentTrackDurationMs);
            TxtPosition.Text = FormatMs(posMs);
        }

        // ------------------------------------------------------------------ //
        //  Spotify – polling
        // ------------------------------------------------------------------ //

        private async Task PollSafeAsync()
        {
            try   { await RefreshNowPlayingAsync(); }
            catch (SpotifyApiException ex) { TxtDevice.Text = $"⚠ {ex.Message}"; }
            catch { /* Transient network errors ignored between polls */ }
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

            TxtTrackName.Text  = state.Item.Name;
            TxtArtistName.Text = string.Join(", ", Array.ConvertAll(
                state.Item.Artists, a => a.Name));

            var artUrl = state.Item.Album?.Images.Length > 0
                ? state.Item.Album.Images[0].Url : "";

            if (artUrl != _currentAlbumArtUrl)
            {
                _currentAlbumArtUrl = artUrl;
                await LoadAlbumArtAsync(artUrl);
            }

            if (!_userDraggingProgress)
            {
                _currentTrackDurationMs = state.Item.DurationMs;
                _suppressSliderEvents   = true;
                SliderProgress.Value    = _currentTrackDurationMs > 0
                    ? (double)state.ProgressMs / _currentTrackDurationMs * 100.0 : 0;
                _suppressSliderEvents   = false;
                TxtPosition.Text = FormatMs(state.ProgressMs);
                TxtDuration.Text = FormatMs(_currentTrackDurationMs);
            }

            if (state.Device?.VolumePercent is int vol)
            {
                _suppressSliderEvents  = true;
                SliderVolume.Value     = vol;
                _suppressSliderEvents  = false;
            }

            TxtDevice.Text = state.Device != null ? $"🔊 {state.Device.Name}" : "";
        }

        private async Task LoadAlbumArtAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) { ImgAlbumArt.Source = null; return; }
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
            catch { ImgAlbumArt.Source = null; }
        }

        // ------------------------------------------------------------------ //
        //  TuneIn – station loading
        // ------------------------------------------------------------------ //

        private async Task LoadPopularStationsAsync()
        {
            try
            {
                var stations = await _tuneIn.GetTopStationsAsync();
                Dispatcher.Invoke(() => PopulateStationList(stations));
            }
            catch
            {
                // Non-critical; list stays empty if the network is unavailable.
            }
        }

        private void PopulateStationList(IReadOnlyList<RadioStation> stations)
        {
            LstStations.ItemsSource = stations;
        }

        // ------------------------------------------------------------------ //
        //  TuneIn – search
        // ------------------------------------------------------------------ //

        private void TxtTuneInSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = TxtTuneInSearch.Text;

            // Show/hide placeholder hint
            TxtTuneInSearchHint.Visibility =
                string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

            // Debounce: restart 400 ms timer on every keystroke
            _tuneInSearchTimer?.Stop();
            _tuneInSearchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400),
            };
            _tuneInSearchTimer.Tick += async (_, __) =>
            {
                _tuneInSearchTimer!.Stop();
                if (text == _lastSearchText) return;
                _lastSearchText = text;

                if (string.IsNullOrWhiteSpace(text))
                    await LoadPopularStationsAsync();
                else
                    await SearchStationsAsync(text);
            };
            _tuneInSearchTimer.Start();
        }

        private async Task SearchStationsAsync(string query)
        {
            try
            {
                var results = await _tuneIn.SearchStationsAsync(query);
                Dispatcher.Invoke(() => PopulateStationList(results));
            }
            catch
            {
                // Search failed silently; list keeps previous results.
            }
        }

        // ------------------------------------------------------------------ //
        //  TuneIn – playback
        // ------------------------------------------------------------------ //

        private void LstStations_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstStations.SelectedItem is RadioStation station)
                PlayStation(station);
        }

        private void PlayStation(RadioStation station)
        {
            var streamUrl = station.StreamUrl;
            if (string.IsNullOrWhiteSpace(streamUrl)) return;

            // Only allow http/https streams
            if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
                return;

            TuneInMedia.Source = uri;
            TuneInMedia.Play();

            TxtTuneInNowPlaying.Text      = station.Name;
            TuneInNowPlayingBar.Visibility = Visibility.Visible;
        }

        private void BtnTuneInStop_Click(object sender, RoutedEventArgs e) => StopTuneIn();

        private void StopTuneIn()
        {
            TuneInMedia.Stop();
            TuneInMedia.Source            = null;
            TuneInNowPlayingBar.Visibility = Visibility.Collapsed;
            LstStations.SelectedItem      = null;
        }

        private void TuneInMedia_MediaFailed(object sender,
            ExceptionRoutedEventArgs e)
        {
            TuneInNowPlayingBar.Visibility = Visibility.Collapsed;
            LstStations.SelectedItem      = null;
            // Optionally surface the error to the user here.
        }

        // ------------------------------------------------------------------ //
        //  Helpers – Spotify
        // ------------------------------------------------------------------ //

        private async Task RunApiCallAsync(Task apiCall)
        {
            try   { await apiCall; }
            catch (SpotifyApiException ex) { TxtDevice.Text = $"⚠ {ex.Message}"; }
        }

        private void UpdatePlayPauseIcon()
        {
            IcoPlay.Visibility  = _isPlaying ? Visibility.Collapsed : Visibility.Visible;
            IcoPause.Visibility = _isPlaying ? Visibility.Visible   : Visibility.Collapsed;
        }

        private void ResetPlayerState()
        {
            TxtTrackName.Text    = "Not playing";
            TxtArtistName.Text   = "–";
            ImgAlbumArt.Source   = null;
            TxtPosition.Text     = "0:00";
            TxtDuration.Text     = "0:00";
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
