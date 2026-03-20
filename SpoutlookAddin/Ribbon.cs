using System.IO;
using System.Reflection;
using Microsoft.Office.Core;

// Tell Office to use the XML-based ribbon definition in Ribbon.xml.
[assembly: System.Runtime.InteropServices.ComVisible(true)]

namespace SpoutlookAddin
{
    /// <summary>
    /// Implements <see cref="IRibbonExtensibility"/> to inject a Spotify group
    /// into the Outlook "Mail" tab containing a "Mini Player" toggle button.
    /// </summary>
    [System.Runtime.InteropServices.ComVisible(true)]
    public sealed class Ribbon : IRibbonExtensibility
    {
        private IRibbonUI? _ribbon;
        private readonly ThisAddIn _addIn;

        public Ribbon(ThisAddIn addIn)
        {
            _addIn = addIn;
        }

        // ------------------------------------------------------------------ //
        //  IRibbonExtensibility
        // ------------------------------------------------------------------ //

        public string GetCustomUI(string ribbonId)
        {
            // Load Ribbon.xml embedded alongside this assembly.
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(
                "SpoutlookAddin.Ribbon.xml");
            if (stream == null) return "";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // ------------------------------------------------------------------ //
        //  Ribbon callbacks
        // ------------------------------------------------------------------ //

        public void Ribbon_Load(IRibbonUI ribbonUI)
        {
            _ribbon = ribbonUI;
        }

        public void BtnTogglePlayer_Click(IRibbonControl control, bool pressed)
        {
            _addIn.ToggleTaskPane();
        }

        public bool BtnTogglePlayer_GetPressed(IRibbonControl control)
        {
            // Reflect the actual pane visibility so the button stays in sync.
            return _addIn.IsTaskPaneVisible;
        }

        /// <summary>Call to force the ribbon to re-query the button state.</summary>
        public void Invalidate() => _ribbon?.Invalidate();
    }
}
