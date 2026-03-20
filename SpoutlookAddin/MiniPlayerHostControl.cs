using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace SpoutlookAddin
{
    /// <summary>
    /// WinForms <see cref="UserControl"/> that wraps the WPF <see cref="MiniPlayerControl"/>
    /// via an <see cref="ElementHost"/>. VSTO Custom Task Panes require a WinForms control
    /// as their host, so this intermediate wrapper is necessary.
    /// </summary>
    public sealed class MiniPlayerHostControl : UserControl
    {
        private readonly MiniPlayerControl _wpfControl;

        public MiniPlayerHostControl(SpotifyAuth auth, SpotifyApiClient apiClient)
        {
            _wpfControl = new MiniPlayerControl(auth, apiClient);

            var host = new ElementHost
            {
                Dock  = DockStyle.Fill,
                Child = _wpfControl,
            };

            Controls.Add(host);
            Dock = DockStyle.Fill;
        }

        /// <summary>Starts the background polling timer.</summary>
        public void StartPolling() => _wpfControl.StartPolling();

        /// <summary>Stops the background polling timer.</summary>
        public void StopPolling()  => _wpfControl.StopPolling();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _wpfControl.Dispose();
            base.Dispose(disposing);
        }
    }
}
