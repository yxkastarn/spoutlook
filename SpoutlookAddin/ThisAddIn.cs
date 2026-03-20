using System;
using Microsoft.Office.Core;
using Microsoft.Office.Tools;

namespace SpoutlookAddin
{
    /// <summary>
    /// Main VSTO add-in class. Bootstraps the Spotify mini-player and registers
    /// the Custom Task Pane that hosts it inside Classic Outlook.
    /// Also implements <see cref="IRibbonExtensibility"/> to inject the Spotify
    /// ribbon group into the Outlook Mail tab.
    /// </summary>
    public partial class ThisAddIn
    {
        // ------------------------------------------------------------------ //
        //  Fields
        // ------------------------------------------------------------------ //

        private SpotifyAuth       _auth      = null!;
        private SpotifyApiClient  _apiClient = null!;
        private Ribbon            _ribbon    = null!;

        /// <summary>Host control (WinForms ElementHost wrapping the WPF player).</summary>
        private MiniPlayerHostControl _hostControl = null!;

        /// <summary>VSTO Custom Task Pane shown inside Outlook.</summary>
        private CustomTaskPane? _taskPane;

        // ------------------------------------------------------------------ //
        //  VSTO lifecycle
        // ------------------------------------------------------------------ //

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            _auth      = new SpotifyAuth();
            _apiClient = new SpotifyApiClient(_auth);

            _hostControl = new MiniPlayerHostControl(_auth, _apiClient);
            _taskPane    = CustomTaskPanes.Add(_hostControl, "Spotify");
            _taskPane.Width   = 280;
            _taskPane.Visible = false;

            // Invalidate ribbon button when pane visibility changes.
            _taskPane.VisibleChanged += (_, __) => _ribbon?.Invalidate();

            // Restore previously saved tokens so the user stays logged in.
            _auth.LoadTokensFromStorage();
            if (_auth.IsAuthenticated)
                _hostControl.StartPolling();
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
            _hostControl?.StopPolling();
            _taskPane?.Dispose();
            _apiClient?.Dispose();
            _auth?.Dispose();
        }

        // ------------------------------------------------------------------ //
        //  Public helpers called by the Ribbon
        // ------------------------------------------------------------------ //

        /// <summary>Toggles the mini-player pane on or off.</summary>
        public void ToggleTaskPane()
        {
            if (_taskPane != null)
                _taskPane.Visible = !_taskPane.Visible;
        }

        /// <summary>Whether the mini-player task pane is currently visible.</summary>
        public bool IsTaskPaneVisible => _taskPane?.Visible ?? false;

        // ------------------------------------------------------------------ //
        //  IRibbonExtensibility (Office calls this to load our ribbon XML)
        // ------------------------------------------------------------------ //

        protected override IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            _ribbon = new Ribbon(this);
            return _ribbon;
        }

        // ------------------------------------------------------------------ //
        //  VSTO-generated region (do not delete)
        // ------------------------------------------------------------------ //

        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup  += new EventHandler(ThisAddIn_Startup);
            this.Shutdown += new EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
